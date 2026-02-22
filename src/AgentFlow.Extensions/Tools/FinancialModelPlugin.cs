using AgentFlow.ToolSDK;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentFlow.Extensions.Tools;

/// <summary>
/// Mock Financial Risk Calculator Plugin for Loan Officer Demo.
/// Evaluates loan risk based on credit score, loan amount, and applicant profile.
/// 
/// In production, this would use ML models (e.g., XGBoost, neural networks) trained on historical loan data.
/// </summary>
public sealed class FinancialModelPlugin : IToolPlugin
{
    private readonly ILogger<FinancialModelPlugin> _logger;

    public FinancialModelPlugin(ILogger<FinancialModelPlugin> logger)
    {
        _logger = logger;
    }

    public ToolMetadata Metadata => new()
    {
        Id = "financial-risk-model",
        Name = "Financial Risk Calculator",
        Version = "1.0.0",
        Author = "AgentFlow Demo Team",
        Description = "Calculate loan risk score using proprietary financial model (mock implementation)",
        Tags = new[] { "finance", "risk", "ml-model", "banking", "demo" },
        RiskLevel = ToolRiskLevel.Medium, // Important financial decision
        License = "MIT"
    };

    public ToolSchema GetSchema()
    {
        return new ToolSchema
        {
            Parameters = new Dictionary<string, ParameterSchema>
            {
                ["creditScore"] = new ParameterSchema
                {
                    Type = "number",
                    Description = "Credit score from bureau (300-850)",
                    Minimum = 300,
                    Maximum = 850
                },
                ["loanAmount"] = new ParameterSchema
                {
                    Type = "number",
                    Description = "Requested loan amount in USD",
                    Minimum = 1000,
                    Maximum = 10_000_000
                },
                ["annualIncome"] = new ParameterSchema
                {
                    Type = "number",
                    Description = "Applicant's annual income in USD (optional)",
                    Minimum = 0
                },
                ["employmentYears"] = new ParameterSchema
                {
                    Type = "number",
                    Description = "Years in current employment (optional)",
                    Minimum = 0,
                    DefaultValue = 0
                },
                ["debtToIncomeRatio"] = new ParameterSchema
                {
                    Type = "number",
                    Description = "Existing debt-to-income ratio (0.0-1.0, optional)",
                    Minimum = 0.0,
                    Maximum = 1.0,
                    DefaultValue = 0.3
                }
            },
            Required = new[] { "creditScore", "loanAmount" },
            Example = new
            {
                creditScore = 720,
                loanAmount = 50000,
                annualIncome = 75000,
                employmentYears = 5,
                debtToIncomeRatio = 0.35
            }
        };
    }

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Risk calculator invoked for tenant {TenantId}, execution {ExecutionId}",
            context.TenantId, context.ExecutionId);

        try
        {
            // Extract parameters
            if (!context.Parameters.TryGetValue("creditScore", out var scoreObj) || !TryConvertToDouble(scoreObj, out var creditScore))
            {
                return ToolResult.FromError(
                    "Parameter 'creditScore' is required and must be a number",
                    "RISK_MISSING_SCORE");
            }

            if (!context.Parameters.TryGetValue("loanAmount", out var amountObj) || !TryConvertToDouble(amountObj, out var loanAmount))
            {
                return ToolResult.FromError(
                    "Parameter 'loanAmount' is required and must be a number",
                    "RISK_MISSING_AMOUNT");
            }

            var annualIncome = context.Parameters.TryGetValue("annualIncome", out var incomeObj) && TryConvertToDouble(incomeObj, out var inc)
                ? inc
                : 50000; // Default assumption

            var employmentYears = context.Parameters.TryGetValue("employmentYears", out var empYearsObj) && TryConvertToDouble(empYearsObj, out var empY)
                ? empY
                : 2; // Default assumption

            var debtToIncomeRatio = context.Parameters.TryGetValue("debtToIncomeRatio", out var dtiObj) && TryConvertToDouble(dtiObj, out var dti)
                ? dti
                : 0.3; // Default assumption

            // Simulate ML model processing
            await Task.Delay(Random.Shared.Next(150, 400), ct);

            // Calculate risk score (0-100, lower is better)
            double riskScore = CalculateRiskScore(creditScore, loanAmount, annualIncome, employmentYears, debtToIncomeRatio);

            string riskLevel = riskScore switch
            {
                < 20 => "Very Low",
                < 40 => "Low",
                < 60 => "Medium",
                < 80 => "High",
                _ => "Very High"
            };

            string recommendation = riskScore switch
            {
                < 40 => "Approve",
                < 60 => "Manual Review Required",
                _ => "Reject"
            };

            double recommendedInterestRate = CalculateInterestRate(riskScore, creditScore);
            int recommendedTermMonths = loanAmount > 100000 ? 360 : loanAmount > 50000 ? 240 : 120;

            var result = new
            {
                success = true,
                riskScore = Math.Round(riskScore, 2),
                riskLevel,
                recommendation,
                confidence = 0.87, // Mock confidence score
                factors = new
                {
                    creditScore,
                    loanAmount,
                    loanToIncomeRatio = Math.Round(loanAmount / annualIncome, 2),
                    debtToIncomeRatio = Math.Round(debtToIncomeRatio, 2),
                    employmentStability = employmentYears >= 3 ? "Stable" : "Moderate"
                },
                terms = new
                {
                    recommendedInterestRate = Math.Round(recommendedInterestRate, 2),
                    recommendedTermMonths,
                    monthlyPayment = Math.Round(CalculateMonthlyPayment(loanAmount, recommendedInterestRate, recommendedTermMonths), 2)
                },
                modelVersion = "v2.3.1-mock",
                calculatedAt = DateTimeOffset.UtcNow.ToString("O")
            };

            _logger.LogInformation(
                "Risk calculation completed. Score: {RiskScore}, Level: {RiskLevel}, Recommendation: {Recommendation}",
                riskScore, riskLevel, recommendation);

            return ToolResult.FromSuccess(JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Risk calculator execution failed");
            return ToolResult.FromError(
                $"Risk calculation error: {ex.Message}",
                "RISK_CALC_ERROR");
        }
    }

    public PluginCapabilities Capabilities => new()
    {
        SupportsAsync = true,
        SupportsStreaming = false,
        IsCacheable = false, // Don't cache risk calculations
        RequiresNetwork = false,
        IsReadOnly = true // Calculation only, no writes
    };

    public IReadOnlyList<PolicyRequirement> RequiredPolicies => new[]
    {
        new PolicyRequirement { PolicyGroupId = "financial-risk-assessment", Reason = "Tool performs financial risk assessments" },
        new PolicyRequirement { PolicyGroupId = "loan-decision-authority", Reason = "Requires authority to make loan decisions" }
    };

    /// <summary>
    /// Mock risk calculation algorithm.
    /// In production, this would be a trained ML model.
    /// </summary>
    private static double CalculateRiskScore(
        double creditScore,
        double loanAmount,
        double annualIncome,
        double employmentYears,
        double debtToIncomeRatio)
    {
        // Weighted risk factors (0-100 scale, higher = more risk)
        double creditRisk = (850 - creditScore) / 5.5; // Max ~100 if score=300
        double loanToIncomeRisk = Math.Min(100, (loanAmount / annualIncome) * 30);
        double dtiRisk = debtToIncomeRatio * 100;
        double employmentRisk = employmentYears < 1 ? 50 : employmentYears < 3 ? 30 : 10;

        // Weighted average
        double totalRisk = (creditRisk * 0.4) + (loanToIncomeRisk * 0.3) + (dtiRisk * 0.2) + (employmentRisk * 0.1);

        return Math.Max(0, Math.Min(100, totalRisk));
    }

    private static double CalculateInterestRate(double riskScore, double creditScore)
    {
        // Base rate: 3.5% for perfect credit
        double baseRate = 3.5;
        
        // Add risk premium
        double riskPremium = (riskScore / 100) * 8; // Up to +8% for highest risk
        
        // Credit score adjustment
        double creditAdjustment = creditScore >= 750 ? -0.5 : creditScore < 650 ? +1.0 : 0;

        return Math.Max(3.0, baseRate + riskPremium + creditAdjustment);
    }

    private static double CalculateMonthlyPayment(double principal, double annualRate, int termMonths)
    {
        double monthlyRate = annualRate / 100 / 12;
        if (monthlyRate == 0) return principal / termMonths;

        return principal * (monthlyRate * Math.Pow(1 + monthlyRate, termMonths)) /
               (Math.Pow(1 + monthlyRate, termMonths) - 1);
    }

    private static bool TryConvertToDouble(object value, out double result)
    {
        result = 0;
        if (value is double d)
        {
            result = d;
            return true;
        }
        if (value is int i)
        {
            result = i;
            return true;
        }
        if (value is long l)
        {
            result = l;
            return true;
        }
        if (value is decimal dec)
        {
            result = (double)dec;
            return true;
        }
        if (value is string str && double.TryParse(str, out result))
        {
            return true;
        }
        return false;
    }
}
