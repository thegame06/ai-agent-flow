using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentFlow.Application.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentFlow.Infrastructure.Memory;

public sealed class QdrantVectorMemory : IVectorMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly QdrantVectorMemoryOptions _options;
    private readonly ILogger<QdrantVectorMemory> _logger;

    public QdrantVectorMemory(
        HttpClient httpClient,
        IOptions<QdrantVectorMemoryOptions> options,
        ILogger<QdrantVectorMemory> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (_httpClient.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl, UriKind.Absolute);
        }
    }

    public async Task<string> StoreEmbeddingAsync(
        string agentId,
        string tenantId,
        string content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var collection = BuildCollectionName(agentId, tenantId);
        await EnsureCollectionExistsAsync(collection, ct);

        var embeddingId = Guid.NewGuid().ToString("N");
        var vector = DeterministicEmbeddingEncoder.Encode(content, _options.VectorSize);

        var payload = new Dictionary<string, object>
        {
            ["agent_id"] = agentId,
            ["tenant_id"] = tenantId,
            ["content"] = content,
            ["stored_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        if (metadata is not null)
        {
            foreach (var kv in metadata)
            {
                payload[$"meta_{kv.Key}"] = kv.Value;
            }
        }

        var upsertBody = new UpsertPointsRequest
        {
            Points =
            [
                new UpsertPoint
                {
                    Id = embeddingId,
                    Vector = vector,
                    Payload = payload
                }
            ]
        };

        await PostAndEnsureSuccessAsync($"/collections/{collection}/points?wait=true", upsertBody, ct);
        return embeddingId;
    }

    public async Task<IReadOnlyList<VectorMemoryResult>> SearchAsync(
        string agentId,
        string tenantId,
        string query,
        int topK = 5,
        float minScore = 0.75f,
        CancellationToken ct = default)
    {
        var collection = BuildCollectionName(agentId, tenantId);
        await EnsureCollectionExistsAsync(collection, ct);

        var searchBody = new SearchPointsRequest
        {
            Vector = DeterministicEmbeddingEncoder.Encode(query, _options.VectorSize),
            Limit = topK,
            WithPayload = true,
            Filter = new QueryFilter
            {
                Must =
                [
                    new MatchCondition("tenant_id", tenantId),
                    new MatchCondition("agent_id", agentId)
                ]
            }
        };

        var response = await PostAndReadAsync<SearchPointsResponse>($"/collections/{collection}/points/search", searchBody, ct);
        if (response.Result is null || response.Result.Count == 0)
        {
            return [];
        }

        return response.Result
            .Where(x => x.Score >= minScore)
            .Select(x => new VectorMemoryResult
            {
                EmbeddingId = x.Id,
                Content = x.Payload.TryGetValue("content", out var rawContent) ? rawContent?.ToString() ?? string.Empty : string.Empty,
                Score = x.Score,
                StoredAt = DateTimeOffset.TryParse(x.Payload.TryGetValue("stored_at", out var stored) ? stored?.ToString() : null, out var storedAt)
                    ? storedAt
                    : DateTimeOffset.UtcNow,
                Metadata = x.Payload
                    .Where(p => p.Key.StartsWith("meta_", StringComparison.Ordinal))
                    .ToDictionary(p => p.Key[5..], p => p.Value?.ToString() ?? string.Empty)
            })
            .ToList();
    }

    public async Task DeleteAsync(string agentId, string tenantId, string embeddingId, CancellationToken ct = default)
    {
        var collection = BuildCollectionName(agentId, tenantId);
        var body = new DeletePointsRequest { Points = [embeddingId] };
        await PostAndEnsureSuccessAsync($"/collections/{collection}/points/delete?wait=true", body, ct);
    }

    private async Task EnsureCollectionExistsAsync(string collectionName, CancellationToken ct)
    {
        var endpoint = $"/collections/{collectionName}";
        var result = await _httpClient.GetAsync(endpoint, ct);

        if (result.IsSuccessStatusCode)
        {
            if (_options.EnsurePayloadIndexes)
            {
                await EnsurePayloadIndexesAsync(collectionName, ct);
            }
            return;
        }

        var createBody = new CreateCollectionRequest
        {
            Vectors = new VectorConfiguration
            {
                Size = _options.VectorSize,
                Distance = _options.Distance
            }
        };

        await PutAndEnsureSuccessAsync(endpoint, createBody, ct);
        if (_options.EnsurePayloadIndexes)
        {
            await EnsurePayloadIndexesAsync(collectionName, ct);
        }
    }

    private async Task EnsurePayloadIndexesAsync(string collectionName, CancellationToken ct)
    {
        foreach (var field in _options.IndexedPayloadFields)
        {
            var body = new CreatePayloadIndexRequest { FieldName = field, FieldSchema = "keyword" };
            var response = await _httpClient.PutAsJsonAsync($"/collections/{collectionName}/index?wait=true", body, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Payload index create skipped/failed for {Field} in {Collection}: {StatusCode}", field, collectionName, response.StatusCode);
            }
        }
    }

    private string BuildCollectionName(string agentId, string tenantId)
    {
        if (!string.IsNullOrWhiteSpace(_options.CollectionPrefix))
        {
            return NormalizeName($"{_options.CollectionPrefix}_{tenantId}_{agentId}");
        }

        return NormalizeName($"{tenantId}_{agentId}");
    }

    private static string NormalizeName(string source)
    {
        var normalized = source.ToLowerInvariant().Replace(':', '_').Replace('-', '_');
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        return sb.ToString();
    }

    private async Task PostAndEnsureSuccessAsync<T>(string uri, T body, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(uri, body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task PutAndEnsureSuccessAsync<T>(string uri, T body, CancellationToken ct)
    {
        var response = await _httpClient.PutAsJsonAsync(uri, body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> PostAndReadAsync<T>(string uri, object body, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(uri, body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return model ?? throw new InvalidOperationException("Qdrant response body was empty.");
    }

    private sealed class DeterministicEmbeddingEncoder
    {
        public static float[] Encode(string text, int size)
        {
            var vector = new float[size];
            if (string.IsNullOrWhiteSpace(text))
            {
                return vector;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var slot = i % size;
                vector[slot] += (text[i] % 97) / 97f;
            }

            var norm = MathF.Sqrt(vector.Sum(v => v * v));
            if (norm > 0)
            {
                for (var i = 0; i < size; i++)
                {
                    vector[i] /= norm;
                }
            }

            return vector;
        }
    }

    private sealed record QdrantEnvelope<T>
    {
        public bool Status { get; init; }
        public T? Result { get; init; }
    }

    private sealed record CreateCollectionRequest
    {
        public VectorConfiguration Vectors { get; init; } = new();
    }

    private sealed record VectorConfiguration
    {
        public int Size { get; init; }
        public string Distance { get; init; } = "Cosine";
    }

    private sealed record UpsertPointsRequest
    {
        public required List<UpsertPoint> Points { get; init; }
    }

    private sealed record UpsertPoint
    {
        public string Id { get; init; } = string.Empty;
        public float[] Vector { get; init; } = [];
        public Dictionary<string, object> Payload { get; init; } = new();
    }

    private sealed record SearchPointsRequest
    {
        public float[] Vector { get; init; } = [];
        public int Limit { get; init; }
        [JsonPropertyName("with_payload")]
        public bool WithPayload { get; init; }
        public QueryFilter Filter { get; init; } = new();
    }

    private sealed record SearchPointsResponse
    {
        public List<ScoredPoint> Result { get; init; } = [];
    }

    private sealed record ScoredPoint
    {
        public string Id { get; init; } = string.Empty;
        public float Score { get; init; }
        public Dictionary<string, object?> Payload { get; init; } = new();
    }

    private sealed record QueryFilter
    {
        public List<MatchCondition> Must { get; init; } = [];
    }

    private sealed record MatchCondition(string Key, MatchValue Match)
    {
        public MatchCondition(string key, string value) : this(key, new MatchValue { Value = value })
        {
        }
    }

    private sealed record MatchValue
    {
        public string Value { get; init; } = string.Empty;
    }

    private sealed record DeletePointsRequest
    {
        public required List<string> Points { get; init; }
    }

    private sealed record CreatePayloadIndexRequest
    {
        [JsonPropertyName("field_name")]
        public string FieldName { get; init; } = string.Empty;

        [JsonPropertyName("field_schema")]
        public string FieldSchema { get; init; } = "keyword";
    }
}

public sealed class QdrantVectorMemoryOptions
{
    public bool Enabled { get; init; }
    public string Transport { get; init; } = "Http";
    public string BaseUrl { get; init; } = "http://localhost:6333";
    public string? GrpcEndpoint { get; init; }
    public string CollectionPrefix { get; init; } = "agentflow";
    public int VectorSize { get; init; } = 256;
    public string Distance { get; init; } = "Cosine";
    public bool EnsurePayloadIndexes { get; init; } = true;
    public string[] IndexedPayloadFields { get; init; } = ["tenant_id", "agent_id"];
}
