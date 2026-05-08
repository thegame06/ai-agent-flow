using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}")]
public sealed class KycTransactionsController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, KycCaseDto> KycCases = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, PaymentIntentDto> Payments = new(StringComparer.OrdinalIgnoreCase);

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

        KycCases[caseId] = dto;
        return Ok(dto);
    }

    [HttpPost("kyc/review/{caseId}")]
    public IActionResult ReviewCase([FromRoute] string tenantId, [FromRoute] string caseId, [FromBody] KycReviewRequest request)
    {
        if (!KycCases.TryGetValue(caseId, out var existing) || !string.Equals(existing.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { message = "KYC case not found." });

        existing.DecisionStatus = request.Approved ? "approved" : "rejected";
        existing.ReviewRequired = false;
        existing.ReviewNotes = request.Notes;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedBy = request.ReviewerId ?? "reviewer";
        KycCases[caseId] = existing;
        return Ok(existing);
    }

    [HttpGet("kyc/cases/{caseId}")]
    public IActionResult GetCase([FromRoute] string tenantId, [FromRoute] string caseId)
    {
        if (!KycCases.TryGetValue(caseId, out var existing) || !string.Equals(existing.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { message = "KYC case not found." });

        return Ok(existing);
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

        Payments[paymentId] = payment;
        return Ok(payment);
    }

    [HttpPost("transactions/payments/{paymentId}/confirm")]
    public IActionResult ConfirmPayment([FromRoute] string tenantId, [FromRoute] string paymentId)
    {
        if (!Payments.TryGetValue(paymentId, out var existing) || !string.Equals(existing.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { message = "Payment not found." });

        existing.Status = "confirmed";
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        Payments[paymentId] = existing;
        return Ok(existing);
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
