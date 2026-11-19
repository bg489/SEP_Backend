using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;

namespace AutoScenarioX.Api.Services.Formats;

/// <summary>
/// Explicit OpenSCENARIO XML 1.0-1.2 import subset: inline vehicles/pedestrians,
/// Init teleport/speed, ego AssignRouteAction world vertices, simulation-time stop.
/// Catalogs and dynamic Stories require a full ScenarioRunner adapter and are rejected,
/// rather than silently dropping behavior and generating a false passing execution.
/// </summary>
public sealed class OpenScenarioFormatAdapter : IScenarioFormatAdapter
{
    public string Format => ScenarioFormats.OpenScenario;
    public IReadOnlyList<string> Extensions => [".xosc", ".osc"];
    public string DefaultExtension => ".xosc";
    public string MediaType => "application/xml";

    public JsonObject Parse(string text)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException error) { throw new ScenarioFormatException($"Invalid OpenSCENARIO XML: {error.Message}"); }
        var root = document.Root;
        if (root?.Name.LocalName != "OpenSCENARIO") throw Unsupported("The root must be OpenSCENARIO.");
        if (root.Descendants().Any(e => e.Name.NamespaceName.Length > 0)) throw Unsupported("XML namespaces are not supported by this adapter.");
        var header = root.Element("FileHeader") ?? throw Unsupported("FileHeader is required.");
        if (!Attr(header, "description").StartsWith("CARLA:", StringComparison.OrdinalIgnoreCase))
            throw Unsupported("FileHeader.description must start with CARLA: to explicitly select CARLA coordinates. Convert other coordinate systems before importing.");
        if (Attr(header, "revMajor") != "1" || !int.TryParse(Attr(header, "revMinor"), out var minor) || minor is < 0 or > 2)
            throw Unsupported("Only OpenSCENARIO XML versions 1.0, 1.1 and 1.2 are supported.");
        if (root.Descendants("CatalogReference").Any()) throw Unsupported("CatalogReference is not supported. Inline the entity/route first; missing catalog references cannot be resolved.");
        if (root.Descendants("Story").Any()) throw Unsupported("Dynamic Story/Act/Maneuver events are not supported by this import subset. Convert those events to canonical scenario JSON.");
        if (root.Descendants("ObjectController").Any() || root.Descendants("EntitySelection").Any() || root.Descendants("UserDefinedAction").Any())
            throw Unsupported("Object controllers, entity selections and user-defined actions are not supported.");
        if (root.Descendants("RoadPosition").Any() || root.Descendants("LanePosition").Any() || root.Descendants("RelativeWorldPosition").Any())
            throw Unsupported("Only absolute WorldPosition is supported; road/lane/relative positions require a map-aware adapter.");

        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in root.Element("ParameterDeclarations")?.Elements("ParameterDeclaration") ?? [])
        {
            var name = Attr(parameter, "name");
            if (!declarations.TryAdd(name, Attr(parameter, "value"))) throw Unsupported($"Duplicate parameter '{name}'.");
        }
        string Resolve(string value) => value.StartsWith('$')
            ? declarations.GetValueOrDefault(value[1..]) ?? throw Unsupported($"Parameter '{value}' is not declared.") : value;
        double Number(XElement node, string name, double? fallback = null)
        {
            var value = (string?)node.Attribute(name);
            if (value is null && fallback.HasValue) return fallback.Value;
            if (!double.TryParse(Resolve(value ?? string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                throw Unsupported($"{node.Name.LocalName}.{name} must be a finite numeric value.");
            return number;
        }
        JsonObject Position(XElement node)
        {
            var world = node.Name.LocalName == "WorldPosition" ? node : node.Element("WorldPosition")
                ?? throw Unsupported("A supported WorldPosition is required.");
            if (Number(world, "p", 0) != 0 || Number(world, "r", 0) != 0) throw Unsupported("Pitched/rolled world poses are not supported.");
            return new JsonObject
            {
                ["x"] = Number(world, "x"), ["y"] = Number(world, "y"), ["z"] = Number(world, "z", 0.5),
                ["yaw"] = Number(world, "h", 0) * 180 / Math.PI
            };
        }

        var logic = root.Element("RoadNetwork")?.Element("LogicFile") ?? throw Unsupported("RoadNetwork/LogicFile is required.");
        var map = Path.GetFileNameWithoutExtension(Attr(logic, "filepath").Replace('\\', '/'));
        if (!System.Text.RegularExpressions.Regex.IsMatch(map, "^Town(01|02|03|04|05|10HD)(_Opt)?$"))
            throw Unsupported("LogicFile must reference one of the 12 supported CARLA Town maps.");
        var objects = root.Element("Entities")?.Elements("ScenarioObject").ToList() ?? [];
        if (objects.Count is < 1 or > 51) throw Unsupported("Provide an ego entity and at most 50 scripted actors.");
        var ids = objects.Select(e => Attr(e, "name")).ToList();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw Unsupported("ScenarioObject names must be unique.");
        var ego = objects.FirstOrDefault(e => e.Descendants("Property").Any(p => Attr(p, "name") == "role_name" && Attr(p, "value") == "hero")) ?? objects[0];
        if (ego.Element("Vehicle") is null) throw Unsupported("The ego entity must be a Vehicle.");
        var init = root.Element("Storyboard")?.Element("Init")?.Element("Actions") ?? throw Unsupported("Storyboard/Init/Actions is required.");
        if (init.Elements("GlobalAction").Any()) throw Unsupported("GlobalAction/environment changes are not supported; configure canonical weather after import.");
        if (init.Descendants("PrivateAction").Any(e => !e.Elements().Any() || e.Elements().Any(c => c.Name.LocalName is not ("TeleportAction" or "LongitudinalAction" or "RoutingAction"))))
            throw Unsupported("Init supports only TeleportAction, SpeedAction and AssignRouteAction.");
        if (init.Descendants("LongitudinalAction").Any(e => e.Element("SpeedAction") is null) || init.Descendants("RoutingAction").Any(e => e.Element("AssignRouteAction") is null))
            throw Unsupported("Only absolute SpeedAction and AssignRouteAction are supported.");
        var privateByEntity = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var item in init.Elements("Private"))
            if (!privateByEntity.TryAdd(Attr(item, "entityRef"), item)) throw Unsupported("Use one Init Private block per entity.");
        if (privateByEntity.Keys.Any(id => !ids.Contains(id))) throw Unsupported("Init references an unknown entity.");
        foreach (var entity in objects)
            if (!privateByEntity.ContainsKey(Attr(entity, "name"))) throw Unsupported($"Entity '{Attr(entity, "name")}' has no Init Private actions.");
        var egoActions = privateByEntity[Attr(ego, "name")];
        if (egoActions.Descendants("Route").Count() != 1 || egoActions.Descendants("Route").Any(r => Attr(r, "closed") == "true"))
            throw Unsupported("Provide exactly one open ego Route.");
        var vertices = egoActions.Descendants("AssignRouteAction").Descendants("Waypoint").Select(w => Position(w.Element("Position") ?? throw Unsupported("Waypoint.Position is required."))).ToList();
        if (vertices.Count < 2) throw Unsupported("The ego AssignRouteAction must have at least two WorldPosition waypoints.");
        var egoSpawn = Spawn(egoActions);
        var route = new JsonObject { ["start"] = egoSpawn, ["goal"] = vertices[^1].DeepClone(), ["goal_tolerance_m"] = 5 };
        var distance = Math.Sqrt(Math.Pow(egoSpawn["x"]!.GetValue<double>() - vertices[0]["x"]!.GetValue<double>(), 2) + Math.Pow(egoSpawn["y"]!.GetValue<double>() - vertices[0]["y"]!.GetValue<double>(), 2));
        if (distance > 5) throw Unsupported("The first route waypoint must be within 5 m of the ego teleport position.");
        if (vertices.Count > 2) route["waypoints"] = new JsonArray(vertices.Skip(1).SkipLast(1).Select(p => (JsonNode)p.DeepClone()).ToArray());
        var actors = new JsonArray();
        foreach (var entity in objects.Where(e => e != ego))
        {
            var id = Attr(entity, "name");
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9_]{0,31}$")) throw Unsupported($"Actor name '{id}' must use lowercase canonical actor ID syntax.");
            var actions = privateByEntity[id];
            if (actions.Descendants("AssignRouteAction").Any()) throw Unsupported("NPC routes are not supported by this subset.");
            actors.Add(new JsonObject
            {
                ["id"] = id, ["type"] = entity.Element("Pedestrian") is null ? "vehicle" : "pedestrian",
                ["blueprint"] = Blueprint(entity), ["spawn"] = Spawn(actions), ["behavior"] = "idle", ["initial_speed_kmh"] = Speed(actions)
            });
            if (Speed(actions) > 0) throw Unsupported("Moving NPC actors require canonical events; this subset imports stationary NPC actors only.");
        }
        var stopConditions = root.Element("Storyboard")?.Element("StopTrigger")?.Descendants("SimulationTimeCondition").ToList() ?? [];
        if (stopConditions.Count > 1) throw Unsupported("Only one simulation-time stop condition is supported.");
        if (root.Element("Storyboard")?.Element("StopTrigger")?.Descendants("Condition").Count() != stopConditions.Count)
            throw Unsupported("Only simulation-time stop conditions are supported.");
        var timeout = stopConditions.Count > 0 ? stopConditions.Min(c => Number(c, "value")) : 120;
        if (stopConditions.Any(c => Attr(c, "rule") is not ("greaterThan" or "greaterOrEqual"))) throw Unsupported("Stop time requires greaterThan or greaterOrEqual.");
        var warnings = new JsonArray("Imported the supported static route subset. Verify actor positions and weather before publishing.");
        if (declarations.Count > 0) warnings.Add("OpenSCENARIO parameters were resolved to their declared values; create canonical parameters for variants.");
        return new JsonObject
        {
            ["schema_version"] = "1.0",
            ["metadata"] = new JsonObject { ["name"] = Attr(header, "description") is { Length: >= 3 } description ? description : "Imported OpenSCENARIO", ["category"] = "other" },
            ["source"] = new JsonObject { ["type"] = "manual", ["import_warnings"] = warnings },
            ["map"] = new JsonObject { ["name"] = map }, ["route"] = route,
            ["weather"] = new JsonObject { ["preset"] = "ClearNoon" },
            ["ego_vehicle"] = new JsonObject { ["blueprint"] = Blueprint(ego), ["role_name"] = "hero", ["initial_speed_kmh"] = Speed(egoActions) },
            ["traffic"] = new JsonObject(), ["actors"] = actors, ["events"] = new JsonArray(), ["parameters"] = new JsonObject(),
            ["execution"] = new JsonObject { ["seed"] = 42, ["timeout_s"] = timeout }
        };

        JsonObject Spawn(XElement actions)
        {
            var teleports = actions.Descendants("TeleportAction").ToList();
            if (teleports.Count != 1) throw Unsupported("Every entity must have exactly one Init TeleportAction.");
            return Position(teleports[0].Element("Position") ?? throw Unsupported("TeleportAction.Position is required."));
        }
        double Speed(XElement actions)
        {
            var speedActions = actions.Descendants("SpeedAction").ToList();
            if (speedActions.Count > 1) throw Unsupported("At most one Init SpeedAction is supported per entity.");
            if (speedActions.Count == 0) return 0;
            if (speedActions[0].Element("SpeedActionDynamics") is { } dynamics && Attr(dynamics, "dynamicsShape") != "step")
                throw Unsupported("Init SpeedAction must use step dynamics.");
            var target = speedActions[0].Descendants("AbsoluteTargetSpeed").SingleOrDefault()
                ?? throw Unsupported("Relative speed targets are not supported.");
            return Number(target, "value") * 3.6;
        }
    }

    public string Serialize(JsonObject content) => throw new BusinessRuleException("EXPORT_FORMAT_UNSUPPORTED", "OpenSCENARIO is import-only. Export canonical JSON/YAML to preserve all behavior.");
    private static string Attr(XElement element, string name) => (string?)element.Attribute(name) ?? string.Empty;
    private static ScenarioFormatException Unsupported(string message) => new($"OpenSCENARIO import: {message}");
    private static string Blueprint(XElement entity)
    {
        var type = entity.Element("Vehicle") ?? entity.Element("Pedestrian") ?? throw Unsupported("Only inline Vehicle and Pedestrian entities are supported.");
        var blueprint = type.Descendants("Property").FirstOrDefault(p => Attr(p, "name") == "blueprint")?.Attribute("value")?.Value ?? Attr(type, "name");
        var prefix = type.Name.LocalName == "Pedestrian" ? "walker.pedestrian." : "vehicle.";
        if (!blueprint.StartsWith(prefix, StringComparison.Ordinal)) throw Unsupported($"Entity '{Attr(entity, "name")}' needs an explicit CARLA blueprint name or Property named blueprint.");
        return blueprint;
    }
}
