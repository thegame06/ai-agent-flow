using AgentFlow.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Collections.Concurrent;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/assistant")]
[AllowAnonymous]
public sealed class AssistantBuilderController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, WizardSessionState> Sessions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<WizardEventDocument> SessionEvents = new();
    private readonly IMongoCollection<WizardSessionDocument>? _sessionsCollection;
    private readonly IMongoCollection<WizardEventDocument>? _eventsCollection;

    public AssistantBuilderController()
    {
    }

    public AssistantBuilderController(IMongoDatabase database)
    {
        _sessionsCollection = database.GetCollection<WizardSessionDocument>("assistant_wizard_sessions");
        _eventsCollection = database.GetCollection<WizardEventDocument>("assistant_wizard_events");
    }

    [HttpPost]
    public IActionResult ValidateAssistantConfig([FromBody] AssistantBuildRequest request)
    {
        var errors = new List<string>();

        ValidateReasoning("reasoning", request.Reasoning, errors);
        if (request.ReasoningFallback is not null)
            ValidateReasoning("reasoningFallback", request.ReasoningFallback, errors);

        var channel = NormalizeChannel(request.Channel);
        if (channel is null)
            errors.Add($"channel '{request.Channel}' is not supported. Use text|voice|video_voice.");

        ValidateVoice("voice", request.Voice, errors);
        ValidateTranscriber("transcriber", request.Transcriber, errors);
        ValidateCrossComponentCompatibility(request, channel, errors);

        if (errors.Count > 0)
            return BadRequest(new { valid = false, errors });

        return Ok(new
        {
            valid = true,
            normalized = new
            {
                request.Name,
                request.FirstMessage,
                channel,
                reasoning = request.Reasoning,
                voice = request.Voice,
                transcriber = request.Transcriber,
                reasoningFallback = request.ReasoningFallback
            }
        });
    }

    [HttpPost("wizard/sessions")]
    public async Task<IActionResult> CreateWizardSession([FromBody] WizardSessionCreateRequest? request, CancellationToken ct = default)
    {
        var session = new WizardSessionState
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = string.IsNullOrWhiteSpace(request?.TenantId) ? "platform" : request!.TenantId.Trim(),
            Stage = "language"
        };

        await SaveSessionAsync(session, ct);
        await RecordWizardEventAsync(session, "session.created", new { session.Stage }, ct);

        return Ok(new
        {
            sessionId = session.Id,
            stage = session.Stage,
            completed = false,
            question = BuildQuestion("language")
        });
    }

    [HttpPost("wizard/sessions/{sessionId}/answers")]
    public async Task<IActionResult> AnswerWizardQuestion([FromRoute] string sessionId, [FromBody] WizardSessionAnswerRequest request, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, ct);
        if (session is null)
            return NotFound(new { error = "wizard_session_not_found" });

        if (string.IsNullOrWhiteSpace(request.Answer))
            return BadRequest(new { error = "answer is required." });

        var stage = session.Stage;
        var answer = request.Answer.Trim();

        switch (stage)
        {
            case "language":
                UpsertArtifact(session, "Language", answer);
                session.Stage = "task";
                break;
            case "task":
                UpsertArtifact(session, "Task", answer);
                if (answer.Equals("Seguimiento de leads", StringComparison.OrdinalIgnoreCase))
                    UpsertArtifact(session, "Role", "Representante de ventas (follow-up)");
                session.Stage = "audience";
                break;
            case "audience":
                UpsertArtifact(session, "Callers", answer);
                session.Stage = "tone";
                break;
            case "tone":
                UpsertArtifact(session, "Tone", answer);
                session.Stage = "completed";
                break;
            default:
                return BadRequest(new { error = "wizard_session_already_completed" });
        }

        await SaveSessionAsync(session, ct);
        await RecordWizardEventAsync(session, "question.answered", new { stage, answer, nextStage = session.Stage }, ct);

        var completed = session.Stage == "completed";
        return Ok(new
        {
            sessionId = session.Id,
            stage = session.Stage,
            completed,
            artifact = session.Artifact,
            question = completed ? null : BuildQuestion(session.Stage)
        });
    }

    [HttpPost("wizard/sessions/{sessionId}/materialize")]
    public async Task<IActionResult> MaterializeWizardSession([FromRoute] string sessionId, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, ct);
        if (session is null)
            return NotFound(new { error = "wizard_session_not_found" });
        if (!string.Equals(session.Stage, "completed", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "wizard_session_not_completed", stage = session.Stage });

        var assistantName = BuildAssistantName(session);
        var firstMessage = BuildFirstMessage(session);
        var request = new AssistantBuildRequest
        {
            Name = assistantName,
            FirstMessage = firstMessage,
            Channel = "voice",
            Reasoning = new AssistantReasoningModelConfig
            {
                Provider = "anthropic",
                Model = "claude-haiku-4-5-20251001",
                MaxTokens = 250
            },
            Voice = new AssistantVoiceConfig
            {
                Provider = "11labs",
                VoiceId = "nmvA11Y688M5reLqDsVm",
                Model = "eleven_turbo_v2_5",
                Language = MapLanguageToCode(session.Artifact.GetValueOrDefault("Language") ?? "Spanish")
            },
            Transcriber = new AssistantTranscriberConfig
            {
                Provider = "deepgram",
                Model = "nova-3",
                Language = MapLanguageToCode(session.Artifact.GetValueOrDefault("Language") ?? "Spanish")
            }
        };

        var errors = new List<string>();
        ValidateReasoning("reasoning", request.Reasoning, errors);
        ValidateVoice("voice", request.Voice, errors);
        ValidateTranscriber("transcriber", request.Transcriber, errors);
        ValidateCrossComponentCompatibility(request, NormalizeChannel(request.Channel), errors);

        if (errors.Count > 0)
            return BadRequest(new { error = "wizard_materialization_invalid", errors });

        await RecordWizardEventAsync(session, "session.materialized", new { assistantName = request.Name }, ct);

        return Ok(new
        {
            sessionId = session.Id,
            tenantId = session.TenantId,
            stage = session.Stage,
            artifact = session.Artifact,
            assistant = request
        });
    }

    [HttpGet("wizard/metrics")]
    public async Task<IActionResult> GetWizardMetrics([FromQuery] string tenantId = "platform", [FromQuery] int limit = 1000, CancellationToken ct = default)
    {
        var events = await LoadWizardEventsAsync(tenantId, limit, ct);
        var created = events.Count(x => x.Action == "session.created");
        var answered = events.Count(x => x.Action == "question.answered");
        var materialized = events.Count(x => x.Action == "session.materialized");
        var answeredEvents = events.Where(x => x.Action == "question.answered").ToList();
        var sessionsReachedCompleted = answeredEvents
            .Where(x => ExtractPayloadField(x.Payload, "nextStage").Equals("completed", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.SessionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var stageCounts = answeredEvents
            .GroupBy(x => ExtractPayloadField(x.Payload, "stage"))
            .ToDictionary(g => string.IsNullOrWhiteSpace(g.Key) ? "unknown" : g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return Ok(new
        {
            tenantId,
            generatedAt = DateTimeOffset.UtcNow,
            windowSize = events.Count,
            funnel = new
            {
                sessionsCreated = created,
                questionsAnswered = answered,
                sessionsCompleted = sessionsReachedCompleted,
                sessionsMaterialized = materialized
            },
            conversion = new
            {
                completionRate = created == 0 ? 0 : Math.Round(sessionsReachedCompleted / (double)created, 4),
                materializationRate = created == 0 ? 0 : Math.Round(materialized / (double)created, 4)
            },
            dropoff = new
            {
                language = stageCounts.GetValueOrDefault("language"),
                task = stageCounts.GetValueOrDefault("task"),
                audience = stageCounts.GetValueOrDefault("audience"),
                tone = stageCounts.GetValueOrDefault("tone")
            }
        });
    }

    private static void ValidateReasoning(string key, AssistantReasoningModelConfig config, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Provider))
            errors.Add($"{key}.provider is required.");
        if (string.IsNullOrWhiteSpace(config.Model))
            errors.Add($"{key}.model is required.");
        if (config.MaxTokens < 50 || config.MaxTokens > 8192)
            errors.Add($"{key}.maxTokens must be between 50 and 8192.");
    }

    private static bool IsVoiceLanguageSupported(string language)
        => language.Equals("es", StringComparison.OrdinalIgnoreCase)
           || language.Equals("en", StringComparison.OrdinalIgnoreCase)
           || language.Equals("fr", StringComparison.OrdinalIgnoreCase);

    private static bool IsTranscriberLanguageSupported(string language)
        => IsVoiceLanguageSupported(language);

    private static string? NormalizeChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return "voice";
        var normalized = channel.Trim().ToLowerInvariant();
        return normalized switch
        {
            "text" => "text",
            "voice" => "voice",
            "video_voice" => "video_voice",
            "video-voice" => "video_voice",
            _ => null
        };
    }

    private static void ValidateVoice(string key, AssistantVoiceConfig config, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Provider))
            errors.Add($"{key}.provider is required.");
        if (string.IsNullOrWhiteSpace(config.Model))
            errors.Add($"{key}.model is required.");
        if (string.IsNullOrWhiteSpace(config.VoiceId))
            errors.Add($"{key}.voiceId is required.");
        if (string.IsNullOrWhiteSpace(config.Language))
            errors.Add($"{key}.language is required.");
        if (!string.IsNullOrWhiteSpace(config.Language) && !IsVoiceLanguageSupported(config.Language))
            errors.Add($"{key}.language '{config.Language}' is not supported.");
    }

    private static void ValidateTranscriber(string key, AssistantTranscriberConfig config, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Provider))
            errors.Add($"{key}.provider is required.");
        if (string.IsNullOrWhiteSpace(config.Model))
            errors.Add($"{key}.model is required.");
        if (string.IsNullOrWhiteSpace(config.Language))
            errors.Add($"{key}.language is required.");
        if (!string.IsNullOrWhiteSpace(config.Language) && !IsTranscriberLanguageSupported(config.Language))
            errors.Add($"{key}.language '{config.Language}' is not supported.");
    }

    private static void ValidateCrossComponentCompatibility(AssistantBuildRequest request, string? channel, List<string> errors)
    {
        if (channel is null)
            return;

        if (!request.Voice.Language.Equals(request.Transcriber.Language, StringComparison.OrdinalIgnoreCase))
            errors.Add("voice.language and transcriber.language must match for continuity.");

        if (channel is "voice" or "video_voice")
        {
            if (string.IsNullOrWhiteSpace(request.Voice.Provider) || string.IsNullOrWhiteSpace(request.Transcriber.Provider))
                errors.Add($"{channel} channel requires voice and transcriber providers.");
        }

        if (!IsVoiceModelCompatible(request.Voice.Provider, request.Voice.Model))
            errors.Add($"voice model '{request.Voice.Model}' is not compatible with provider '{request.Voice.Provider}'.");
        if (!IsTranscriberModelCompatible(request.Transcriber.Provider, request.Transcriber.Model))
            errors.Add($"transcriber model '{request.Transcriber.Model}' is not compatible with provider '{request.Transcriber.Provider}'.");

        if (channel == "video_voice")
        {
            if (!request.Transcriber.Provider.Equals("deepgram", StringComparison.OrdinalIgnoreCase)
                && !request.Transcriber.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("video_voice channel requires transcriber provider deepgram|openai.");
            }

            if (string.IsNullOrWhiteSpace(request.Voice.Codec) || !IsCodecSupported(request.Voice.Codec))
                errors.Add("video_voice channel requires voice codec pcm16|mulaw|opus.");
            if (string.IsNullOrWhiteSpace(request.Transcriber.Codec) || !IsCodecSupported(request.Transcriber.Codec))
                errors.Add("video_voice channel requires transcriber codec pcm16|mulaw|opus.");
        }

    }

    private static bool IsVoiceModelCompatible(string provider, string model)
    {
        var p = provider.Trim().ToLowerInvariant();
        var m = model.Trim().ToLowerInvariant();
        return p switch
        {
            "11labs" or "elevenlabs" => m.Contains("eleven", StringComparison.Ordinal),
            "azure" => m.Contains("neural", StringComparison.Ordinal) || m.Contains("tts", StringComparison.Ordinal),
            _ => true
        };
    }

    private static bool IsTranscriberModelCompatible(string provider, string model)
    {
        var p = provider.Trim().ToLowerInvariant();
        var m = model.Trim().ToLowerInvariant();
        return p switch
        {
            "deepgram" => m.Contains("nova", StringComparison.Ordinal),
            "openai" => m.Contains("gpt-4o-transcribe", StringComparison.Ordinal) || m.Contains("whisper", StringComparison.Ordinal),
            _ => true
        };
    }

    private static bool IsCodecSupported(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
            return false;

        var c = codec.Trim().ToLowerInvariant();
        return c is "pcm16" or "mulaw" or "opus";
    }

    private static object BuildQuestion(string stage)
    {
        return stage switch
        {
            "language" => new
            {
                question = "What language(s) should the agent speak?",
                multiSelect = false,
                options = new[]
                {
                    new { label = "English", description = "English only" },
                    new { label = "Spanish", description = "Spanish only" },
                    new { label = "French", description = "French only" },
                    new { label = "Multilingual", description = "Speaks multiple languages — switches based on the callee" }
                }
            },
            "task" => new
            {
                question = "¿Qué debería hacer tu agente?",
                multiSelect = false,
                options = new[]
                {
                    new { label = "Calificar prospectos", description = "Hacer preguntas clave y decidir si el prospecto es apto" },
                    new { label = "Agendar demos/citas", description = "Confirmar interés y reservar un horario" },
                    new { label = "Seguimiento de leads", description = "Reactivar prospectos fríos y moverlos al siguiente paso" },
                    new { label = "Encuestas", description = "Llamar para recopilar opiniones y medir satisfacción" }
                }
            },
            "audience" => new
            {
                question = "¿Quiénes suelen ser las personas a las que llamará este agente?",
                multiSelect = false,
                options = new[]
                {
                    new { label = "Leads fríos", description = "Personas que no respondieron o dejaron de contestar" },
                    new { label = "Leads recientes", description = "Personas que acaban de registrarse o pedir información" },
                    new { label = "Prospectos en negociación", description = "Personas que ya hablaron y están evaluando la oferta" },
                    new { label = "Clientes existentes", description = "Clientes actuales para renovar, upsell o reactivar" }
                }
            },
            "tone" => new
            {
                question = "¿Qué tono debería usar el agente?",
                multiSelect = false,
                options = new[]
                {
                    new { label = "Profesional", description = "Directo, claro y formal" },
                    new { label = "Amigable", description = "Cercano y conversacional" },
                    new { label = "Empático", description = "Paciente, valida objeciones y preocupaciones" },
                    new { label = "Seguro", description = "Confiado, orientado a cerrar el siguiente paso" }
                }
            },
            _ => new { question = string.Empty, multiSelect = false, options = Array.Empty<object>() }
        };
    }

    private static void UpsertArtifact(WizardSessionState session, string key, string value)
        => session.Artifact[key] = value;

    private async Task<WizardSessionState?> LoadSessionAsync(string sessionId, CancellationToken ct)
    {
        if (_sessionsCollection is not null)
        {
            var doc = await _sessionsCollection.Find(x => x.Id == sessionId).FirstOrDefaultAsync(ct);
            if (doc is null)
                return null;
            return new WizardSessionState
            {
                Id = doc.Id,
                TenantId = doc.TenantId,
                Stage = doc.Stage,
                UpdatedAt = doc.UpdatedAt,
                Artifact = new Dictionary<string, string>(doc.Artifact ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
            };
        }

        return Sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    private async Task SaveSessionAsync(WizardSessionState session, CancellationToken ct)
    {
        session.UpdatedAt = DateTimeOffset.UtcNow;
        if (_sessionsCollection is not null)
        {
            var doc = new WizardSessionDocument
            {
                Id = session.Id,
                TenantId = session.TenantId,
                Stage = session.Stage,
                Artifact = session.Artifact,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt
            };

            await _sessionsCollection.ReplaceOneAsync(
                x => x.Id == doc.Id,
                doc,
                new ReplaceOptions { IsUpsert = true },
                ct);
            return;
        }

        Sessions[session.Id] = session;
    }

    private async Task RecordWizardEventAsync(WizardSessionState session, string action, object payload, CancellationToken ct)
    {
        var evt = new WizardEventDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = session.TenantId,
            SessionId = session.Id,
            Action = action,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow
        };

        if (_eventsCollection is not null)
        {
            await _eventsCollection.InsertOneAsync(evt, cancellationToken: ct);
            return;
        }

        SessionEvents.Enqueue(evt);
        while (SessionEvents.Count > 5000 && SessionEvents.TryDequeue(out _))
        {
            // keep bounded
        }
    }

    private async Task<List<WizardEventDocument>> LoadWizardEventsAsync(string tenantId, int limit, CancellationToken ct)
    {
        var bounded = Math.Clamp(limit, 10, 5000);
        if (_eventsCollection is not null)
        {
            return await _eventsCollection.Find(x => x.TenantId == tenantId)
                .SortByDescending(x => x.OccurredAt)
                .Limit(bounded)
                .ToListAsync(ct);
        }

        return SessionEvents
            .Where(x => x.TenantId.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.OccurredAt)
            .Take(bounded)
            .ToList();
    }

    private static string ExtractPayloadField(string payloadJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return string.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty(fieldName, out var el))
                return el.GetString() ?? string.Empty;
        }
        catch
        {
            // ignore
        }

        return string.Empty;
    }

    private static string BuildAssistantName(WizardSessionState session)
    {
        var task = session.Artifact.GetValueOrDefault("Task") ?? "Outbound";
        return $"{task} (Outbound)";
    }

    private static string BuildFirstMessage(WizardSessionState session)
    {
        var role = session.Artifact.GetValueOrDefault("Role") ?? "asistente";
        return $"Hola, soy tu {role}. ¿Tienes un minuto para continuar?";
    }

    private static string MapLanguageToCode(string language)
    {
        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "english" => "en",
            "spanish" => "es",
            "french" => "fr",
            "multilingual" => "es",
            _ => "es"
        };
    }

    public sealed record WizardSessionCreateRequest
    {
        public string? Mode { get; init; }
        public string? TenantId { get; init; }
    }

    public sealed record WizardSessionAnswerRequest
    {
        public required string Answer { get; init; }
    }

    private sealed class WizardSessionState
    {
        public string Id { get; init; } = string.Empty;
        public string TenantId { get; init; } = "platform";
        public string Stage { get; set; } = "language";
        public Dictionary<string, string> Artifact { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class WizardSessionDocument
    {
        public string Id { get; init; } = string.Empty;
        public string TenantId { get; init; } = "platform";
        public string Stage { get; init; } = "language";
        public Dictionary<string, string> Artifact { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    }

    private sealed class WizardEventDocument
    {
        public string Id { get; init; } = string.Empty;
        public string TenantId { get; init; } = "platform";
        public string SessionId { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Payload { get; init; } = "{}";
        public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    }
}
