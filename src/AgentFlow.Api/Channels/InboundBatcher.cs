using System.Collections.Concurrent;
using System.Text.Json;
using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Api.Channels;

/// <summary>
/// Messages a customer sends within a couple of seconds (a photo and its caption arrive as two
/// webhooks on Twilio; people often type in bursts) are answered once, together. Each message joins
/// its conversation's batch; only the last one in the window goes to the assistant, carrying the text
/// of all of them. Per process: with several API instances the same customer should reach the same
/// one (or the batch should move to a shared store).
/// </summary>
public sealed class InboundBatcher
{
    private sealed class Batch
    {
        public readonly object Gate = new();
        public readonly List<ChannelMessage> Items = [];
        public int Version;
    }

    private readonly ConcurrentDictionary<string, Batch> _batches = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _turns = new();

    /// <summary>
    /// One assistant turn at a time per customer: the customer has one conversation across channels,
    /// so a message on QR and another on Twilio must not be answered at the same time on one thread.
    /// Dispose the result to let the next turn in.
    /// </summary>
    public async Task<IDisposable> TurnAsync(string customerKey)
    {
        var gate = _turns.GetOrAdd(customerKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) gate.Release(); }
    }

    /// <summary>Adds the message to its conversation's batch; returns its position (the batch version).</summary>
    public int Add(string sessionId, ChannelMessage message)
    {
        var batch = _batches.GetOrAdd(sessionId, _ => new Batch());
        lock (batch.Gate)
        {
            batch.Items.Add(message);
            return ++batch.Version;
        }
    }

    /// <summary>The whole batch when this message is still the latest in it; null when a newer one will answer.</summary>
    public IReadOnlyList<ChannelMessage>? TakeIfLatest(string sessionId, int version)
    {
        if (!_batches.TryGetValue(sessionId, out var batch)) return null;
        lock (batch.Gate)
        {
            if (batch.Version != version) return null;
            _batches.TryRemove(sessionId, out _);
            return batch.Items.ToList();
        }
    }

    /// <summary>
    /// The message the assistant reads for a batch: the last one, with every message's text (in order)
    /// and every attachment. <c>agentflow.own_content</c> keeps what the last message itself said.
    /// </summary>
    public static ChannelMessage Combine(IReadOnlyList<ChannelMessage> batch)
    {
        var last = batch[^1];
        if (batch.Count == 1) return last;
        var text = string.Join("\n", batch.Select(m => m.Content).Where(t => !string.IsNullOrWhiteSpace(t)));
        var combined = ChannelMessage.CreateIncoming(last.TenantId, last.ChannelId, last.SessionId, last.From, text, last.RawPayload, last.ExternalMessageId);
        foreach (var (key, value) in last.Metadata) combined.Metadata[key] = value;
        combined.Metadata["agentflow.own_content"] = last.Content;
        combined.Metadata["agentflow.batch_size"] = batch.Count.ToString();

        var attachments = new List<JsonElement>();
        foreach (var message in batch)
        {
            if (message.Metadata.GetValueOrDefault(Provenance.AttachmentsKey) is not { Length: > 0 } raw) continue;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    attachments.AddRange(doc.RootElement.EnumerateArray().Select(e => e.Clone()));
            }
            catch (JsonException) { }
        }
        if (attachments.Count > 0) combined.Metadata[Provenance.AttachmentsKey] = JsonSerializer.Serialize(attachments);
        else combined.Metadata.Remove(Provenance.AttachmentsKey);
        return combined;
    }
}
