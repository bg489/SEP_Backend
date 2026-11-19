using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoScenarioX.Api.Common.Exceptions;

namespace AutoScenarioX.Api.Services.Tags;

/// <summary>
/// Quy tắc tên tag, khớp CHECK của asx.tags.name và pattern metadata.tags trong JSON Schema:
/// chữ thường/số, '_' và '-', tối đa 32 ký tự; tối đa 20 tag mỗi scenario (maxItems của schema).
/// </summary>
public static partial class TagNames
{
    public const int MaxPerScenario = 20;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,31}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string name) => Pattern().IsMatch(name);

    /// <summary>Chuẩn hoá: trim + chữ thường. Không kiểm tra hợp lệ.</summary>
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    /// <summary>
    /// Bộ tag người dùng gửi lên: chuẩn hoá, bỏ trùng (giữ thứ tự xuất hiện đầu), báo lỗi theo vị trí.
    /// </summary>
    public static IReadOnlyList<string> FromRequest(IReadOnlyList<string?>? tags, string field = "tags")
    {
        if (tags is null)
            throw Invalid(field, "Provide a list of tags (use an empty list to remove all tags).");

        var errors = new Dictionary<string, string[]>();
        var result = new List<string>();
        for (var i = 0; i < tags.Count; i++)
        {
            var name = Normalize(tags[i] ?? string.Empty);
            if (!IsValid(name))
                errors[$"{field}[{i}]"] = ["Tags use 1–32 lowercase letters, digits, '-' or '_', starting with a letter or digit."];
            else if (!result.Contains(name))
                result.Add(name);
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
        if (result.Count > MaxPerScenario)
            throw Invalid(field, $"A scenario can have at most {MaxPerScenario} tags.");
        return result;
    }

    /// <summary>
    /// Tag lấy từ metadata.tags của file import: bỏ qua giá trị sai thay vì chặn import
    /// (lỗi đã được validator báo ở tầng schema), bỏ trùng, giữ tối đa 20.
    /// </summary>
    public static IReadOnlyList<string> FromContent(JsonNode? metadataTags)
    {
        if (metadataTags is not JsonArray array) return [];
        return array
            .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? Normalize(s) : null)
            .OfType<string>()
            .Where(IsValid)
            .Distinct()
            .Take(MaxPerScenario)
            .ToList();
    }

    private static RequestValidationException Invalid(string field, string message)
        => new(new Dictionary<string, string[]> { [field] = [message] });
}
