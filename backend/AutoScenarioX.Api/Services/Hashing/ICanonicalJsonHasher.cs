using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Services.Hashing;

public interface ICanonicalJsonHasher
{
    /// <summary>
    /// content_hash của một nội dung scenario ĐÃ chuẩn hoá.
    /// Định dạng "sha256:&lt;64 hex&gt;" (khớp CHECK trong DB).
    /// </summary>
    string ComputeHash(JsonObject normalizedContent);
}
