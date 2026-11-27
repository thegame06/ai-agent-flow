using System.Text;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Collections;
using AgentFlow.Api.Commerce;
using AgentFlow.Api.Entitlements;
using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>Collections portfolio: customers and their loans, imported from the lender.</summary>
[ApiController]
[RequiresAppModule(AppModules.Collections)]
[Authorize]
[Route("api/v1/tenants/{tenantId}/collections")]
public sealed class CollectionsController : ControllerBase
{
    private const int MaxBatchRows = 5000;
    private const long MaxCsvBytes = 20 * 1024 * 1024;

    private readonly ICollectionsStore _store;
    private readonly ICollectionsImportService _imports;
    private readonly ICommerceStore _parties;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ICollectionPromiseService _promises;
    private readonly ICampaignStore _campaigns;
    private readonly ICollectionPaymentService _payments;

    public CollectionsController(
        ICollectionsStore store,
        ICollectionsImportService imports,
        ICommerceStore parties,
        ITenantContextAccessor tenantContext,
        ICollectionPromiseService promises,
        ICampaignStore campaigns,
        ICollectionPaymentService payments)
    {
        _payments = payments;
        _store = store;
        _imports = imports;
        _parties = parties;
        _tenantContext = tenantContext;
        _promises = promises;
        _campaigns = campaigns;
    }

    /// <summary>Import a portfolio CSV (one row per loan). Use dryRun=true to preview and validate without saving.</summary>
    [HttpPost("imports/csv")]
    [RequestSizeLimit(MaxCsvBytes)]
    public async Task<IActionResult> ImportCsv([FromRoute] string tenantId, [FromForm] CollectionsCsvImportForm form, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        if (form.File is null || form.File.Length == 0) return BadRequest(new { message = "Adjunte un archivo CSV." });

        string content;
        using (var reader = new StreamReader(form.File.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            content = await reader.ReadToEndAsync(ct);

        var parsed = CollectionsCsvParser.Parse(content);
        var result = await _imports.ImportAsync(
            tenantId, parsed.Rows, parsed.Errors, parsed.TotalRows, form.SnapshotDate,
            "csv", form.File.FileName, form.DryRun, _tenantContext.Current!.UserId, ct);
        return Ok(result);
    }

    /// <summary>Import loans from another system (up to 5,000 rows per call).</summary>
    [HttpPost("accounts/batch")]
    public async Task<IActionResult> ImportBatch([FromRoute] string tenantId, [FromBody] CollectionsBatchImportRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        if (request.Rows.Count == 0) return BadRequest(new { message = "rows is required." });
        if (request.Rows.Count > MaxBatchRows) return BadRequest(new { message = $"Max {MaxBatchRows} rows per batch." });

        var rows = request.Rows.Select((row, i) => (i + 1, row)).ToList();
        var result = await _imports.ImportAsync(
            tenantId, rows, [], rows.Count, request.SnapshotDate, "api", null, request.DryRun, _tenantContext.Current!.UserId, ct);
        return Ok(result);
    }

    [HttpGet("imports/template")]
    public IActionResult GetTemplate([FromRoute] string tenantId)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(CollectionsCsvParser.BuildTemplate())).ToArray();
        return File(bytes, "text/csv", "plantilla-cartera.csv");
    }

    [HttpGet("imports")]
    public async Task<IActionResult> GetImports([FromRoute] string tenantId, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetImportBatchesAsync(tenantId, Math.Clamp(limit, 1, 100), ct));
    }

    /// <summary>
    /// Recovery in a period: money that came back, how much of it followed a campaign contact,
    /// the contact funnel, promises and the breakdown by campaign, stage and day.
    /// <paramref name="from"/> and <paramref name="to"/> are days (yyyy-MM-dd, UTC, inclusive); default: the last 30 days.
    /// </summary>
    [HttpGet("recovery")]
    public async Task<IActionResult> GetRecovery(
        [FromRoute] string tenantId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromServices] ICollectionsRecoveryService recovery,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var lastDay = (to ?? DateTime.UtcNow).Date;
        var firstDay = (from ?? lastDay.AddDays(-29)).Date;
        if (firstDay > lastDay) return BadRequest(new { message = "La fecha inicial es posterior a la final." });
        if ((lastDay - firstDay).TotalDays > 366) return BadRequest(new { message = "El periodo puede ser de hasta un año." });

        var start = new DateTimeOffset(DateTime.SpecifyKind(firstDay, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(lastDay.AddDays(1).AddTicks(-1), DateTimeKind.Utc));
        return Ok(await recovery.GetReportAsync(tenantId, start, end, ct));
    }

    /// <summary>How customers pay: payment methods and which reference they quote. The assistant reads it.</summary>
    [HttpGet("payment-settings")]
    public async Task<IActionResult> GetPaymentSettings([FromRoute] string tenantId, [FromServices] ICollectionPaymentDeskStore desk, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await desk.GetSettingsAsync(tenantId, ct) ?? new CollectionPaymentSettingsDocument { TenantId = tenantId });
    }

    [HttpPut("payment-settings")]
    public async Task<IActionResult> SavePaymentSettings(
        [FromRoute] string tenantId,
        [FromBody] SaveCollectionPaymentSettingsRequest request,
        [FromServices] ICollectionPaymentDeskStore desk,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var methods = (request.Methods ?? []).Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();
        if (methods.Count > 30) return BadRequest(new { message = "Maximo 30 medios de pago." });
        foreach (var m in methods)
        {
            m.Id = string.IsNullOrWhiteSpace(m.Id) ? Guid.NewGuid().ToString("N")[..8] : m.Id;
            m.Name = m.Name.Trim();
            m.Type = m.Type is "bank_transfer" or "cash" or "app" or "other" ? m.Type : "other";
            m.Currency = string.IsNullOrWhiteSpace(m.Currency) ? null : m.Currency.Trim().ToUpperInvariant();
        }

        var settings = new CollectionPaymentSettingsDocument
        {
            TenantId = tenantId,
            Methods = methods,
            ReferenceKind = request.ReferenceKind == "customer" ? "customer" : "loan",
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = _tenantContext.Current!.UserId
        };
        await desk.SaveSettingsAsync(settings, ct);
        return Ok(settings);
    }

    /// <summary>Payments customers said they made (through the assistant), waiting for a person.</summary>
    [HttpGet("payment-reports")]
    public async Task<IActionResult> GetPaymentReports(
        [FromRoute] string tenantId,
        [FromQuery] CollectionPaymentReportStatus? status,
        [FromServices] ICollectionPaymentDeskStore desk,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var reports = await desk.GetReportsAsync(tenantId, status, 200, ct);
        var pending = status == CollectionPaymentReportStatus.Pending
            ? reports.Count
            : (await desk.GetReportsAsync(tenantId, CollectionPaymentReportStatus.Pending, 1000, ct)).Count;
        return Ok(new { items = reports, pending });
    }

    [HttpPost("payment-reports/{reportId}/confirm")]
    public async Task<IActionResult> ConfirmPaymentReport(
        [FromRoute] string tenantId,
        [FromRoute] string reportId,
        [FromBody] ConfirmPaymentReportRequest? request,
        [FromServices] ICollectionPaymentReportService reports,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        var result = await reports.ConfirmAsync(tenantId, reportId, request?.LoanId, request?.Amount, request?.PaidAt, _tenantContext.Current!.UserId, ct);
        return result.Ok ? Ok(result) : UnprocessableEntity(result);
    }

    [HttpPost("payment-reports/{reportId}/reject")]
    public async Task<IActionResult> RejectPaymentReport(
        [FromRoute] string tenantId,
        [FromRoute] string reportId,
        [FromBody] RejectPaymentReportRequest? request,
        [FromServices] ICollectionPaymentReportService reports,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        var result = await reports.RejectAsync(tenantId, reportId, request?.Notes, _tenantContext.Current!.UserId, ct);
        return result.Ok ? Ok(result) : UnprocessableEntity(result);
    }

    /// <summary>Portfolio totals and the breakdown by delinquency stage (active loans).</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var loans = await _store.GetLoansAsync(tenantId, ct);
        var active = loans.Where(l => l.Status is CollectionLoanStatus.Active or CollectionLoanStatus.Restructured).ToList();
        var lastImport = (await _store.GetImportBatchesAsync(tenantId, 1, ct)).FirstOrDefault();

        return Ok(new
        {
            customers = loans.Select(l => l.PartyId).Distinct().Count(),
            loans = loans.Count,
            activeLoans = active.Count,
            totalBalance = active.Sum(l => l.TotalBalance),
            overdueBalance = active.Sum(l => l.OverdueBalance),
            currencies = loans.Select(l => l.Currency).Distinct().ToList(),
            byStage = Enum.GetValues<DelinquencyStage>().Select(stage =>
            {
                var inStage = active.Where(l => l.Stage == stage).ToList();
                return new
                {
                    stage = stage.ToString(),
                    label = DelinquencyStages.Label(stage),
                    loans = inStage.Count,
                    customers = inStage.Select(l => l.PartyId).Distinct().Count(),
                    overdueBalance = inStage.Sum(l => l.OverdueBalance)
                };
            }),
            lastImport = lastImport is null ? null : new { lastImport.Id, lastImport.SnapshotDate, lastImport.CreatedAt, lastImport.Source, lastImport.FileName },
            promises = await PromiseTotalsAsync(tenantId, ct)
        });
    }

    /// <summary>Customers with their loans aggregated, worst delinquency first.</summary>
    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers(
        [FromRoute] string tenantId,
        [FromQuery] string? query = null,
        [FromQuery] string? stage = null,
        [FromQuery] int? minDaysPastDue = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var loans = await _store.GetLoansAsync(tenantId, ct);
        var customers = loans
            .GroupBy(l => l.PartyId)
            .Select(g =>
            {
                var worst = g.OrderByDescending(l => l.DaysPastDue).First();
                return new CollectionsCustomerRow
                {
                    PartyId = g.Key,
                    CustomerExternalId = g.Select(l => l.CustomerExternalId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    Name = g.Select(l => l.CustomerName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    Phone = g.Select(l => l.CustomerPhone).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    Loans = g.Count(),
                    ActiveLoans = g.Count(l => l.Status is CollectionLoanStatus.Active or CollectionLoanStatus.Restructured),
                    TotalBalance = g.Sum(l => l.TotalBalance),
                    OverdueBalance = g.Sum(l => l.OverdueBalance),
                    MaxDaysPastDue = worst.DaysPastDue,
                    Stage = worst.Stage.ToString(),
                    StageLabel = DelinquencyStages.Label(worst.Stage),
                    Currency = worst.Currency,
                    SnapshotDate = g.Max(l => l.SnapshotDate)
                };
            });

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            customers = customers.Where(c =>
                (c.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Phone?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.CustomerExternalId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        if (!string.IsNullOrWhiteSpace(stage))
            customers = customers.Where(c => string.Equals(c.Stage, stage, StringComparison.OrdinalIgnoreCase));
        if (minDaysPastDue is not null)
            customers = customers.Where(c => c.MaxDaysPastDue >= minDaysPastDue);

        var ordered = customers.OrderByDescending(c => c.MaxDaysPastDue).ThenByDescending(c => c.OverdueBalance).ToList();
        pageSize = Math.Clamp(pageSize, 1, 200);
        return Ok(new { total = ordered.Count, page, pageSize, items = ordered.Skip(Math.Max(0, page) * pageSize).Take(pageSize) });
    }

    [HttpGet("customers/{partyId}")]
    public async Task<IActionResult> GetCustomer([FromRoute] string tenantId, [FromRoute] string partyId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var loans = await _store.GetLoansByPartyAsync(tenantId, partyId, ct);
        if (loans.Count == 0) return NotFound(new { message = "Customer has no loans in the portfolio." });
        var party = await _parties.GetPartyByIdAsync(tenantId, partyId, ct);

        await _promises.ResolveAsync(tenantId, DateTimeOffset.UtcNow, partyId, ct);
        return Ok(new
        {
            party = party is null ? null : new { party.Id, party.DisplayName, party.FullName, party.Phone, party.Email, party.Kind },
            loans,
            promises = await _store.GetPromisesAsync(tenantId, null, partyId, 50, ct),
            contacts = await ContactHistoryAsync(tenantId, partyId, ct)
        });
    }

    /// <summary>Every loan of the portfolio, with filters, sorting and paging.</summary>
    [HttpGet("loans")]
    public async Task<IActionResult> GetLoans(
        [FromRoute] string tenantId,
        [FromQuery] string? query = null,
        [FromQuery] string? stage = null,
        [FromQuery] string? status = null,
        [FromQuery] string? product = null,
        [FromQuery] int? minDaysPastDue = null,
        [FromQuery] int? maxDaysPastDue = null,
        [FromQuery] string sort = "dpd",
        [FromQuery] bool desc = true,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var all = await _store.GetLoansAsync(tenantId, ct);
        IEnumerable<CollectionLoanDocument> loans = all;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            loans = loans.Where(l =>
                l.LoanExternalId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (l.CustomerName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.CustomerExternalId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.CustomerPhone?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        if (Enum.TryParse<DelinquencyStage>(stage, true, out var stageValue))
            loans = loans.Where(l => l.Stage == stageValue);
        if (Enum.TryParse<CollectionLoanStatus>(status, true, out var statusValue))
            loans = loans.Where(l => l.Status == statusValue);
        if (!string.IsNullOrWhiteSpace(product))
            loans = loans.Where(l => string.Equals(l.Product, product, StringComparison.OrdinalIgnoreCase));
        if (minDaysPastDue is not null)
            loans = loans.Where(l => l.DaysPastDue >= minDaysPastDue);
        if (maxDaysPastDue is not null)
            loans = loans.Where(l => l.DaysPastDue <= maxDaysPastDue);

        Func<CollectionLoanDocument, object?> key = sort.ToLowerInvariant() switch
        {
            "overdue" => l => l.OverdueBalance,
            "balance" => l => l.TotalBalance,
            "nextpayment" => l => l.NextPaymentDate,
            "customer" => l => l.CustomerName,
            "loan" => l => l.LoanExternalId,
            "disbursed" => l => l.DisbursedAt,
            _ => l => l.DaysPastDue
        };
        var filtered = (desc ? loans.OrderByDescending(key) : loans.OrderBy(key)).ToList();
        pageSize = Math.Clamp(pageSize, 1, 200);

        return Ok(new
        {
            total = filtered.Count,
            page,
            pageSize,
            totalBalance = filtered.Sum(l => l.TotalBalance),
            overdueBalance = filtered.Sum(l => l.OverdueBalance),
            products = all.Select(l => l.Product).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().OrderBy(p => p).ToList(),
            items = filtered.Skip(Math.Max(0, page) * pageSize).Take(pageSize).Select(l => new
            {
                l.Id,
                l.LoanExternalId,
                l.PartyId,
                l.CustomerExternalId,
                l.CustomerName,
                l.CustomerPhone,
                l.Product,
                l.Currency,
                l.OriginalAmount,
                l.DisbursedAt,
                l.TotalBalance,
                l.OverdueBalance,
                l.DaysPastDue,
                Stage = l.Stage.ToString(),
                StageLabel = DelinquencyStages.Label(l.Stage),
                Status = l.Status.ToString(),
                l.NextPaymentDate,
                l.NextPaymentAmount,
                l.LastPaymentDate,
                l.LastPaymentAmount,
                l.SnapshotDate
            })
        });
    }

    /// <summary>One loan with its history of cut-offs, the customer's other loans, promises and contacts.</summary>
    [HttpGet("loans/{loanId}")]
    public async Task<IActionResult> GetLoan([FromRoute] string tenantId, [FromRoute] string loanId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var loan = await _store.GetLoanAsync(tenantId, loanId, ct);
        if (loan is null) return NotFound(new { message = "Loan not found." });

        await _promises.ResolveAsync(tenantId, DateTimeOffset.UtcNow, loan.PartyId, ct);
        var party = await _parties.GetPartyByIdAsync(tenantId, loan.PartyId, ct);
        var siblings = (await _store.GetLoansByPartyAsync(tenantId, loan.PartyId, ct)).Where(l => l.Id != loan.Id).ToList();
        var promises = (await _store.GetPromisesAsync(tenantId, null, loan.PartyId, 50, ct))
            .Where(p => p.LoanIds.Count == 0 || p.LoanIds.Contains(loan.Id)).ToList();

        return Ok(new
        {
            loan,
            stageLabel = DelinquencyStages.Label(loan.Stage),
            snapshots = await _store.GetSnapshotsAsync(tenantId, loanId, 60, ct),
            customer = party is null ? null : new { party.Id, party.DisplayName, party.FullName, party.Phone, party.Email },
            otherLoans = siblings.Select(l => new { l.Id, l.LoanExternalId, l.Product, l.TotalBalance, l.OverdueBalance, l.DaysPastDue, Stage = l.Stage.ToString(), Status = l.Status.ToString(), l.Currency }),
            promises,
            payments = await _store.GetPaymentsByLoanAsync(tenantId, loan.Id, 50, ct),
            contacts = await ContactHistoryAsync(tenantId, loan.PartyId, ct)
        });
    }

    /// <summary>
    /// Payments received (from the lender's system or typed by an operator). Each one lowers the
    /// loan's overdue balance right away and closes promises that it covers. A repeated reference is ignored.
    /// </summary>
    [HttpPost("payments")]
    public async Task<IActionResult> RegisterPayments([FromRoute] string tenantId, [FromBody] RegisterCollectionPaymentsRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        if (request.Payments is not { Count: > 0 }) return BadRequest(new { message = "Envia al menos un pago." });
        if (request.Payments.Count > MaxBatchRows) return BadRequest(new { message = $"Maximo {MaxBatchRows} pagos por envio." });
        var source = string.IsNullOrWhiteSpace(request.Source) ? "api" : request.Source!;
        return Ok(await _payments.RegisterAsync(tenantId, request.Payments, source, _tenantContext.Current!.UserId, ct));
    }

    /// <summary>
    /// Creates a starting collections strategy: one draft campaign per delinquency stage, with
    /// schedule, contact window and follow-ups. Nothing is sent until each campaign is activated.
    /// </summary>
    [HttpPost("strategies/by-stage")]
    public async Task<IActionResult> CreateStageStrategy([FromRoute] string tenantId, [FromBody] CreateStageStrategyRequest? request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var created = CollectionsStrategyTemplates.Build(
            tenantId,
            string.IsNullOrWhiteSpace(request?.Timezone) ? "America/Mexico_City" : request!.Timezone!,
            string.IsNullOrWhiteSpace(request?.Channel) ? "whatsapp" : request!.Channel!,
            _tenantContext.Current!.UserId,
            DateTimeOffset.UtcNow);
        foreach (var campaign in created)
            await _campaigns.UpsertCampaignAsync(campaign, ct);
        return Ok(created);
    }

    /// <summary>Promises to pay, resolved against the current portfolio before listing.</summary>
    [HttpGet("promises")]
    public async Task<IActionResult> GetPromises([FromRoute] string tenantId, [FromQuery] string? status = null, CancellationToken ct = default)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        await _promises.ResolveAsync(tenantId, DateTimeOffset.UtcNow, null, ct);
        CollectionPromiseStatus? filter = Enum.TryParse<CollectionPromiseStatus>(status, true, out var parsed) ? parsed : null;
        return Ok(new
        {
            totals = await PromiseTotalsAsync(tenantId, ct),
            items = await _store.GetPromisesAsync(tenantId, filter, null, 500, ct)
        });
    }

    /// <summary>An operator records a promise to pay (e.g. after a phone call).</summary>
    [HttpPost("promises")]
    public async Task<IActionResult> CreatePromise([FromRoute] string tenantId, [FromBody] CreateCollectionPromiseRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        if (request.PromisedDate is null) return BadRequest(new { message = "Indica la fecha prometida." });
        if (request.Amount is < 0) return BadRequest(new { message = "El monto no puede ser negativo." });

        var promise = await _promises.CreateAsync(
            tenantId, request.PartyId, request.Amount, request.PromisedDate.Value,
            CampaignOutcomes.Sources.Human, _tenantContext.Current!.UserId, request.Notes,
            loanIds: request.LoanIds, ct: ct);
        return promise is null ? NotFound(new { message = "El cliente no tiene prestamos en la cartera." }) : Ok(promise);
    }

    [HttpPost("promises/{promiseId}/cancel")]
    public async Task<IActionResult> CancelPromise([FromRoute] string tenantId, [FromRoute] string promiseId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        var promise = await _store.GetPromiseAsync(tenantId, promiseId, ct);
        if (promise is null) return NotFound(new { message = "Promise not found." });
        if (promise.Status != CollectionPromiseStatus.Pending) return Conflict(new { message = "Solo se puede cancelar una promesa pendiente." });
        promise.Status = CollectionPromiseStatus.Cancelled;
        promise.ResolvedAt = DateTimeOffset.UtcNow;
        await _store.UpdatePromiseAsync(promise, ct);
        return Ok(promise);
    }

    private async Task<object> PromiseTotalsAsync(string tenantId, CancellationToken ct)
    {
        var all = await _store.GetPromisesAsync(tenantId, null, null, 5000, ct);
        var resolved = all.Count(p => p.Status is CollectionPromiseStatus.Kept or CollectionPromiseStatus.Broken);
        var kept = all.Count(p => p.Status == CollectionPromiseStatus.Kept);
        return new
        {
            pending = all.Count(p => p.Status == CollectionPromiseStatus.Pending),
            pendingAmount = all.Where(p => p.Status == CollectionPromiseStatus.Pending).Sum(p => p.Amount ?? 0m),
            kept,
            broken = all.Count(p => p.Status == CollectionPromiseStatus.Broken),
            keptRate = resolved == 0 ? 0m : Math.Round((decimal)kept / resolved, 4)
        };
    }

    private async Task<IReadOnlyList<object>> ContactHistoryAsync(string tenantId, string partyId, CancellationToken ct)
    {
        var contacts = await _campaigns.GetContactExecutionsByPartyAsync(tenantId, partyId, 30, ct);
        var names = new Dictionary<string, string>();
        var now = DateTimeOffset.UtcNow;
        var result = new List<object>();
        foreach (var c in contacts)
        {
            if (!names.TryGetValue(c.CampaignId, out var name))
                names[c.CampaignId] = name = (await _campaigns.GetCampaignAsync(tenantId, c.CampaignId, ct))?.Name ?? "Campana";
            var outcome = CampaignOutcomeService.EffectiveOutcome(c, now);
            result.Add(new
            {
                c.Id,
                c.CampaignId,
                CampaignName = name,
                c.Channel,
                c.Recipient,
                Status = c.Status.ToString(),
                c.RenderedMessage,
                c.LastReply,
                c.RepliedAt,
                Outcome = outcome,
                OutcomeLabel = CampaignOutcomes.Find(outcome)?.Label,
                c.OutcomeSource,
                c.OutcomeNotes,
                c.PromiseAmount,
                c.PromiseDate,
                c.SkipReason,
                c.CreatedAt
            });
        }
        return result;
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}

public sealed class CollectionsCsvImportForm
{
    public IFormFile? File { get; set; }
    /// <summary>Cut-off date of the data; defaults to today.</summary>
    public DateTimeOffset? SnapshotDate { get; set; }
    /// <summary>Validate and preview without saving.</summary>
    public bool DryRun { get; set; }
}

public sealed record CollectionsBatchImportRequest
{
    public DateTimeOffset? SnapshotDate { get; init; }
    public bool DryRun { get; init; }
    public List<CollectionLoanRow> Rows { get; init; } = [];
}

public sealed record CollectionsCustomerRow
{
    public string PartyId { get; init; } = string.Empty;
    public string? CustomerExternalId { get; init; }
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public int Loans { get; init; }
    public int ActiveLoans { get; init; }
    public decimal TotalBalance { get; init; }
    public decimal OverdueBalance { get; init; }
    public int MaxDaysPastDue { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string StageLabel { get; init; } = string.Empty;
    public string Currency { get; init; } = "MXN";
    public DateTimeOffset SnapshotDate { get; init; }
}

public sealed record CreateCollectionPromiseRequest
{
    public string PartyId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public DateTimeOffset? PromisedDate { get; init; }
    public string? Notes { get; init; }
    public List<string>? LoanIds { get; init; }
}

public sealed record RegisterCollectionPaymentsRequest
{
    public List<CollectionPaymentInput>? Payments { get; init; }
    /// <summary>api (default), human or csv.</summary>
    public string? Source { get; init; }
}

public sealed record CreateStageStrategyRequest
{
    public string? Timezone { get; init; }
    public string? Channel { get; init; }
}

public sealed record SaveCollectionPaymentSettingsRequest
{
    public List<CollectionPaymentMethod>? Methods { get; init; }
    public string? ReferenceKind { get; init; }
    public string? Notes { get; init; }
}

public sealed record ConfirmPaymentReportRequest
{
    public string? LoanId { get; init; }
    public decimal? Amount { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
}

public sealed record RejectPaymentReportRequest
{
    public string? Notes { get; init; }
}
