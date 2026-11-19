using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Json;

namespace AutoScenarioX.Api.Services.Hashing;

/// <summary>
/// Tính content_hash theo quy tắc cố định của dự án (xem CLAUDE.md):
/// SHA-256 của JSON đã chuẩn hoá, CHỈ gồm các khối ảnh hưởng tới thực thi.
/// metadata / source / extensions KHÔNG được tính -> đổi mô tả hay tag không sinh version mới.
/// </summary>
public sealed class CanonicalJsonHasher : ICanonicalJsonHasher
{
    private static readonly string[] HashedBlocks =
    [
        "map", "route", "weather", "ego_vehicle", "traffic", "actors", "events", "parameters", "execution"
    ];

    public string ComputeHash(JsonObject normalizedContent)
    {
        var subset = new JsonObject();
        foreach (var block in HashedBlocks)
        {
            if (normalizedContent.TryGetPropertyValue(block, out var value) && value is not null)
                subset[block] = value.DeepClone();
        }

        var canonical = CanonicalJsonSerializer.Serialize(subset);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }
}
