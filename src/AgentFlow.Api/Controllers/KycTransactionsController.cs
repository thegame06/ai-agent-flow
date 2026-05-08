using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}")]
public sealed class KycTransactionsController : ControllerBase
{
    private readonly IMongoCollection<KycCaseDto> _kycCases;
    private readonly IMongoCollection<PaymentIntentDto> _payments;

    public KycTransactionsController(IMongoDatabase database)
    {
        _kycCases = database.GetCollection<KycCaseDto>("kyc_cases");
        _payments = database.GetCollection<PaymentIntentDto>("payment_intents");

        _kycCases.Indexes.CreateMany([
            new CreateIndexModel<KycCaseDto>(Builders<KycCaseDto>.IndexKeys.Ascending(x => x.TenantId).Descending(x => x.UpdatedAt)),
            new CreateIndexModel<KycCaseDto>(Builders<KycCaseDto>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.DecisionStatus))
        ]);
        _payments.Indexes.CreateMany([
            new CreateIndexModel<PaymentIntentDto>(Builders<PaymentIntentDto>.IndexKeys.Ascending(x => x.TenantId).Descending(x => x.UpdatedAt)),
            new CreateIndexModel<PaymentIntentDto>(Builders<PaymentIntentDto>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Status))
        ]);
    }

    [HttpPost("kyc/document-check")]
    public IActionResult DocumentCheck([FromRoute] string tenantId, [FromBody] KycDocumentCheckRequest request)
    {
        var caseId = Guid.NewGuid().ToString("N");
        var score = CalculateSimpleScore(request.DocumentNumber, request.FullName);
        var status = score >= 70 ? "approved" : "needs_review";

        var dto = new KycCaseDto
        {
            CaseId = caseId,
            TenantId = tenantId,
            CustomerId = request.CustomerId,
            FullName = request.FullName,
            DocumentType = request.DocumentType,
            DocumentNumber = request.DocumentNumber,
            DecisionStatus = status,
            RiskScore = score,
            ReviewRequired = status != "approved",
            Evidence = new List<string>(request.EvidenceUrls ?? []),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "system"
        };

        _kycCases.InsertOne(dto);
        return Ok(dto);
    }

    [HttpPost("kyc/review/{caseId}")]
    public IActionResult ReviewCase([FromRoute] string tenantId, [FromRoute] string caseId, [FromBody] KycReviewRequest request)
    {
        var existing = _kycCases.Find(x => x.CaseId == caseId && x.TenantId == tenantId).FirstOrDefault();
        if (existing is null)
            return NotFound(new { message = "KYC case not found." });

        existing.DecisionStatus = request.Approved ? "approved" : "rejected";
        existing.ReviewRequired = false;
        existing.ReviewNotes = request.Notes;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedBy = request.ReviewerId ?? "reviewer";
        _kycCases.ReplaceOne(x => x.CaseId == caseId && x.TenantId == tenantId, existing);
        return Ok(existing);
    }

    [HttpGet("kyc/cases/{caseId}")]
    public IActionResult GetCase([FromRoute] string tenantId, [FromRoute] string caseId)
    {
        var existing = _kycCases.Find(x => x.CaseId == caseId && x.TenantId == tenantId).FirstOrDefault();
        if (existing is null)
            return NotFound(new { message = "KYC case not found." });

        return Ok(existing);
    }

    [HttpGet("kyc/cases")]
    public IActionResult ListCases(
        [FromRoute] string tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var filter = Builders<KycCaseDto>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(status))
            filter &= Builders<KycCaseDto>.Filter.Eq(x => x.DecisionStatus, status);
        if (from.HasValue)
            filter &= Builders<KycCaseDto>.Filter.Gte(x => x.UpdatedAt, from.Value);
        if (to.HasValue)
            filter &= Builders<KycCaseDto>.Filter.Lte(x => x.UpdatedAt, to.Value);

        var total = _kycCases.CountDocuments(filter);
        var items = _kycCases.Find(filter)
            .SortByDescending(x => x.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToList();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost("transactions/payments")]
    public IActionResult CreatePayment([FromRoute] string tenantId, [FromBody] CreatePaymentIntentRequest request)
    {
        var paymentId = Guid.NewGuid().ToString("N");
        var payment = new PaymentIntentDto
        {
            PaymentId = paymentId,
            TenantId = tenantId,
            CustomerId = request.CustomerId,
            Currency = request.Currency,
            Amount = request.Amount,
            Reference = request.Reference ?? $"pay-{paymentId[..8]}",
            Status = "created",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _payments.InsertOne(payment);
        return Ok(payment);
    }

    [HttpPost("transactions/payments/{paymentId}/confirm")]
    public IActionResult ConfirmPayment([FromRoute] string tenantId, [FromRoute] string paymentId)
    {
        var existing = _payments.Find(x => x.PaymentId == paymentId && x.TenantId == tenantId).FirstOrDefault();
        if (existing is null)
            return NotFound(new { message = "Payment not found." });

        existing.Status = "confirmed";
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        _payments.ReplaceOne(x => x.PaymentId == paymentId && x.TenantId == tenantId, existing);
        return Ok(existing);
    }

    [HttpGet("transactions/payments")]
    public IActionResult ListPayments(
        [FromRoute] string tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var filter = Builders<PaymentIntentDto>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(status))
            filter &= Builders<PaymentIntentDto>.Filter.Eq(x => x.Status, status);
        if (from.HasValue)
            filter &= Builders<PaymentIntentDto>.Filter.Gte(x => x.UpdatedAt, from.Value);
        if (to.HasValue)
            filter &= Builders<PaymentIntentDto>.Filter.Lte(x => x.UpdatedAt, to.Value);

        var total = _payments.CountDocuments(filter);
        var items = _payments.Find(filter)
            .SortByDescending(x => x.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToList();

        return Ok(new { total, page, pageSize, items });
    }

    private static int CalculateSimpleScore(string? documentNumber, string? fullName)
    {
        var score = 50;
        if (!string.IsNullOrWhiteSpace(documentNumber) && documentNumber.Length >= 8) score += 20;
        if (!string.IsNullOrWhiteSpace(fullName) && fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2) score += 20;
        return Math.Clamp(score, 0, 100);
    }
}

public sealed record KycDocumentCheckRequest
{
    public string? CustomerId { get; init; }
    public string? FullName { get; init; }
    public string? DocumentType { get; init; }
    public string? DocumentNumber { get; init; }
    public List<string>? EvidenceUrls { get; init; }
}

public sealed record KycReviewRequest
{
    public bool Approved { get; init; }
    public string? Notes { get; init; }
    public string? ReviewerId { get; init; }
}

public sealed record CreatePaymentIntentRequest
{
    public string? CustomerId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public string? Reference { get; init; }
}

public sealed record KycCaseDto
{
    public required string CaseId { get; init; }
    public required string TenantId { get; init; }
    public string? CustomerId { get; init; }
    public string? FullName { get; init; }
    public string? DocumentType { get; init; }
    public string? DocumentNumber { get; init; }
    public int RiskScore { get; init; }
    public bool ReviewRequired { get; set; }
    public string DecisionStatus { get; set; } = "needs_review";
    public string? ReviewNotes { get; set; }
    public List<string> Evidence { get; init; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed record PaymentIntentDto
{
    public required string PaymentId { get; init; }
    public required string TenantId { get; init; }
    public string? CustomerId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public string Status { get; set; } = "created";
    public string? Reference { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
