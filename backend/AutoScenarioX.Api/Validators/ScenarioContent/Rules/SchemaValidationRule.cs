using System.Text.Json.Nodes;
using AutoScenarioX.Api.Services.Interfaces;
using Json.Schema;

namespace AutoScenarioX.Api.Validators.ScenarioContent.Rules;

/// <summary>
/// Tầng 1: đối chiếu với Resources/scenario.schema.json (JSON Schema draft 2020-12).
/// Bắt lỗi thiếu trường, sai kiểu, sai định dạng, ngoài miền giá trị.
/// Kiểm tra trên nội dung NGUYÊN VĂN để lỗi chỉ đúng chỗ người dùng nhập.
/// </summary>
public sealed class SchemaValidationRule(IReferenceDataService referenceData) : IScenarioValidationRule
{
    /// <summary>
    /// Các từ khoá tạo NHÁNH trong schema. Nhánh không khớp vẫn bị báo lỗi dù tài liệu đúng:
    /// vd location dùng spawn_point_index thì nhánh toạ độ {x,y} luôn "thiếu x, y".
    /// Những lỗi đó chỉ gây rối nên bị lọc bỏ.
    /// </summary>
    private static readonly string[] BranchKeywords = ["anyOf", "oneOf", "if", "not"];

    private static JsonSchema? _compiled;
    private static JsonNode? _schemaNode;

    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List
    };

    public ValidationTier Tier => ValidationTier.Schema;

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default)
    {
        var schema = _compiled ??= JsonSchema.FromText(referenceData.GetScenarioSchemaJson());
        var schemaNode = _schemaNode ??= JsonNode.Parse(referenceData.GetScenarioSchemaJson());
        var instance = JsonNode.Parse(context.Raw.GetRawText());

        var result = schema.Evaluate(instance, Options);
        if (result.IsValid) return Task.FromResult<IReadOnlyList<ValidationIssue>>([]);

        var nodes = Flatten(result).ToList();
        var failures = nodes.Where(node => !node.IsValid && node.Errors is { Count: > 0 }).ToList();
        var describer = new SchemaErrorDescriber(schemaNode!, instance, failures);

        // Ưu tiên lỗi trực tiếp; chỉ khi không có mới tóm tắt lỗi trong nhánh
        var direct = failures.Where(node => !IsInsideBranch(node)).SelectMany(node => ToIssues(node, describer)).ToList();
        if (direct.Count > 0) return Task.FromResult<IReadOnlyList<ValidationIssue>>(Distinct(direct));

        // Nhánh thua của một oneOf/if đã khớp vẫn có lỗi trong danh sách: đó là nhiễu, chỉ giữ lỗi không nằm dưới nút hợp lệ
        var valid = nodes.Where(node => node.IsValid).ToList();
        bool IsNoise(EvaluationResults node) => valid.Any(v => IsAncestor(v, node));

        var real = failures.Where(node => !IsNoise(node)).ToList();
        if (real.Count > 0)
        {
            var path = real.Select(InstancePath).OrderBy(p => p.Length).First();
            var node = real.First(n => InstancePath(n) == path);
            var error = node.Errors!.First();
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(
                [ValidationIssue.Error(Tier, path, describer.Describe(node, error.Key, error.Value, path))]);
        }

        // "not" (vd event đèn giao thông có actor_ref) không sinh thông điệp nào: tự nhận ra từ schema
        foreach (var node in nodes.Where(n => !n.IsValid && !IsNoise(n)))
            if (describer.DescribeNegation(node) is { } negation)
                return Task.FromResult<IReadOnlyList<ValidationIssue>>([ValidationIssue.Error(Tier, InstancePath(node), negation)]);

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(
            [ValidationIssue.Error(Tier, "/", "Content does not match scenario schema v1.0.")]);
    }

    private static IEnumerable<EvaluationResults> Flatten(EvaluationResults node)
    {
        yield return node;
        foreach (var child in node.Details)
            foreach (var found in Flatten(child))
                yield return found;
    }

    /// <summary>a là tổ tiên của b: đường đánh giá của a là tiền tố thật sự, vị trí trong tài liệu là tiền tố (hoặc trùng).</summary>
    private static bool IsAncestor(EvaluationResults a, EvaluationResults b)
        => IsPrefix(a.EvaluationPath.ToString(), b.EvaluationPath.ToString(), proper: true) &&
           IsPrefix(a.InstanceLocation.ToString(), b.InstanceLocation.ToString(), proper: false);

    private static bool IsPrefix(string prefix, string path, bool proper)
        => path == prefix ? !proper : path.StartsWith(prefix + "/", StringComparison.Ordinal) || prefix.Length == 0;

    private static bool IsInsideBranch(EvaluationResults node)
        => node.EvaluationPath.ToString()
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => BranchKeywords.Contains(segment, StringComparer.Ordinal));

    /// <summary>Path giữ nguyên như thư viện báo (khớp bản Python); thông điệp được viết lại cho dễ hiểu.</summary>
    private IEnumerable<ValidationIssue> ToIssues(EvaluationResults node, SchemaErrorDescriber describer)
        => node.Errors!.Select(error => ValidationIssue.Error(Tier, IssuePath(node, error.Key),
            describer.Describe(node, error.Key, error.Value, InstancePath(node))));

    /// <summary>
    /// Khoá thừa (additionalProperties: false) và tên khoá sai (propertyNames) được JsonSchema.Net báo tại chính khoá đó,
    /// còn bản Python báo tại object chứa nó: lấy object cha cho khớp. Thông điệp vẫn nêu tên khoá.
    /// </summary>
    private static string IssuePath(EvaluationResults node, string keyword)
    {
        var evaluation = node.EvaluationPath.ToString();
        var keyLevel = (keyword == "" && evaluation.EndsWith("/additionalProperties", StringComparison.Ordinal)) ||
                       evaluation.Contains("/propertyNames", StringComparison.Ordinal);
        var path = InstancePath(node);
        if (!keyLevel || path == "/") return path;
        var parent = path[..path.LastIndexOf('/')];
        return parent.Length == 0 ? "/" : parent;
    }

    private static string InstancePath(EvaluationResults node)
    {
        var path = node.InstanceLocation.ToString();
        return string.IsNullOrEmpty(path) ? "/" : path;
    }

    /// <summary>Một lỗi có thể xuất hiện nhiều lần qua $ref; chỉ giữ lại một.</summary>
    private static List<ValidationIssue> Distinct(List<ValidationIssue> issues)
        => issues.DistinctBy(i => (i.Path, i.Message)).ToList();
}
