using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentFlow.Application.Memory;
using AgentFlow.Infrastructure.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentFlow.Tests.Integration.Memory;

public class QdrantVectorMemoryIntegrationTests
{
    [Fact]
    public async Task Store_Search_Delete_RoundTrip_Works_With_Tenant_And_Agent_Isolation()
    {
        var handler = new FakeQdrantHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://qdrant-test")
        };

        var memory = new QdrantVectorMemory(
            httpClient,
            Options.Create(new QdrantVectorMemoryOptions
            {
                Enabled = true,
                BaseUrl = "http://qdrant-test",
                VectorSize = 32,
                CollectionPrefix = "test"
            }),
            NullLogger<QdrantVectorMemory>.Instance);

        var id1 = await memory.StoreEmbeddingAsync(
            "agent-a",
            "tenant-1",
            "credito hipotecario aprobado para cliente premium",
            new Dictionary<string, string> { ["topic"] = "mortgage" });

        await memory.StoreEmbeddingAsync(
            "agent-a",
            "tenant-2",
            "tarjeta de credito rechazada por riesgo",
            new Dictionary<string, string> { ["topic"] = "card" });

        await memory.StoreEmbeddingAsync(
            "agent-b",
            "tenant-1",
            "consulta de saldo disponible",
            new Dictionary<string, string> { ["topic"] = "balance" });

        var hits = await memory.SearchAsync("agent-a", "tenant-1", "cliente premium hipotecario", topK: 5, minScore: 0.15f);

        Assert.Single(hits);
        Assert.Equal("mortgage", hits[0].Metadata["topic"]);
        Assert.Contains("hipotecario", hits[0].Content);

        await memory.DeleteAsync("agent-a", "tenant-1", id1);

        var afterDelete = await memory.SearchAsync("agent-a", "tenant-1", "cliente premium hipotecario", topK: 5, minScore: 0.10f);
        Assert.Empty(afterDelete);
    }

    private sealed class FakeQdrantHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Dictionary<string, StoredPoint>> _collections = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (request.Method == HttpMethod.Get && parts.Length == 2 && parts[0] == "collections")
            {
                return _collections.ContainsKey(parts[1])
                    ? Json(HttpStatusCode.OK, new { status = true })
                    : Json(HttpStatusCode.NotFound, new { status = false });
            }

            if (request.Method == HttpMethod.Put && parts.Length == 2 && parts[0] == "collections")
            {
                _collections.TryAdd(parts[1], new Dictionary<string, StoredPoint>());
                return Json(HttpStatusCode.OK, new { status = true });
            }

            if (request.Method == HttpMethod.Put && parts.Length == 3 && parts[0] == "collections" && parts[2] == "index")
            {
                return Json(HttpStatusCode.OK, new { status = true });
            }

            if (request.Method == HttpMethod.Post && parts.Length == 3 && parts[0] == "collections" && parts[2] == "points")
            {
                var body = await request.Content!.ReadFromJsonAsync<UpsertBody>(cancellationToken: cancellationToken);
                var collection = GetOrCreate(parts[1]);

                foreach (var point in body!.Points)
                {
                    collection[point.Id] = new StoredPoint(point.Id, point.Vector, point.Payload);
                }

                return Json(HttpStatusCode.OK, new { status = true });
            }

            if (request.Method == HttpMethod.Post && parts.Length == 4 && parts[0] == "collections" && parts[2] == "points" && parts[3] == "search")
            {
                var body = await request.Content!.ReadFromJsonAsync<SearchBody>(cancellationToken: cancellationToken);
                var collection = GetOrCreate(parts[1]);

                var tenant = body!.Filter.Must.First(m => m.Key == "tenant_id").Match.Value;
                var agent = body.Filter.Must.First(m => m.Key == "agent_id").Match.Value;

                var result = collection.Values
                    .Where(p => p.Payload.TryGetValue("tenant_id", out var t) && Equals(t?.ToString(), tenant))
                    .Where(p => p.Payload.TryGetValue("agent_id", out var a) && Equals(a?.ToString(), agent))
                    .Select(p => new
                    {
                        id = p.Id,
                        score = Cosine(body.Vector, p.Vector),
                        payload = p.Payload
                    })
                    .OrderByDescending(x => x.score)
                    .Take(body.Limit)
                    .ToList();

                return Json(HttpStatusCode.OK, new { status = true, result });
            }

            if (request.Method == HttpMethod.Post && parts.Length == 4 && parts[0] == "collections" && parts[2] == "points" && parts[3] == "delete")
            {
                var body = await request.Content!.ReadFromJsonAsync<DeleteBody>(cancellationToken: cancellationToken);
                var collection = GetOrCreate(parts[1]);

                foreach (var id in body!.Points)
                {
                    collection.Remove(id);
                }

                return Json(HttpStatusCode.OK, new { status = true });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private Dictionary<string, StoredPoint> GetOrCreate(string collection)
        {
            if (!_collections.TryGetValue(collection, out var points))
            {
                points = new Dictionary<string, StoredPoint>();
                _collections[collection] = points;
            }

            return points;
        }

        private static HttpResponseMessage Json(HttpStatusCode code, object payload)
            => new(code)
            {
                Content = JsonContent.Create(payload, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };

        private static float Cosine(float[] a, float[] b)
        {
            var dot = 0f;
            var aNorm = 0f;
            var bNorm = 0f;

            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                aNorm += a[i] * a[i];
                bNorm += b[i] * b[i];
            }

            return dot / (MathF.Sqrt(aNorm) * MathF.Sqrt(bNorm) + 1e-6f);
        }

        private sealed record StoredPoint(string Id, float[] Vector, Dictionary<string, object> Payload);
        private sealed record UpsertBody(List<UpsertPoint> Points);
        private sealed record UpsertPoint(string Id, float[] Vector, Dictionary<string, object> Payload);
        private sealed record SearchBody(float[] Vector, int Limit, SearchFilter Filter);
        private sealed record SearchFilter(List<SearchMatch> Must);
        private sealed record SearchMatch(string Key, MatchValue Match);
        private sealed record MatchValue(string Value);
        private sealed record DeleteBody(List<string> Points);
    }
}
