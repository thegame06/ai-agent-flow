using System.Security.Cryptography;
using System.Text;

namespace AgentFlow.Intents.Classification;

/// <summary>
/// Deterministic fallback embedding generator.
/// Produces stable vectors without external API dependencies.
/// </summary>
public sealed class HashEmbeddingGenerator : IEmbeddingGenerator
{
    public int Dimension => 1536;

    public string ModelName => "local-hash-embedding-fallback";

    public Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<IReadOnlyList<float>>(new float[Dimension]);
        }

        var vector = new float[Dimension];
        var tokens = text
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            for (var i = 0; i < hash.Length; i++)
            {
                var idx = ((hash[i] << 2) + i) % Dimension;
                vector[idx] += (hash[i] / 255f) - 0.5f;
            }
        }

        // L2 normalize for consistent cosine behavior.
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0f)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return Task.FromResult<IReadOnlyList<float>>(vector);
    }
}
