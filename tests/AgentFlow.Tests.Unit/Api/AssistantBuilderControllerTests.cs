using AgentFlow.Abstractions;
using AgentFlow.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgentFlow.Tests.Unit.Api;

public sealed class AssistantBuilderControllerTests
{
    [Fact]
    public void ValidateAssistantConfig_WithValidRequest_ReturnsOk()
    {
        var controller = new AssistantBuilderController();
        var request = new AssistantBuildRequest
        {
            Name = "Seguimiento Leads",
            FirstMessage = "Hola, te llamo para dar seguimiento.",
            Reasoning = new AssistantReasoningModelConfig
            {
                Provider = "anthropic",
                Model = "claude-haiku",
                MaxTokens = 250
            },
            Voice = new AssistantVoiceConfig
            {
                Provider = "11labs",
                VoiceId = "voice-1",
                Model = "eleven_turbo_v2_5",
                Language = "es"
            },
            Transcriber = new AssistantTranscriberConfig
            {
                Provider = "deepgram",
                Model = "nova-3",
                Language = "es"
            }
        };

        var result = controller.ValidateAssistantConfig(request);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void ValidateAssistantConfig_WithInvalidLanguage_ReturnsBadRequest()
    {
        var controller = new AssistantBuilderController();
        var request = new AssistantBuildRequest
        {
            Name = "Seguimiento Leads",
            FirstMessage = "Hola",
            Reasoning = new AssistantReasoningModelConfig
            {
                Provider = "anthropic",
                Model = "claude-haiku",
                MaxTokens = 250
            },
            Voice = new AssistantVoiceConfig
            {
                Provider = "11labs",
                VoiceId = "voice-1",
                Model = "eleven_turbo_v2_5",
                Language = "de"
            },
            Transcriber = new AssistantTranscriberConfig
            {
                Provider = "deepgram",
                Model = "nova-3",
                Language = "de"
            }
        };

        var result = controller.ValidateAssistantConfig(request);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void ValidateAssistantConfig_WithProviderModelMismatch_ReturnsBadRequest()
    {
        var controller = new AssistantBuilderController();
        var request = new AssistantBuildRequest
        {
            Name = "Seguimiento Leads",
            FirstMessage = "Hola",
            Channel = "voice",
            Reasoning = new AssistantReasoningModelConfig
            {
                Provider = "anthropic",
                Model = "claude-haiku",
                MaxTokens = 250
            },
            Voice = new AssistantVoiceConfig
            {
                Provider = "11labs",
                VoiceId = "voice-1",
                Model = "azure-neural-tts",
                Language = "es"
            },
            Transcriber = new AssistantTranscriberConfig
            {
                Provider = "deepgram",
                Model = "whisper-1",
                Language = "es"
            }
        };

        var result = controller.ValidateAssistantConfig(request);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void ValidateAssistantConfig_VideoVoice_InvalidTranscriberProvider_ReturnsBadRequest()
    {
        var controller = new AssistantBuilderController();
        var request = new AssistantBuildRequest
        {
            Name = "Seguimiento Leads",
            FirstMessage = "Hola",
            Channel = "video_voice",
            Reasoning = new AssistantReasoningModelConfig
            {
                Provider = "anthropic",
                Model = "claude-haiku",
                MaxTokens = 250
            },
            Voice = new AssistantVoiceConfig
            {
                Provider = "11labs",
                VoiceId = "voice-1",
                Model = "eleven_turbo_v2_5",
                Language = "es"
            },
            Transcriber = new AssistantTranscriberConfig
            {
                Provider = "assemblyai",
                Model = "universal",
                Language = "es"
            }
        };

        var result = controller.ValidateAssistantConfig(request);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task WizardSession_AdvancesOneQuestionAtATime_AndCompletes()
    {
        var controller = new AssistantBuilderController();
        var created = await controller.CreateWizardSession(null) as OkObjectResult;
        Assert.NotNull(created);
        using var createdJson = JsonDocument.Parse(JsonSerializer.Serialize(created!.Value));
        var sessionId = createdJson.RootElement.GetProperty("sessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var step1 = await controller.AnswerWizardQuestion(sessionId!, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Spanish" }) as OkObjectResult;
        using var step1Json = JsonDocument.Parse(JsonSerializer.Serialize(step1!.Value));
        Assert.Equal("task", step1Json.RootElement.GetProperty("stage").GetString());

        var step2 = await controller.AnswerWizardQuestion(sessionId!, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Seguimiento de leads" }) as OkObjectResult;
        using var step2Json = JsonDocument.Parse(JsonSerializer.Serialize(step2!.Value));
        Assert.Equal("audience", step2Json.RootElement.GetProperty("stage").GetString());

        var step3 = await controller.AnswerWizardQuestion(sessionId!, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Prospectos en negociación" }) as OkObjectResult;
        using var step3Json = JsonDocument.Parse(JsonSerializer.Serialize(step3!.Value));
        Assert.Equal("tone", step3Json.RootElement.GetProperty("stage").GetString());

        var step4 = await controller.AnswerWizardQuestion(sessionId!, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Amigable" }) as OkObjectResult;
        using var step4Json = JsonDocument.Parse(JsonSerializer.Serialize(step4!.Value));
        Assert.Equal("completed", step4Json.RootElement.GetProperty("stage").GetString());
        Assert.True(step4Json.RootElement.GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task WizardSession_InvalidAnswer_ReturnsBadRequest()
    {
        var controller = new AssistantBuilderController();
        var created = await controller.CreateWizardSession(null) as OkObjectResult;
        using var createdJson = JsonDocument.Parse(JsonSerializer.Serialize(created!.Value));
        var sessionId = createdJson.RootElement.GetProperty("sessionId").GetString()!;

        var invalid = await controller.AnswerWizardQuestion(
            sessionId,
            new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Deutsch" }) as BadRequestObjectResult;

        Assert.NotNull(invalid);
        using var invalidJson = JsonDocument.Parse(JsonSerializer.Serialize(invalid!.Value));
        Assert.Equal("wizard_invalid_answer", invalidJson.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WizardSession_GetStatus_ReturnsNextQuestion()
    {
        var controller = new AssistantBuilderController();
        var created = await controller.CreateWizardSession(null) as OkObjectResult;
        using var createdJson = JsonDocument.Parse(JsonSerializer.Serialize(created!.Value));
        var sessionId = createdJson.RootElement.GetProperty("sessionId").GetString()!;

        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Spanish" });
        var status = await controller.GetWizardSession(sessionId) as OkObjectResult;

        Assert.NotNull(status);
        using var statusJson = JsonDocument.Parse(JsonSerializer.Serialize(status!.Value));
        Assert.Equal("task", statusJson.RootElement.GetProperty("stage").GetString());
        Assert.True(statusJson.RootElement.TryGetProperty("question", out var questionEl));
        Assert.False(string.IsNullOrWhiteSpace(questionEl.GetProperty("question").GetString()));
    }

    [Fact]
    public async Task WizardSession_Materialize_ReturnsAssistantRequest()
    {
        var controller = new AssistantBuilderController();
        var created = await controller.CreateWizardSession(new AssistantBuilderController.WizardSessionCreateRequest { TenantId = "tenant-1" }) as OkObjectResult;
        using var createdJson = JsonDocument.Parse(JsonSerializer.Serialize(created!.Value));
        var sessionId = createdJson.RootElement.GetProperty("sessionId").GetString()!;

        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Spanish" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Seguimiento de leads" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Prospectos en negociación" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Amigable" });

        var materialized = await controller.MaterializeWizardSession(sessionId) as OkObjectResult;
        Assert.NotNull(materialized);
        using var materializedJson = JsonDocument.Parse(JsonSerializer.Serialize(materialized!.Value));
        Assert.Equal("completed", materializedJson.RootElement.GetProperty("stage").GetString());
        Assert.True(materializedJson.RootElement.TryGetProperty("assistant", out _));
    }

    [Fact]
    public async Task WizardMetrics_ReturnsConversionFunnel()
    {
        var controller = new AssistantBuilderController();
        var created = await controller.CreateWizardSession(new AssistantBuilderController.WizardSessionCreateRequest { TenantId = "tenant-metrics" }) as OkObjectResult;
        using var createdJson = JsonDocument.Parse(JsonSerializer.Serialize(created!.Value));
        var sessionId = createdJson.RootElement.GetProperty("sessionId").GetString()!;

        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Spanish" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Seguimiento de leads" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Prospectos en negociación" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Amigable" });
        await controller.MaterializeWizardSession(sessionId);

        var metrics = await controller.GetWizardMetrics("tenant-metrics") as OkObjectResult;
        Assert.NotNull(metrics);
        using var metricsJson = JsonDocument.Parse(JsonSerializer.Serialize(metrics!.Value));
        Assert.Equal("tenant-metrics", metricsJson.RootElement.GetProperty("tenantId").GetString());
        Assert.True(metricsJson.RootElement.GetProperty("funnel").GetProperty("sessionsCreated").GetInt32() >= 1);
        Assert.True(metricsJson.RootElement.GetProperty("funnel").GetProperty("sessionsMaterialized").GetInt32() >= 1);
    }

    [Fact]
    public async Task WizardSession_MaterializeThenValidateAssistantConfig_ReturnsOk()
    {
        var controller = new AssistantBuilderController();
        var created = await controller.CreateWizardSession(new AssistantBuilderController.WizardSessionCreateRequest { TenantId = "tenant-e2e" }) as OkObjectResult;
        using var createdJson = JsonDocument.Parse(JsonSerializer.Serialize(created!.Value));
        var sessionId = createdJson.RootElement.GetProperty("sessionId").GetString()!;

        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Spanish" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Seguimiento de leads" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Prospectos en negociación" });
        await controller.AnswerWizardQuestion(sessionId, new AssistantBuilderController.WizardSessionAnswerRequest { Answer = "Amigable" });

        var materialized = await controller.MaterializeWizardSession(sessionId) as OkObjectResult;
        Assert.NotNull(materialized);
        using var materializedJson = JsonDocument.Parse(JsonSerializer.Serialize(materialized!.Value));
        var assistantElement = materializedJson.RootElement.GetProperty("assistant");
        var assistant = JsonSerializer.Deserialize<AssistantBuildRequest>(assistantElement.GetRawText());
        Assert.NotNull(assistant);

        var validation = controller.ValidateAssistantConfig(assistant!);
        Assert.IsType<OkObjectResult>(validation);
    }
}
