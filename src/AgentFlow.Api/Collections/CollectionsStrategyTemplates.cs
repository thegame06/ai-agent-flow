using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;

namespace AgentFlow.Api.Collections;

/// <summary>
/// A starting collections strategy: one campaign per delinquency stage, each with its own tone,
/// daily schedule, contact window and follow-ups. Created as drafts: nothing is sent until an
/// operator reviews and activates each one.
/// </summary>
public static class CollectionsStrategyTemplates
{
    private sealed record StageTemplate(string Stage, string Name, string Goal, string Message, CampaignFollowUpPolicy FollowUps);

    private const string PromiseReminder =
        "Hola {{primer_nombre}}, te recordamos tu compromiso de pago de {{monto_promesa}} {{moneda}} para el {{fecha_promesa}}. Gracias.";
    private const string PromiseBroken =
        "Hola {{primer_nombre}}, no vimos reflejado tu pago del {{fecha_promesa}}. Tu saldo vencido es {{total_vencido}} {{moneda}}. ¿Cuando podrias pagar?";

    private static readonly StageTemplate[] Stages =
    [
        new("Dpd1To30", "Cobranza 1-30 dias: recordatorio amable",
            "Recordar el pago a clientes con atraso temprano antes de que crezca la mora.",
            "Hola {{primer_nombre}}, te recordamos que tienes un saldo vencido de {{total_vencido}} {{moneda}} ({{dias_atraso}} dias). ¿Cuando podrias realizar tu pago? Respondenos con la fecha.",
            Policy(
                Rule("sin-respuesta", "no_response", "message", after: 48,
                    message: "Hola {{primer_nombre}}, seguimos pendientes de tu pago de {{total_vencido}} {{moneda}}. ¿Nos confirmas cuando podrias pagar?"),
                Rule("recordatorio-promesa", "promise_due", "message", before: 24, message: PromiseReminder),
                Rule("promesa-incumplida", "promise_broken", "message", after: 2, message: PromiseBroken),
                Rule("llamar-despues", "outcome:callback_requested", "message", after: 24,
                    message: "Hola {{primer_nombre}}, como quedamos te volvemos a escribir. ¿Cuando podrias pagar tu saldo de {{total_vencido}} {{moneda}}?"),
                Rule("asesor", "outcome:needs_human", "handoff"))),
        new("Dpd31To60", "Cobranza 31-60 dias: compromiso de pago",
            "Conseguir una fecha y monto de pago concretos.",
            "Hola {{primer_nombre}}, tu cuenta tiene {{dias_atraso}} dias de atraso y un saldo vencido de {{total_vencido}} {{moneda}}. Queremos ayudarte a ponerte al corriente: ¿que fecha y monto puedes pagar?",
            Policy(
                Rule("sin-respuesta", "no_response", "message", after: 24,
                    message: "Hola {{primer_nombre}}, es importante regularizar tu saldo de {{total_vencido}} {{moneda}}. Respondenos con la fecha y el monto que puedes pagar."),
                Rule("recordatorio-promesa", "promise_due", "message", before: 24, message: PromiseReminder),
                Rule("promesa-incumplida", "promise_broken", "message", after: 2, message: PromiseBroken),
                Rule("no-puede-pagar", "outcome:cannot_pay", "handoff"),
                Rule("asesor", "outcome:needs_human", "handoff"))),
        new("Dpd61To90", "Cobranza 61-90 dias: convenio",
            "Ofrecer un convenio antes de que la cuenta pase a mora grave.",
            "Hola {{primer_nombre}}, tu saldo vencido de {{total_vencido}} {{moneda}} tiene {{dias_atraso}} dias de atraso. Podemos ofrecerte un convenio de pago. ¿Te interesa que un asesor te contacte?",
            Policy(
                Rule("sin-respuesta", "no_response", "message", after: 24,
                    message: "Hola {{primer_nombre}}, aun puedes acceder a un convenio para tu saldo de {{total_vencido}} {{moneda}}. Respondenos SI para que un asesor te contacte."),
                Rule("interesado", "outcome:interested", "handoff"),
                Rule("recordatorio-promesa", "promise_due", "message", before: 24, message: PromiseReminder),
                Rule("promesa-incumplida", "promise_broken", "handoff"),
                Rule("no-puede-pagar", "outcome:cannot_pay", "handoff"),
                Rule("asesor", "outcome:needs_human", "handoff"))),
        new("Dpd90Plus", "Cobranza 90+ dias: gestion con asesor",
            "Pasar a un asesor a todo cliente que responda; mora grave.",
            "Hola {{primer_nombre}}, tu cuenta tiene un saldo vencido de {{total_vencido}} {{moneda}} con {{dias_atraso}} dias de atraso. Comunicate con nosotros para revisar opciones de pago.",
            Policy(
                Rule("sin-respuesta", "no_response", "handoff", after: 48),
                Rule("respondio", "outcome:replied", "handoff"),
                Rule("interesado", "outcome:interested", "handoff"),
                Rule("promesa-incumplida", "promise_broken", "handoff"),
                Rule("no-puede-pagar", "outcome:cannot_pay", "handoff"),
                Rule("asesor", "outcome:needs_human", "handoff")))
    ];

    /// <param name="timezone">Customers' timezone (schedule and contact window).</param>
    public static IReadOnlyList<CampaignContract> Build(string tenantId, string timezone, string channel, string actor, DateTimeOffset now)
    {
        var window = JsonSerializer.Serialize(new { startHour = 8, endHour = 20, days = new[] { 1, 2, 3, 4, 5, 6 } });
        var throttle = JsonSerializer.Serialize(new { maxPerRecipientPerDay = 1 });
        return Stages.Select(t => new CampaignContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Name = t.Name,
            Description = t.Goal,
            Goal = t.Goal,
            Status = CampaignStatus.Draft,
            CampaignType = CampaignType.Collections,
            ExecutionMode = CampaignExecutionMode.Direct,
            TriggerType = CampaignTriggerType.Schedule,
            ChannelAction = CampaignChannelAction.Message,
            Channel = channel,
            MessageDraft = t.Message,
            ScheduleType = CampaignScheduleType.Daily,
            ScheduleExpression = "10:00",
            Timezone = timezone,
            StartAt = now,
            ExecutionWindowJson = window,
            ThrottleJson = throttle,
            AudienceFilterJson = JsonSerializer.Serialize(new { source = "collections", stages = new[] { t.Stage }, excludeCampaignContactDays = 3 }),
            FollowupPolicyJson = JsonSerializer.Serialize(t.FollowUps, CampaignFollowUpPolicy.Json),
            Enabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = actor
        }).ToList();
    }

    private static CampaignFollowUpPolicy Policy(params CampaignFollowUpRule[] rules) => new() { Enabled = true, MaxFollowUps = 3, Rules = rules.ToList() };

    private static CampaignFollowUpRule Rule(string id, string trigger, string action, double after = 0, double before = 0, string? message = null)
        => new() { Id = id, Trigger = trigger, Action = action, AfterHours = after, BeforeHours = before, Message = message };
}
