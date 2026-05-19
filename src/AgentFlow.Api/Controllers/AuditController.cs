using System.Text.Json;
using System.Text;
using System.Globalization;
using AgentFlow.Abstractions;
using AgentFlow.Api.Commerce;
using AgentFlow.Application.Memory;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Domain.ValueObjects;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/audit")]
[Authorize]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditMemory _auditMemory;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IChannelMessageRepository _messageRepo;
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IConversationThreadRepository _threadRepo;
    private readonly IAgentExecutionRepository _executionRepo;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly ICommerceStore? _commerce;

    public AuditController(
        IAuditMemory auditMemory,
        ITenantContextAccessor tenantContext,
        IChannelSessionRepository sessionRepo,
        IChannelMessageRepository messageRepo,
        IChannelDefinitionRepository channelRepo,
        IConversationThreadRepository threadRepo,
        IAgentExecutionRepository executionRepo,
        IAgentDefinitionRepository agentRepo,
        ICommerceStore? commerce = null)
    {
        _auditMemory = auditMemory;
        _tenantContext = tenantContext;
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _channelRepo = channelRepo;
        _threadRepo = threadRepo;
        _executionRepo = executionRepo;
        _agentRepo = agentRepo;
        _commerce = commerce;
    }

    [HttpGet]
    public async Task<IActionResult> GetAuditLogs(
        [FromRoute] string tenantId,
        [FromQuery] int limit = 100,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();

        var boundedLimit = Math.Clamp(limit, 1, 500);

        IReadOnlyList<AuditEntry> logs = !string.IsNullOrWhiteSpace(correlationId)
            ? await _auditMemory.GetByCorrelationAsync(tenantId, correlationId, boundedLimit, ct)
            : await _auditMemory.GetRecentAsync(tenantId, boundedLimit, ct);

        if (!string.IsNullOrWhiteSpace(action))
        {
            logs = logs
                .Where(x => string.Equals(x.EventType.ToString(), action, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (from.HasValue)
            logs = logs.Where(x => x.OccurredAt >= from.Value).ToList();
        if (to.HasValue)
            logs = logs.Where(x => x.OccurredAt <= to.Value).ToList();

        return Ok(logs.Select(l => new
        {
            l.Id,
            l.OccurredAt,
            Actor = l.UserId,
            Action = l.EventType.ToString(),
            Resource = l.AgentId,
            Severity = GetSeverity(l.EventType),
            l.CorrelationId,
            l.ExecutionId,
            l.EventJson,
            Ip = "internal"
        }));
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportAuditLogs(
        [FromRoute] string tenantId,
        [FromQuery] int limit = 100,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();

        var boundedLimit = Math.Clamp(limit, 1, 5000);
        IReadOnlyList<AuditEntry> logs = !string.IsNullOrWhiteSpace(correlationId)
            ? await _auditMemory.GetByCorrelationAsync(tenantId, correlationId, boundedLimit, ct)
            : await _auditMemory.GetRecentAsync(tenantId, boundedLimit, ct);

        if (!string.IsNullOrWhiteSpace(action))
            logs = logs.Where(x => string.Equals(x.EventType.ToString(), action, StringComparison.OrdinalIgnoreCase)).ToList();
        if (from.HasValue)
            logs = logs.Where(x => x.OccurredAt >= from.Value).ToList();
        if (to.HasValue)
            logs = logs.Where(x => x.OccurredAt <= to.Value).ToList();

        var ordered = logs.OrderByDescending(x => x.OccurredAt).ToList();
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var payload = ordered.Select(l => new
            {
                l.Id,
                l.OccurredAt,
                Actor = l.UserId,
                Action = l.EventType.ToString(),
                Resource = l.AgentId,
                Severity = GetSeverity(l.EventType),
                l.CorrelationId,
                l.ExecutionId,
                l.EventJson
            });

            return File(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)),
                "application/json",
                $"audit-{tenantId}-{stamp}.json");
        }

        var sb = new StringBuilder();
        sb.AppendLine("FechaHoraUtc,Actor,Evento,Recurso,CorrelationId,ExecutionId,Severidad,DetalleTecnico");
        foreach (var row in ordered)
        {
            var columns = new[]
            {
                row.OccurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                row.UserId ?? string.Empty,
                row.EventType.ToString(),
                row.AgentId ?? string.Empty,
                row.CorrelationId ?? string.Empty,
                row.ExecutionId ?? string.Empty,
                GetSeverity(row.EventType),
                row.EventJson ?? string.Empty
            };
            sb.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
        }

        return File(
            Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv; charset=utf-8",
            $"audit-{tenantId}-{stamp}.csv");
    }

    [HttpGet("correlations")]
    public async Task<IActionResult> GetCorrelationSummary(
        [FromRoute] string tenantId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();

        var boundedLimit = Math.Clamp(limit, 1, 300);
        var recent = await _auditMemory.GetRecentAsync(tenantId, 2000, ct);

        var summary = recent
            .Where(x => !string.IsNullOrWhiteSpace(x.CorrelationId))
            .GroupBy(x => x.CorrelationId)
            .Select(g => new
            {
                CorrelationId = g.Key,
                EventCount = g.Count(),
                FirstOccurredAt = g.Min(x => x.OccurredAt),
                LastOccurredAt = g.Max(x => x.OccurredAt),
                Actions = g.Select(x => x.EventType.ToString()).Distinct().Take(6).ToArray(),
                Agents = g.Select(x => x.AgentId).Distinct().Take(6).ToArray()
            })
            .OrderByDescending(x => x.LastOccurredAt)
            .Take(boundedLimit)
            .ToList();

        return Ok(summary);
    }

    [HttpGet("journey/{correlationId}")]
    public async Task<IActionResult> GetJourney(
        [FromRoute] string tenantId,
        [FromRoute] string correlationId,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(correlationId)) return BadRequest("correlationId is required.");

        var auditEntries = (await _auditMemory.GetByCorrelationAsync(tenantId, correlationId, 2000, ct))
            .OrderBy(x => x.OccurredAt)
            .ToList();

        var session = await _sessionRepo.GetByIdAsync(correlationId, tenantId, ct)
            ?? await _sessionRepo.GetByThreadIdAsync(correlationId, tenantId, ct);

        ConversationThread? thread = null;
        if (!string.IsNullOrWhiteSpace(session?.ThreadId))
            thread = await _threadRepo.GetByIdAsync(session.ThreadId, tenantId, ct);
        else
            thread = await _threadRepo.GetByIdAsync(correlationId, tenantId, ct);

        var channel = session is null
            ? null
            : await _channelRepo.GetByIdAsync(session.ChannelId, tenantId, ct);

        var messages = session is null
            ? []
            : (await _messageRepo.GetBySessionAsync(session.Id, tenantId, 200, ct))
                .OrderBy(x => x.CreatedAt)
                .ToList();

        var executions = (await _executionRepo.GetByCorrelationIdAsync(correlationId, tenantId, 200, ct))
            .OrderBy(x => x.CreatedAt)
            .ToList();

        var agentIds = executions
            .Select(x => x.AgentDefinitionId)
            .Concat(auditEntries.Select(x => x.AgentId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var agentNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var agentId in agentIds)
        {
            var agent = await _agentRepo.GetByIdAsync(agentId, tenantId, ct);
            if (agent is not null)
                agentNames[agentId] = agent.Name;
        }

        CommercePartyDocument? party = null;
        IReadOnlyList<CommerceSaleDocument> sales = [];
        IReadOnlyList<CommerceInvoiceDocument> invoices = [];

        if (_commerce is not null && session is not null)
        {
            party = await _commerce.GetPartyByIdentityAsync(tenantId, session.ChannelType, session.Identifier, ct);
            if (party is not null)
            {
                sales = await _commerce.SearchSalesAsync(tenantId, party.Id, null, 0, 20, ct);
                invoices = await _commerce.SearchInvoicesAsync(tenantId, party.Id, null, 0, 20, ct);
            }
        }

        var tools = BuildToolSummary(executions);
        var workflowRefs = BuildWorkflowSummary(auditEntries);
        var decisions = BuildDecisionSummary(auditEntries, executions, agentNames);
        var timeline = BuildJourneyTimeline(
            correlationId,
            session,
            channel,
            messages,
            auditEntries,
            executions,
            agentNames,
            sales,
            invoices);

        var firstCustomerMessage = messages.FirstOrDefault(x => x.Direction == MessageDirection.Incoming);
        var lastOutbound = messages.LastOrDefault(x => x.Direction == MessageDirection.Outgoing);
        var paidInvoices = invoices.Where(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase)).ToList();
        var becameCustomer = string.Equals(party?.Kind, "customer", StringComparison.OrdinalIgnoreCase);
        var commercialStage = ResolveCommercialStage(party, sales, invoices);

        var summary = new JourneySummaryDto
        {
            CorrelationId = correlationId,
            StartedAt = timeline.FirstOrDefault()?.OccurredAt
                ?? auditEntries.FirstOrDefault()?.OccurredAt
                ?? session?.CreatedAt
                ?? thread?.CreatedAt,
            LastUpdatedAt = timeline.LastOrDefault()?.OccurredAt
                ?? auditEntries.LastOrDefault()?.OccurredAt
                ?? session?.LastActivityAt
                ?? thread?.LastActivityAt,
            CurrentStage = commercialStage,
            CustomerBecameClient = becameCustomer || paidInvoices.Count > 0 || sales.Count > 0,
            SessionStatus = session?.Status.ToString() ?? "unknown",
            Channel = channel?.Name ?? session?.ChannelType ?? "unknown",
            Customer = new JourneyCustomerDto
            {
                Identifier = session?.Identifier ?? party?.Identifier ?? string.Empty,
                DisplayName = party?.DisplayName ?? party?.FullName ?? session?.Metadata.GetValueOrDefault("display_name") ?? session?.Identifier,
                Kind = party?.Kind ?? session?.Metadata.GetValueOrDefault("customer_kind") ?? "lead"
            },
            FirstCustomerMessage = firstCustomerMessage?.Content,
            LastVisibleReply = lastOutbound?.Content,
            AgentCount = executions.Select(x => x.AgentDefinitionId).Distinct(StringComparer.Ordinal).Count(),
            WorkflowCount = workflowRefs.Count,
            ToolCount = tools.Count,
            MessageCount = messages.Count,
            SalesCount = sales.Count,
            InvoicesCount = invoices.Count,
            PaidInvoicesCount = paidInvoices.Count,
            SalesTotal = sales.Sum(x => x.Total),
            InvoicedTotal = invoices.Sum(x => x.Total),
            PaidTotal = paidInvoices.Sum(x => x.Total)
        };

        return Ok(new JourneyResponseDto
        {
            Summary = summary,
            CrossCutting = new JourneyCrossCuttingDto
            {
                Session = session is null ? null : new JourneySessionDto
                {
                    SessionId = session.Id,
                    ThreadId = session.ThreadId,
                    ChannelId = session.ChannelId,
                    ChannelType = session.ChannelType,
                    Status = session.Status.ToString(),
                    WindowOpen = !session.IsExpired(),
                    CreatedAt = session.CreatedAt,
                    LastActivityAt = session.LastActivityAt,
                    ExpiresAt = session.ExpiresAt
                },
                Thread = thread is null ? null : new JourneyThreadDto
                {
                    ThreadId = thread.Id,
                    Status = thread.Status.ToString(),
                    TurnCount = thread.TurnCount,
                    CreatedAt = thread.CreatedAt,
                    LastActivityAt = thread.LastActivityAt
                },
                Agents = executions
                    .GroupBy(x => x.AgentDefinitionId)
                    .Select(g => new JourneyAgentDto
                    {
                        AgentId = g.Key,
                        AgentName = agentNames.GetValueOrDefault(g.Key, g.Key),
                        ExecutionCount = g.Count(),
                        Statuses = g.Select(x => x.Status.ToString()).Distinct().ToList(),
                        Roles = g.Select(x => x.AgentSystemRole).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList()
                    })
                    .OrderByDescending(x => x.ExecutionCount)
                    .ToList(),
                Tools = tools,
                Workflows = workflowRefs,
                Decisions = decisions
            },
            Timeline = timeline
        });
    }

    private static List<JourneyToolDto> BuildToolSummary(IReadOnlyList<AgentExecution> executions)
    {
        return executions
            .SelectMany(x => x.Steps)
            .Where(x => !string.IsNullOrWhiteSpace(x.ToolName))
            .GroupBy(x => x.ToolName!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new JourneyToolDto
            {
                ToolName = g.Key,
                Invocations = g.Count(),
                SuccessCount = g.Count(x => x.IsSuccess),
                FailureCount = g.Count(x => !x.IsSuccess),
                FirstUsedAt = g.Min(x => x.StartedAt),
                LastUsedAt = g.Max(x => x.CompletedAt ?? x.StartedAt)
            })
            .OrderByDescending(x => x.Invocations)
            .ToList();
    }

    private static List<JourneyWorkflowDto> BuildWorkflowSummary(IReadOnlyList<AuditEntry> auditEntries)
    {
        var results = new List<JourneyWorkflowDto>();

        foreach (var entry in auditEntries.Where(x => x.EventType == AuditEventType.ConnectOperation || x.EventType == AuditEventType.RoutingDecision))
        {
            var payload = ParseJson(entry.EventJson);
            if (payload is null) continue;

            var workflowId =
                ReadString(payload, "workflowId")
                ?? ReadString(payload, "workflowExecutionId")
                ?? ReadNestedString(payload, "details", "workflowId")
                ?? ReadNestedString(payload, "details", "workflowExecutionId");

            if (string.IsNullOrWhiteSpace(workflowId)) continue;

            results.Add(new JourneyWorkflowDto
            {
                WorkflowId = workflowId,
                Action = ReadString(payload, "action")
                    ?? ReadNestedString(payload, "details", "action")
                    ?? entry.EventType.ToString(),
                OccurredAt = entry.OccurredAt
            });
        }

        return results
            .GroupBy(x => $"{x.WorkflowId}|{x.Action}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.OccurredAt).First())
            .OrderBy(x => x.OccurredAt)
            .ToList();
    }

    private static List<JourneyDecisionDto> BuildDecisionSummary(
        IReadOnlyList<AuditEntry> auditEntries,
        IReadOnlyList<AgentExecution> executions,
        IReadOnlyDictionary<string, string> agentNames)
    {
        var results = new List<JourneyDecisionDto>();

        foreach (var entry in auditEntries.OrderBy(x => x.OccurredAt))
        {
            var payload = ParseJson(entry.EventJson);

            if (entry.EventType == AuditEventType.RoutingDecision)
            {
                var workflowId =
                    ReadString(payload, "workflowExecutionId")
                    ?? ReadString(payload, "triggeredWorkflow")
                    ?? ReadNestedString(payload, "triggeredWorkflow", "workflowExecutionId");

                results.Add(new JourneyDecisionDto
                {
                    Kind = "routing",
                    Title = "Se tomo una decision de enrutamiento",
                    Explanation = string.IsNullOrWhiteSpace(workflowId)
                        ? "El router evaluo el mensaje y decidio como continuar la conversacion."
                        : $"El router detecto una intencion y disparo el flujo {workflowId}.",
                    OccurredAt = entry.OccurredAt,
                    Source = agentNames.GetValueOrDefault(entry.AgentId, entry.AgentId)
                });
            }
            else if (entry.EventType == AuditEventType.HandoffRequested || entry.EventType == AuditEventType.HandoffCompleted)
            {
                var sourceAgent = ReadString(payload, "sourceAgentId") ?? ReadString(payload, "sourceAgent");
                var targetAgent = ReadString(payload, "targetAgentId") ?? ReadString(payload, "targetAgent");
                results.Add(new JourneyDecisionDto
                {
                    Kind = "handoff",
                    Title = entry.EventType == AuditEventType.HandoffRequested
                        ? "Se pidio apoyo a otro agente"
                        : "La conversacion fue transferida entre agentes",
                    Explanation = $"Origen: {agentNames.GetValueOrDefault(sourceAgent ?? string.Empty, sourceAgent ?? "desconocido")}. Destino: {agentNames.GetValueOrDefault(targetAgent ?? string.Empty, targetAgent ?? "desconocido")}.",
                    OccurredAt = entry.OccurredAt,
                    Source = agentNames.GetValueOrDefault(entry.AgentId, entry.AgentId)
                });
            }
            else if (entry.EventType == AuditEventType.SecurityViolation)
            {
                results.Add(new JourneyDecisionDto
                {
                    Kind = "security",
                    Title = "Una accion fue bloqueada por seguridad",
                    Explanation = ReadString(payload, "error") ?? ReadString(payload, "message") ?? "El sistema detuvo una accion por reglas de seguridad.",
                    OccurredAt = entry.OccurredAt,
                    Source = agentNames.GetValueOrDefault(entry.AgentId, entry.AgentId)
                });
            }
        }

        foreach (var execution in executions.Where(x => x.Status == ExecutionStatus.Failed))
        {
            results.Add(new JourneyDecisionDto
            {
                Kind = "execution",
                Title = "Una ejecucion termino con error",
                Explanation = execution.ErrorMessage ?? execution.ErrorCode ?? "La ejecucion fallo sin detalle adicional.",
                OccurredAt = execution.CompletedAt ?? execution.CreatedAt,
                Source = agentNames.GetValueOrDefault(execution.AgentDefinitionId, execution.AgentDefinitionId)
            });
        }

        return results
            .OrderBy(x => x.OccurredAt)
            .Take(50)
            .ToList();
    }

    private static List<JourneyTimelineItemDto> BuildJourneyTimeline(
        string correlationId,
        ChannelSession? session,
        ChannelDefinition? channel,
        IReadOnlyList<ChannelMessage> messages,
        IReadOnlyList<AuditEntry> auditEntries,
        IReadOnlyList<AgentExecution> executions,
        IReadOnlyDictionary<string, string> agentNames,
        IReadOnlyList<CommerceSaleDocument> sales,
        IReadOnlyList<CommerceInvoiceDocument> invoices)
    {
        var items = new List<JourneyTimelineItemDto>();

        if (session is not null)
        {
            items.Add(new JourneyTimelineItemDto
            {
                Id = $"session:{session.Id}",
                OccurredAt = session.CreatedAt,
                Category = "session",
                Title = "Se abrio la sesion",
                Description = $"La conversacion entro por {channel?.Name ?? session.ChannelType} con el identificador {session.Identifier}.",
                Detail = $"Estado inicial: {session.Status}. CorrelationId: {correlationId}."
            });
        }

        foreach (var message in messages)
        {
            var isIncoming = message.Direction == MessageDirection.Incoming;
            var actor = message.Metadata.GetValueOrDefault("actor")
                ?? (isIncoming ? "customer" : "system");

            items.Add(new JourneyTimelineItemDto
            {
                Id = $"message:{message.Id}",
                OccurredAt = message.CreatedAt,
                Category = isIncoming ? "customer_message" : "reply",
                Title = isIncoming ? "El cliente escribio" : "Se envio una respuesta",
                Description = message.Content,
                Detail = $"Actor: {actor}. Estado: {message.Status}. Delivery: {message.Metadata.GetValueOrDefault("agentflow.delivery") ?? (isIncoming ? "received" : "sent")}."
            });
        }

        foreach (var execution in executions)
        {
            var agentLabel = agentNames.GetValueOrDefault(execution.AgentDefinitionId, execution.AgentDefinitionId);
            items.Add(new JourneyTimelineItemDto
            {
                Id = $"execution:{execution.Id}",
                OccurredAt = execution.StartedAt ?? execution.CreatedAt,
                Category = "agent_execution",
                Title = $"Arranco el agente {agentLabel}",
                Description = execution.Input.UserMessage,
                Detail = $"Rol: {execution.AgentSystemRole}. Estado final: {execution.Status}. Pasos: {execution.Steps.Count}."
            });

            var topTools = execution.Steps
                .Where(x => !string.IsNullOrWhiteSpace(x.ToolName))
                .GroupBy(x => x.ToolName!, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key} x{g.Count()}")
                .Take(5)
                .ToList();

            if (topTools.Count > 0)
            {
                items.Add(new JourneyTimelineItemDto
                {
                    Id = $"execution-tools:{execution.Id}",
                    OccurredAt = execution.CompletedAt ?? execution.CreatedAt,
                    Category = "tool_usage",
                    Title = $"Herramientas usadas por {agentLabel}",
                    Description = string.Join(", ", topTools),
                    Detail = $"Tools detectadas en los pasos de la ejecucion {execution.Id}."
                });
            }
        }

        foreach (var entry in auditEntries)
        {
            var payload = ParseJson(entry.EventJson);
            var agentLabel = agentNames.GetValueOrDefault(entry.AgentId, entry.AgentId);

            switch (entry.EventType)
            {
                case AuditEventType.RoutingDecision:
                {
                    var workflowId =
                        ReadString(payload, "workflowExecutionId")
                        ?? ReadString(payload, "triggeredWorkflow")
                        ?? ReadNestedString(payload, "triggeredWorkflow", "workflowExecutionId");
                    items.Add(new JourneyTimelineItemDto
                    {
                        Id = $"audit:{entry.Id}",
                        OccurredAt = entry.OccurredAt,
                        Category = "routing",
                        Title = "El sistema decidio como atender el mensaje",
                        Description = string.IsNullOrWhiteSpace(workflowId)
                            ? "Se clasifico el mensaje para decidir el siguiente paso."
                            : $"Se clasifico el mensaje y se lanzo el flujo {workflowId}.",
                        Detail = $"Decision registrada por {agentLabel}."
                    });
                    break;
                }
                case AuditEventType.HandoffRequested:
                case AuditEventType.HandoffCompleted:
                case AuditEventType.HandoffFailed:
                {
                    var sourceAgent = ReadString(payload, "sourceAgentId") ?? ReadString(payload, "sourceAgent");
                    var targetAgent = ReadString(payload, "targetAgentId") ?? ReadString(payload, "targetAgent");
                    var verb = entry.EventType switch
                    {
                        AuditEventType.HandoffRequested => "Se solicito apoyo a otro agente",
                        AuditEventType.HandoffCompleted => "Otro agente tomo el caso",
                        _ => "La transferencia entre agentes fallo"
                    };

                    items.Add(new JourneyTimelineItemDto
                    {
                        Id = $"audit:{entry.Id}",
                        OccurredAt = entry.OccurredAt,
                        Category = "handoff",
                        Title = verb,
                        Description = $"Origen: {agentNames.GetValueOrDefault(sourceAgent ?? string.Empty, sourceAgent ?? "desconocido")}. Destino: {agentNames.GetValueOrDefault(targetAgent ?? string.Empty, targetAgent ?? "desconocido")}.",
                        Detail = $"Evento auditado como {entry.EventType}."
                    });
                    break;
                }
                case AuditEventType.ExecutionFailed:
                {
                    items.Add(new JourneyTimelineItemDto
                    {
                        Id = $"audit:{entry.Id}",
                        OccurredAt = entry.OccurredAt,
                        Category = "error",
                        Title = "Hubo una falla en la atencion",
                        Description = ReadString(payload, "error") ?? "La ejecucion no pudo completar una respuesta segura.",
                        Detail = $"Registrado por {agentLabel}."
                    });
                    break;
                }
                case AuditEventType.SecurityViolation:
                {
                    items.Add(new JourneyTimelineItemDto
                    {
                        Id = $"audit:{entry.Id}",
                        OccurredAt = entry.OccurredAt,
                        Category = "security",
                        Title = "Seguridad bloqueo una accion",
                        Description = ReadString(payload, "error") ?? ReadString(payload, "message") ?? "La plataforma bloqueo una accion por politica.",
                        Detail = $"Registrado por {agentLabel}."
                    });
                    break;
                }
                case AuditEventType.ConnectOperation:
                {
                    var action =
                        ReadString(payload, "action")
                        ?? ReadNestedString(payload, "details", "action")
                        ?? "workflow.action";

                    if (action.Contains("workflow", StringComparison.OrdinalIgnoreCase))
                    {
                        items.Add(new JourneyTimelineItemDto
                        {
                            Id = $"audit:{entry.Id}",
                            OccurredAt = entry.OccurredAt,
                            Category = "workflow",
                            Title = "Hubo actividad de workflow",
                            Description = action,
                            Detail = $"Workflow o control runtime registrado por {agentLabel}."
                        });
                    }
                    break;
                }
            }
        }

        foreach (var sale in sales)
        {
            items.Add(new JourneyTimelineItemDto
            {
                Id = $"sale:{sale.Id}",
                OccurredAt = sale.CreatedAt,
                Category = "commerce",
                Title = "Se creo una venta",
                Description = $"Venta {sale.Id} por {sale.Total:0.##} {sale.Currency}.",
                Detail = $"Estado comercial: {sale.State}. Metodo de pago: {sale.PaymentMethod}."
            });
        }

        foreach (var invoice in invoices)
        {
            items.Add(new JourneyTimelineItemDto
            {
                Id = $"invoice:{invoice.Id}",
                OccurredAt = invoice.CreatedAt,
                Category = "commerce",
                Title = "Se genero una factura",
                Description = $"Factura {invoice.Number} por {invoice.Total:0.##} {invoice.Currency}.",
                Detail = $"Estado: {invoice.Status}."
            });
        }

        return items
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static string ResolveCommercialStage(
        CommercePartyDocument? party,
        IReadOnlyList<CommerceSaleDocument> sales,
        IReadOnlyList<CommerceInvoiceDocument> invoices)
    {
        if (invoices.Any(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase))) return "paid";
        if (invoices.Count > 0) return "invoiced";
        if (sales.Count > 0) return "sale_created";
        if (string.Equals(party?.Kind, "customer", StringComparison.OrdinalIgnoreCase)) return "customer";
        return "lead";
    }

    private bool CanAccess(string tenantId)
    {
        var context = _tenantContext.Current!;
        return context.TenantId == tenantId || context.IsPlatformAdmin;
    }

    private static JsonElement? ParseJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement? element, string propertyName)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return null;
        if (!element.Value.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static string? ReadNestedString(JsonElement? element, string parentProperty, string propertyName)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return null;
        if (!element.Value.TryGetProperty(parentProperty, out var parent) || parent.ValueKind != JsonValueKind.Object) return null;
        return ReadString(parent, propertyName);
    }

    private static string GetSeverity(AuditEventType type) => type switch
    {
        AuditEventType.ExecutionFailed => "error",
        AuditEventType.SecurityViolation => "critical",
        AuditEventType.ToolFailed => "warning",
        AuditEventType.ExecutionCancelled => "warning",
        _ => "info"
    };

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var normalized = value.Replace("\r", " ").Replace("\n", " ");
        var mustQuote = normalized.Contains(',') || normalized.Contains('"');
        if (!mustQuote) return normalized;
        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }
}

public sealed record JourneyResponseDto
{
    public required JourneySummaryDto Summary { get; init; }
    public required JourneyCrossCuttingDto CrossCutting { get; init; }
    public required List<JourneyTimelineItemDto> Timeline { get; init; }
}

public sealed record JourneySummaryDto
{
    public required string CorrelationId { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? LastUpdatedAt { get; init; }
    public string CurrentStage { get; init; } = "lead";
    public bool CustomerBecameClient { get; init; }
    public string SessionStatus { get; init; } = "unknown";
    public string Channel { get; init; } = "unknown";
    public required JourneyCustomerDto Customer { get; init; }
    public string? FirstCustomerMessage { get; init; }
    public string? LastVisibleReply { get; init; }
    public int AgentCount { get; init; }
    public int WorkflowCount { get; init; }
    public int ToolCount { get; init; }
    public int MessageCount { get; init; }
    public int SalesCount { get; init; }
    public int InvoicesCount { get; init; }
    public int PaidInvoicesCount { get; init; }
    public decimal SalesTotal { get; init; }
    public decimal InvoicedTotal { get; init; }
    public decimal PaidTotal { get; init; }
}

public sealed record JourneyCustomerDto
{
    public string Identifier { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string Kind { get; init; } = "lead";
}

public sealed record JourneyCrossCuttingDto
{
    public JourneySessionDto? Session { get; init; }
    public JourneyThreadDto? Thread { get; init; }
    public List<JourneyAgentDto> Agents { get; init; } = [];
    public List<JourneyToolDto> Tools { get; init; } = [];
    public List<JourneyWorkflowDto> Workflows { get; init; } = [];
    public List<JourneyDecisionDto> Decisions { get; init; } = [];
}

public sealed record JourneySessionDto
{
    public required string SessionId { get; init; }
    public string? ThreadId { get; init; }
    public required string ChannelId { get; init; }
    public required string ChannelType { get; init; }
    public required string Status { get; init; }
    public bool WindowOpen { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record JourneyThreadDto
{
    public required string ThreadId { get; init; }
    public required string Status { get; init; }
    public int TurnCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
}

public sealed record JourneyAgentDto
{
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public int ExecutionCount { get; init; }
    public List<string> Statuses { get; init; } = [];
    public List<string> Roles { get; init; } = [];
}

public sealed record JourneyToolDto
{
    public required string ToolName { get; init; }
    public int Invocations { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public DateTimeOffset FirstUsedAt { get; init; }
    public DateTimeOffset LastUsedAt { get; init; }
}

public sealed record JourneyWorkflowDto
{
    public required string WorkflowId { get; init; }
    public required string Action { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public sealed record JourneyDecisionDto
{
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Explanation { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public sealed record JourneyTimelineItemDto
{
    public required string Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? Detail { get; init; }
}
