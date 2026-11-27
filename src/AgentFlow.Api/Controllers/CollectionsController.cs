using System.Text;
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

    public CollectionsController(ICollectionsStore store, ICollectionsImportService imports, ICommerceStore parties, ITenantContextAccessor tenantContext)
    {
        _store = store;
        _imports = imports;
        _parties = parties;
        _tenantContext = tenantContext;
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
            lastImport = lastImport is null ? null : new { lastImport.Id, lastImport.SnapshotDate, lastImport.CreatedAt, lastImport.Source, lastImport.FileName }
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

        return Ok(new
        {
            party = party is null ? null : new { party.Id, party.DisplayName, party.FullName, party.Phone, party.Email, party.Kind },
            loans
        });
    }

    [HttpGet("loans/{loanId}")]
    public async Task<IActionResult> GetLoan([FromRoute] string tenantId, [FromRoute] string loanId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var loan = await _store.GetLoanAsync(tenantId, loanId, ct);
        if (loan is null) return NotFound(new { message = "Loan not found." });
        return Ok(new { loan, snapshots = await _store.GetSnapshotsAsync(tenantId, loanId, 60, ct) });
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
