using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Services.Variants;

/// <summary>
/// Kiểm tra grid trước khi cấp phát các tổ hợp; thứ tự tên tham số là ordinal.
/// Strategy "cartesian" sinh mọi tổ hợp; "pairwise" sinh bộ phủ mọi cặp giá trị (xem <see cref="Pairwise"/>).
/// Ngoài tên tham số, grid nhận trục <see cref="WeatherAxis"/> với các preset thời tiết (như Euro NCAP thử ngày/đêm).
/// </summary>
public static class ParameterGridExpander
{
    /// <summary>Trục thời tiết: có dấu chấm nên không trùng được tên tham số (^[a-z][a-z0-9_]*$).</summary>
    public const string WeatherAxis = "weather.preset";

    public static IReadOnlyList<JsonObject> Expand(
        Dictionary<string, JsonElement>? grid, JsonObject parameters, int maxCount,
        string strategy = VariantStrategies.Cartesian)
    {
        if (!VariantStrategies.All.Contains(strategy))
            throw Invalid("strategy", $"Use one of: {string.Join(", ", VariantStrategies.All)}.");
        var pairwise = strategy == VariantStrategies.Pairwise;
        if (grid is null || grid.Count == 0)
            throw Invalid("parameterGrid", "Provide at least one parameter with a list of values.");

        var axes = new List<(string Name, JsonElement[] Values)>();
        var count = 1;
        foreach (var (name, axis) in grid.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var field = $"parameterGrid.{name}";
            var weather = name == WeatherAxis;
            if (!weather && parameters[name] is not JsonObject)
                throw Invalid(field, $"Parameter '{name}' is not defined in the source version.");
            if (axis.ValueKind != JsonValueKind.Array || axis.GetArrayLength() == 0)
                throw Invalid(field, "Provide a non-empty array of values.");
            // Pairwise: số tổ hợp không nhỏ hơn số giá trị của trục dài nhất.
            if (axis.GetArrayLength() > (pairwise ? maxCount : maxCount / count))
                throw Invalid("parameterGrid", LimitMessage(maxCount));
            if (weather)
            {
                var presets = axis.EnumerateArray().ToArray();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < presets.Length; i++)
                {
                    if (presets[i].ValueKind != JsonValueKind.String || !ScenarioCatalog.WeatherPresets.Contains(presets[i].GetString()!))
                        throw Invalid($"{field}[{i}]", "The value must be a weather preset from the catalog.");
                    if (!seen.Add(presets[i].GetString()!))
                        throw Invalid(field, "Values must be unique.");
                }
                if (!pairwise) count *= presets.Length;
                axes.Add((name, presets));
                continue;
            }
            var definition = (JsonObject)parameters[name]!;

            var min = JsonSerializer.SerializeToElement(definition["min"]);
            var max = JsonSerializer.SerializeToElement(definition["max"]);
            if (!min.TryGetDecimal(out var lower) || !max.TryGetDecimal(out var upper))
                throw Invalid(field, "The parameter range exceeds the supported decimal range.");

            var values = axis.EnumerateArray().ToArray();
            var unique = new HashSet<decimal>();
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
                    throw Invalid($"{field}[{i}]", "The value must be a number within the decimal range.");
                if (number < lower || number > upper)
                    throw Invalid($"{field}[{i}]", $"The value must be within [{lower}, {upper}].");
                if (definition["type"]?.GetValue<string>() == "integer" && decimal.Truncate(number) != number)
                    throw Invalid($"{field}[{i}]", "Integer parameters require whole-number values.");
                if (!unique.Add(number))
                    throw Invalid(field, "Values must be unique (including equivalent numbers such as 1 and 1.0).");
            }
            if (!pairwise) count *= values.Length;
            axes.Add((name, values));
        }

        var sizes = axes.Select(a => a.Values.Length).ToArray();
        // Pairwise cần ít nhất tích của hai trục dài nhất; chặn trước khi sinh.
        if (pairwise && sizes.OrderDescending().Take(2).Aggregate(1, (a, b) => a * b) > maxCount)
            throw Invalid("parameterGrid", LimitMessage(maxCount));
        var rows = pairwise ? Pairwise(sizes) : Cartesian(sizes);
        if (rows.Count > maxCount)
            throw Invalid("parameterGrid", LimitMessage(maxCount));

        return rows.Select(row =>
        {
            var item = new JsonObject();
            for (var p = 0; p < axes.Count; p++)
                item[axes[p].Name] = JsonNode.Parse(axes[p].Values[row[p]].GetRawText());
            return item;
        }).ToList();
    }

    /// <summary>Mọi tổ hợp chỉ số; trục đầu đổi chậm nhất.</summary>
    private static List<int[]> Cartesian(int[] sizes)
    {
        var rows = new List<int[]> { new int[sizes.Length] };
        for (var p = 0; p < sizes.Length; p++)
            rows = rows.SelectMany(row => Enumerable.Range(0, sizes[p]).Select(v =>
            {
                var next = (int[])row.Clone();
                next[p] = v;
                return next;
            })).ToList();
        return rows;
    }

    /// <summary>
    /// IPOG (Lei &amp; Tai) cho t = 2, tất định: cùng grid luôn ra cùng tập và cùng thứ tự.
    /// Xếp trục theo số giá trị giảm dần (hoà thì giữ thứ tự ordinal), khởi tạo bằng tích của
    /// hai trục đầu; mỗi trục sau: mở rộng ngang (mỗi dòng chọn giá trị phủ nhiều cặp mới nhất,
    /// hoà chọn chỉ số nhỏ nhất) rồi mở rộng dọc (điền ô trống hoặc thêm dòng cho cặp còn thiếu).
    /// Ô còn trống cuối cùng nhận giá trị đầu tiên của trục.
    /// </summary>
    private static List<int[]> Pairwise(int[] sizes)
    {
        var n = sizes.Length;
        if (n <= 2) return Cartesian(sizes);

        var order = Enumerable.Range(0, n).OrderByDescending(p => sizes[p]).ThenBy(p => p).ToArray();
        const int free = -1;
        var rows = new List<int[]>();
        for (var a = 0; a < sizes[order[0]]; a++)
            for (var b = 0; b < sizes[order[1]]; b++)
            {
                var row = Enumerable.Repeat(free, n).ToArray();
                row[order[0]] = a;
                row[order[1]] = b;
                rows.Add(row);
            }

        for (var k = 2; k < n; k++)
        {
            var current = order[k];
            var earlier = order[..k];
            // uncovered[j][vj, vk]: cặp (trục earlier[j] = vj, trục current = vk) chưa được phủ.
            var uncovered = earlier.Select(p => new bool[sizes[p], sizes[current]]).ToArray();
            for (var j = 0; j < k; j++)
                for (var vj = 0; vj < sizes[earlier[j]]; vj++)
                    for (var vk = 0; vk < sizes[current]; vk++)
                        uncovered[j][vj, vk] = true;

            foreach (var row in rows)
            {
                var best = 0;
                var bestGain = -1;
                for (var vk = 0; vk < sizes[current]; vk++)
                {
                    var gain = 0;
                    for (var j = 0; j < k; j++)
                        if (row[earlier[j]] != free && uncovered[j][row[earlier[j]], vk]) gain++;
                    if (gain > bestGain) (best, bestGain) = (vk, gain);
                }
                row[current] = best;
                for (var j = 0; j < k; j++)
                    if (row[earlier[j]] != free) uncovered[j][row[earlier[j]], best] = false;
            }

            for (var j = 0; j < k; j++)
                for (var vj = 0; vj < sizes[earlier[j]]; vj++)
                    for (var vk = 0; vk < sizes[current]; vk++)
                    {
                        if (!uncovered[j][vj, vk]) continue;
                        var target = rows.FirstOrDefault(r => r[current] == vk && r[earlier[j]] == free);
                        if (target is null)
                        {
                            target = Enumerable.Repeat(free, n).ToArray();
                            target[current] = vk;
                            rows.Add(target);
                        }
                        target[earlier[j]] = vj;
                        // Ô vừa điền có thể phủ thêm cặp với các trục khác đã có giá trị.
                        for (var i = 0; i < k; i++)
                            if (target[earlier[i]] != free) uncovered[i][target[earlier[i]], vk] = false;
                    }
        }

        foreach (var row in rows)
            for (var p = 0; p < n; p++)
                if (row[p] == free) row[p] = 0;
        return rows;
    }

    private static string LimitMessage(int maxCount)
        => $"The number of combinations exceeds the limit of {maxCount} variants per batch.";

    private static RequestValidationException Invalid(string field, string message)
        => new(new Dictionary<string, string[]> { [field] = [message] });
}
