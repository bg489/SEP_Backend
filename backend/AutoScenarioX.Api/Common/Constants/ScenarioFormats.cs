namespace AutoScenarioX.Api.Common.Constants;

/// <summary>
/// Định dạng file import/export.
/// PHẢI khớp CHECK constraint của asx.import_records.format
/// trong database/migrations/001_flow1_scenario_management.sql.
/// </summary>
public static class ScenarioFormats
{
    public const string Json = "json";
    public const string Yaml = "yaml";

    /// <summary>Chưa hỗ trợ - sẽ làm khi có adapter CARLA Route XML.</summary>
    public const string CarlaRouteXml = "carla_route_xml";

    /// <summary>Chưa hỗ trợ - sẽ làm khi có adapter OpenSCENARIO.</summary>
    public const string OpenScenario = "openscenario";

    /// <summary>Các định dạng đã có adapter chạy được.</summary>
    public static readonly IReadOnlyList<string> Supported = [Json, Yaml];

    /// <summary>Các định dạng có trong DB nhưng chưa có adapter.</summary>
    public static readonly IReadOnlyList<string> Planned = [CarlaRouteXml, OpenScenario];
}

/// <summary>
/// Trạng thái một lần import.
/// PHẢI khớp CHECK constraint của asx.import_records.status.
/// </summary>
public static class ImportStatuses
{
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
}
