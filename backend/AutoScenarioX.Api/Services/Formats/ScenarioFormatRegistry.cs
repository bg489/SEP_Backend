using AutoScenarioX.Api.Common.Constants;

namespace AutoScenarioX.Api.Services.Formats;

/// <summary>Tra adapter theo mã định dạng hoặc theo tên file.</summary>
public interface IScenarioFormatRegistry
{
    IReadOnlyList<string> SupportedFormats { get; }

    /// <summary>Adapter của định dạng này, null nếu chưa hỗ trợ.</summary>
    IScenarioFormatAdapter? ByFormat(string? format);

    /// <summary>Adapter suy từ đuôi tên file, null nếu đuôi lạ.</summary>
    IScenarioFormatAdapter? ByFileName(string? fileName);
}

public sealed class ScenarioFormatRegistry : IScenarioFormatRegistry
{
    private readonly Dictionary<string, IScenarioFormatAdapter> _byFormat;
    private readonly Dictionary<string, IScenarioFormatAdapter> _byExtension;

    public ScenarioFormatRegistry(IEnumerable<IScenarioFormatAdapter> adapters)
    {
        var list = adapters.ToList();
        _byFormat = list.ToDictionary(a => a.Format, StringComparer.OrdinalIgnoreCase);
        _byExtension = new Dictionary<string, IScenarioFormatAdapter>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in list)
            foreach (var extension in adapter.Extensions)
                _byExtension[extension] = adapter;
    }

    public IReadOnlyList<string> SupportedFormats => _byFormat.Keys.Order(StringComparer.Ordinal).ToArray();

    public IScenarioFormatAdapter? ByFormat(string? format)
        => string.IsNullOrWhiteSpace(format) ? null
            : _byFormat.GetValueOrDefault(format.Trim());

    public IScenarioFormatAdapter? ByFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var extension = Path.GetExtension(fileName.Trim());
        return string.IsNullOrEmpty(extension) ? null : _byExtension.GetValueOrDefault(extension);
    }
}
