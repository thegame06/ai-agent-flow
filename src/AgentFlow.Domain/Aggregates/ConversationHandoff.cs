namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// Who is answering a conversation, kept in the session metadata. While a conversation is
/// waiting for or held by a person, the assistant does not answer: inbound messages are left
/// for the person.
/// </summary>
public static class ConversationHandoff
{
    public const string StateKey = "handoff.state";
    public const string ReasonKey = "handoff.reason";
    public const string RequestedAtKey = "handoff.requested_at";
    public const string RequestedByKey = "handoff.requested_by";
    public const string AssigneeKey = "handoff.assignee";
    public const string TakenAtKey = "handoff.taken_at";
    /// <summary>When a person gave the conversation back (kept after release so the assistant knows).</summary>
    public const string ReturnedAtKey = "handoff.returned_at";
    /// <summary>The last thing the person told the customer, so the assistant can pick up from there.</summary>
    public const string LastAdvisorMessageKey = "handoff.last_advisor_message";

    /// <summary>How long the assistant is told that a person handled the conversation.</summary>
    private static readonly TimeSpan ReturnNoteWindow = TimeSpan.FromDays(7);

    /// <summary>The assistant asked for a person; nobody took it yet.</summary>
    public const string Pending = "pending";
    /// <summary>A person took the conversation.</summary>
    public const string WithPerson = "human";

    public static string? State(ChannelSession? session) =>
        session?.Metadata.GetValueOrDefault(StateKey) is { Length: > 0 } state ? state : null;

    /// <summary>True while a person owns (or is about to own) the conversation.</summary>
    public static bool AssistantPaused(ChannelSession? session) =>
        State(session) is Pending or WithPerson;

    public static void Request(ChannelSession session, string reason, string requestedBy, DateTimeOffset at)
    {
        if (State(session) == WithPerson) return; // already with a person
        session.Metadata.Remove(ReturnedAtKey);
        session.Metadata[StateKey] = Pending;
        session.Metadata[ReasonKey] = reason;
        session.Metadata[RequestedAtKey] = at.ToString("O");
        session.Metadata[RequestedByKey] = requestedBy;
    }

    public static void Take(ChannelSession session, string userId, DateTimeOffset at)
    {
        if (State(session) is null)
        {
            session.Metadata[ReasonKey] = "Tomada por un asesor";
            session.Metadata[RequestedAtKey] = at.ToString("O");
        }
        session.Metadata.Remove(ReturnedAtKey);
        session.Metadata[StateKey] = WithPerson;
        session.Metadata[AssigneeKey] = userId;
        session.Metadata[TakenAtKey] = at.ToString("O");
    }

    /// <summary>Gives the conversation back to the assistant.</summary>
    public static void Release(ChannelSession session, DateTimeOffset? at = null)
    {
        foreach (var key in new[] { StateKey, ReasonKey, RequestedAtKey, RequestedByKey, AssigneeKey, TakenAtKey })
            session.Metadata.Remove(key);
        session.Metadata[ReturnedAtKey] = (at ?? DateTimeOffset.UtcNow).ToString("O");
        // The assistant's thread ends with "I passed you to an advisor" and misses what the person
        // said; replaying it made the assistant hand off again. It starts over, with AssistantNote.
        session.UnlinkThread();
    }

    /// <summary>
    /// What the assistant should know when a person handled the conversation and gave it back:
    /// its own thread still ends with "I passed you to an advisor" and it never saw what the person said.
    /// </summary>
    public static string? AssistantNote(ChannelSession? session, DateTimeOffset now)
    {
        if (session is null || AssistantPaused(session)) return null;
        if (!DateTimeOffset.TryParse(session.Metadata.GetValueOrDefault(ReturnedAtKey), out var returnedAt) ||
            now - returnedAt > ReturnNoteWindow)
            return null;

        var last = session.Metadata.GetValueOrDefault(LastAdvisorMessageKey);
        return "Un asesor ya atendio esta conversacion y te la devolvio: ahora la atiendes tu. " +
               (string.IsNullOrWhiteSpace(last) ? string.Empty : $"Lo ultimo que el asesor le escribio al cliente: \"{last}\". ") +
               "Responde normalmente su ultimo mensaje; vuelve a pasar el caso solo si en ese mensaje pide otra vez un asesor.";
    }
}
