using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentFlow.Abstractions;

namespace AgentFlow.Infrastructure.Providers;

public sealed class TwilioWhatsAppProviderAdapter : IMessageSendProviderAdapter
{
    public string ProviderId => "twilio";

    public IReadOnlyList<ProviderCapabilityDescriptor> Capabilities =>
    [
        new() { Name = CommunicationCapabilities.TextSend, Channel = "whatsapp", Description = "Send WhatsApp free-form text using Twilio." },
        new() { Name = CommunicationCapabilities.TemplateSend, Channel = "whatsapp", Description = "Send WhatsApp template-like content using Twilio." }
    ];

    public async Task<ProviderMessageSendResult> SendMessageAsync(
        ProviderConnectionProfile connection,
        ProviderMessageSendRequest request,
        CancellationToken ct = default)
    {
        var accountSid = GetValue(connection, "accountSid", "account")
            ?? throw new InvalidOperationException("Twilio WhatsApp provider requires accountSid.");
        var authToken = GetSecret(connection, "authToken", "token", "secret")
            ?? throw new InvalidOperationException("Twilio WhatsApp provider requires authToken secret.");
        var fromPhone = GetValue(connection, "fromPhoneNumber", "senderPhoneNumber", "from")
            ?? throw new InvalidOperationException("Twilio WhatsApp provider requires fromPhoneNumber.");

        using var httpClient = new HttpClient();
        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json");

        var form = new Dictionary<string, string>
        {
            ["To"] = $"whatsapp:{request.Recipient}",
            ["From"] = $"whatsapp:{fromPhone}",
            ["Body"] = request.Content
        };

        if (!string.IsNullOrWhiteSpace(request.StatusCallbackUrl))
            form["StatusCallback"] = request.StatusCallbackUrl!;

        requestMessage.Content = new FormUrlEncodedContent(form);
        requestMessage.Headers.Authorization = BuildBasicAuth(accountSid, authToken);

        var response = await httpClient.SendAsync(requestMessage, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twilio WhatsApp send failed with {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return new ProviderMessageSendResult
        {
            ProviderMessageId = doc.RootElement.GetProperty("sid").GetString() ?? Guid.NewGuid().ToString("N"),
            ProviderStatus = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "queued" : "queued",
            RawResponse = body
        };
    }

    private static AuthenticationHeaderValue BuildBasicAuth(string accountSid, string authToken) =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}")));

    private static string? GetValue(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Config.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? GetSecret(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Secret.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}

public sealed class TwilioVoiceProviderAdapter : IVoiceCallProviderAdapter, IVoiceCallControlProviderAdapter
{
    public string ProviderId => "twilio";

    public IReadOnlyList<ProviderCapabilityDescriptor> Capabilities =>
    [
        new() { Name = CommunicationCapabilities.CallOutbound, Channel = "voice", Description = "Place outbound voice calls using Twilio." },
        new() { Name = CommunicationCapabilities.CallOutbound, Channel = "callcenter", Description = "Place outbound call-center voice calls using Twilio." },
        new() { Name = CommunicationCapabilities.CallControl, Channel = "voice", Description = "Track and control Twilio voice lifecycle." },
        new() { Name = CommunicationCapabilities.CallControl, Channel = "callcenter", Description = "Track and control Twilio call-center voice lifecycle." }
    ];

    public async Task<ProviderVoiceCallResult> PlaceCallAsync(
        ProviderConnectionProfile connection,
        ProviderVoiceCallRequest request,
        CancellationToken ct = default)
    {
        var accountSid = GetValue(connection, "accountSid", "account")
            ?? throw new InvalidOperationException("Twilio voice provider requires accountSid.");
        var authToken = GetSecret(connection, "authToken", "token", "secret")
            ?? throw new InvalidOperationException("Twilio voice provider requires authToken secret.");
        var fromPhone = GetValue(connection, "fromPhoneNumber", "senderPhoneNumber", "from")
            ?? throw new InvalidOperationException("Twilio voice provider requires fromPhoneNumber.");
        var statusCallback = request.StatusCallbackUrl
            ?? GetValue(connection, "statusCallbackUrl", "statusCallbackURI");

        var twiml = request.Script.Contains("<Say", StringComparison.OrdinalIgnoreCase)
            ? request.Script
            : $"<Response><Say language='es-MX' loop='1' voice='Polly.Mia'>{System.Security.SecurityElement.Escape(CleanForAudio(request.Script))}</Say></Response>";

        var form = new Dictionary<string, string>
        {
            ["To"] = request.PhoneNumber,
            ["From"] = fromPhone,
            ["Twiml"] = twiml
        };

        if (!string.IsNullOrWhiteSpace(statusCallback))
        {
            form["StatusCallback"] = statusCallback!;
            form["StatusCallbackEvent"] = "initiated ringing answered completed busy no-answer canceled";
        }

        using var httpClient = new HttpClient();
        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Calls.json")
        {
            Content = new FormUrlEncodedContent(form)
        };

        requestMessage.Headers.Authorization = BuildBasicAuth(accountSid, authToken);
        var response = await httpClient.SendAsync(requestMessage, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twilio voice call failed with {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return new ProviderVoiceCallResult
        {
            ProviderCallId = doc.RootElement.GetProperty("sid").GetString() ?? Guid.NewGuid().ToString("N"),
            ProviderStatus = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "queued" : "queued",
            RawResponse = body
        };
    }

    public async Task<ProviderVoiceCallControlResult> UpdateCallAsync(
        ProviderConnectionProfile connection,
        ProviderVoiceCallControlRequest request,
        CancellationToken ct = default)
    {
        var accountSid = GetValue(connection, "accountSid", "account")
            ?? throw new InvalidOperationException("Twilio voice provider requires accountSid.");
        var authToken = GetSecret(connection, "authToken", "token", "secret")
            ?? throw new InvalidOperationException("Twilio voice provider requires authToken secret.");

        using var httpClient = new HttpClient();
        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Calls/{request.CallId}.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Twiml"] = request.Twiml
            })
        };

        requestMessage.Headers.Authorization = BuildBasicAuth(accountSid, authToken);
        var response = await httpClient.SendAsync(requestMessage, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twilio voice call update failed with {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return new ProviderVoiceCallControlResult
        {
            ProviderCallId = doc.RootElement.GetProperty("sid").GetString() ?? request.CallId,
            ProviderStatus = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "updated" : "updated",
            RawResponse = body
        };
    }

    private static AuthenticationHeaderValue BuildBasicAuth(string accountSid, string authToken) =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}")));

    private static string CleanForAudio(string text)
    {
        var firstLine = text.Split('\n')[0];
        return System.Text.RegularExpressions.Regex.Replace(firstLine, @"\d{3,}", match => string.Join(" ", match.Value.ToCharArray()));
    }

    private static string? GetValue(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Config.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? GetSecret(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Secret.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}

public sealed class MetaWhatsAppProviderAdapter : IMessageSendProviderAdapter
{
    public string ProviderId => "meta";

    public IReadOnlyList<ProviderCapabilityDescriptor> Capabilities =>
    [
        new() { Name = CommunicationCapabilities.TextSend, Channel = "whatsapp", Description = "Send WhatsApp free-form text using Meta Cloud API." },
        new() { Name = CommunicationCapabilities.TemplateSend, Channel = "whatsapp", Description = "Send WhatsApp templates using Meta Cloud API." }
    ];

    public async Task<ProviderMessageSendResult> SendMessageAsync(
        ProviderConnectionProfile connection,
        ProviderMessageSendRequest request,
        CancellationToken ct = default)
    {
        var apiToken = GetValue(connection, "apiToken", "ApiToken")
            ?? GetSecret(connection, "apiToken", "token", "secret")
            ?? throw new InvalidOperationException("Meta WhatsApp provider requires apiToken.");
        var phoneNumberId = GetValue(connection, "phoneNumberId", "PhoneNumberId")
            ?? throw new InvalidOperationException("Meta WhatsApp provider requires phoneNumberId.");
        var baseUrl = GetValue(connection, "baseUrl") ?? "https://graph.facebook.com/v20.0";

        object payload = string.IsNullOrWhiteSpace(request.TemplateName)
            ? new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = request.Recipient,
                type = "text",
                text = new { body = request.Content }
            }
            : new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = request.Recipient,
                type = "template",
                template = new
                {
                    name = request.TemplateName,
                    language = new { code = "es" }
                }
            };

        using var httpClient = new HttpClient();
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/{phoneNumberId}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var response = await httpClient.SendAsync(requestMessage, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta WhatsApp send failed with {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var messageId = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
        return new ProviderMessageSendResult
        {
            ProviderMessageId = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId,
            ProviderStatus = "accepted",
            RawResponse = body
        };
    }

    private static string? GetValue(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Config.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? GetSecret(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Secret.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}

public sealed class OpenAiTranscriptionProviderAdapter : IAudioTranscriptionProviderAdapter
{
    public string ProviderId => "openai";

    public IReadOnlyList<ProviderCapabilityDescriptor> Capabilities =>
    [
        new() { Name = CommunicationCapabilities.AudioTranscribe, Channel = "voice", Description = "Transcribe call audio using OpenAI speech-to-text." },
        new() { Name = CommunicationCapabilities.AudioTranscribe, Channel = "callcenter", Description = "Transcribe call-center audio using OpenAI speech-to-text." }
    ];

    public async Task<ProviderTranscriptionResult> TranscribeAsync(
        ProviderConnectionProfile connection,
        ProviderTranscriptionRequest request,
        CancellationToken ct = default)
    {
        var apiKey = GetSecret(connection, "apiKey", "openaiApiKey", "token", "secret")
            ?? GetValue(connection, "apiKey", "openaiApiKey")
            ?? throw new InvalidOperationException("OpenAI transcription provider requires apiKey.");
        var baseUrl = GetValue(connection, "baseUrl") ?? "https://api.openai.com/v1/audio/transcriptions";
        var model = request.Model
            ?? GetValue(connection, "transcriptionModel", "model")
            ?? "gpt-4o-mini-transcribe";

        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(request.AudioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(request.ContentType) ? "audio/wav" : request.ContentType);
        content.Add(audioContent, "file", "chunk.wav");
        content.Add(new StringContent(model), "model");
        if (!string.IsNullOrWhiteSpace(request.Language))
            content.Add(new StringContent(request.Language!), "language");

        using var httpClient = new HttpClient();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI transcription failed with {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var transcript = doc.RootElement.TryGetProperty("text", out var textEl)
            ? textEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(transcript))
            throw new InvalidOperationException("OpenAI transcription response missing text.");

        return new ProviderTranscriptionResult
        {
            Transcript = transcript!,
            ProviderStatus = "completed",
            RawResponse = body
        };
    }

    private static string? GetValue(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Config.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? GetSecret(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Secret.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}

public sealed class OpenAiSynthesisProviderAdapter : IAudioSynthesisProviderAdapter
{
    public string ProviderId => "openai";

    public IReadOnlyList<ProviderCapabilityDescriptor> Capabilities =>
    [
        new() { Name = CommunicationCapabilities.AudioSynthesize, Channel = "voice", Description = "Synthesize voice audio using OpenAI TTS." },
        new() { Name = CommunicationCapabilities.AudioSynthesize, Channel = "callcenter", Description = "Synthesize call-center audio using OpenAI TTS." }
    ];

    public async Task<ProviderSynthesisResult> SynthesizeAsync(
        ProviderConnectionProfile connection,
        ProviderSynthesisRequest request,
        CancellationToken ct = default)
    {
        var apiKey = GetSecret(connection, "apiKey", "openaiApiKey", "token", "secret")
            ?? GetValue(connection, "apiKey", "openaiApiKey")
            ?? throw new InvalidOperationException("OpenAI synthesis provider requires apiKey.");
        var baseUrl = GetValue(connection, "ttsBaseUrl") ?? "https://api.openai.com/v1/audio/speech";
        var model = request.Model
            ?? GetValue(connection, "ttsModel", "model")
            ?? "gpt-4o-mini-tts";
        var voice = request.Voice
            ?? GetValue(connection, "ttsVoice", "voice")
            ?? "alloy";
        var format = request.OutputFormat ?? "wav";

        var payload = new
        {
            model,
            voice,
            input = request.Text,
            response_format = format
        };

        using var httpClient = new HttpClient();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await httpClient.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"OpenAI synthesis failed with {(int)response.StatusCode}: {body}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType
            ?? (format.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? "audio/mpeg" : "audio/wav");

        return new ProviderSynthesisResult
        {
            AudioBytes = bytes,
            ContentType = contentType,
            ProviderStatus = "completed"
        };
    }

    private static string? GetValue(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Config.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? GetSecret(ProviderConnectionProfile connection, string key, params string[] aliases)
    {
        if (connection.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Secret.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
