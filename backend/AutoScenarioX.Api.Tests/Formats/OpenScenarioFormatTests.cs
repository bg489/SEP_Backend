using AutoScenarioX.Api.Common.Exceptions;
using AutoScenarioX.Api.Services.Formats;
using AutoScenarioX.Api.Tests.Support;

namespace AutoScenarioX.Api.Tests.Formats;

public class OpenScenarioFormatTests
{
    public const string Minimal = """
        <OpenSCENARIO>
          <FileHeader revMajor="1" revMinor="2" description="CARLA: Static route import"/>
          <ParameterDeclarations><ParameterDeclaration name="speed" parameterType="double" value="5"/></ParameterDeclarations>
          <RoadNetwork><LogicFile filepath="Town10HD_Opt"/></RoadNetwork>
          <Entities><ScenarioObject name="ego"><Vehicle name="vehicle.tesla.model3"/></ScenarioObject></Entities>
          <Storyboard>
            <Init><Actions><Private entityRef="ego">
              <PrivateAction><TeleportAction><Position><WorldPosition x="1" y="2" z="0.5" h="1.5707963267948966"/></Position></TeleportAction></PrivateAction>
              <PrivateAction><LongitudinalAction><SpeedAction><SpeedActionDynamics dynamicsShape="step" dynamicsDimension="time" value="0"/><SpeedActionTarget><AbsoluteTargetSpeed value="$speed"/></SpeedActionTarget></SpeedAction></LongitudinalAction></PrivateAction>
              <PrivateAction><RoutingAction><AssignRouteAction><Route name="route" closed="false">
                <Waypoint routeStrategy="shortest"><Position><WorldPosition x="1" y="2"/></Position></Waypoint>
                <Waypoint routeStrategy="shortest"><Position><WorldPosition x="15" y="2"/></Position></Waypoint>
                <Waypoint routeStrategy="shortest"><Position><WorldPosition x="30" y="2"/></Position></Waypoint>
              </Route></AssignRouteAction></RoutingAction></PrivateAction>
            </Private></Actions></Init>
            <StopTrigger><ConditionGroup><Condition name="timeout" delay="0" conditionEdge="rising"><ByValueCondition><SimulationTimeCondition value="60" rule="greaterThan"/></ByValueCondition></Condition></ConditionGroup></StopTrigger>
          </Storyboard>
        </OpenSCENARIO>
        """;

    [Fact]
    public void Import_preserves_carla_map_world_route_heading_and_speed_units()
    {
        var content = new OpenScenarioFormatAdapter().Parse(Minimal);
        Assert.Equal("Town10HD_Opt", content["map"]!["name"]!.GetValue<string>());
        Assert.Equal(90, content["route"]!["start"]!["yaw"]!.GetValue<double>(), 8);
        Assert.Equal(30, content["route"]!["goal"]!["x"]!.GetValue<double>());
        Assert.Single(content["route"]!["waypoints"]!.AsArray());
        Assert.Equal(18, content["ego_vehicle"]!["initial_speed_kmh"]!.GetValue<double>());
        Assert.Equal(60, content["execution"]!["timeout_s"]!.GetValue<double>());
        Assert.Empty(content["events"]!.AsArray());
        Assert.NotEmpty(content["source"]!["import_warnings"]!.AsArray());
    }

    [Theory]
    [InlineData("1", "0")]
    [InlineData("1", "1")]
    [InlineData("1", "2")]
    public void Supported_xml_revisions_can_be_imported(string major, string minor)
        => Assert.NotNull(new OpenScenarioFormatAdapter().Parse(Minimal.Replace("revMajor=\"1\" revMinor=\"2\"", $"revMajor=\"{major}\" revMinor=\"{minor}\"")));

    [Theory]
    [InlineData("revMajor=\"1\"", "revMajor=\"2\"")]
    [InlineData("CARLA: Static route import", "Other coordinate system")]
    [InlineData("Town10HD_Opt", "Town99")]
    [InlineData("value=\"$speed\"", "value=\"$missing\"")]
    [InlineData("</Storyboard>", "<Story name=\"dynamic\"/></Storyboard>")]
    [InlineData("<Vehicle name=\"vehicle.tesla.model3\"/>", "<CatalogReference catalogName=\"missing\" entryName=\"car\"/>")]
    [InlineData("closed=\"false\"", "closed=\"true\"")]
    [InlineData("dynamicsShape=\"step\"", "dynamicsShape=\"linear\"")]
    public void Unsupported_content_is_rejected_instead_of_losing_behavior(string from, string to)
        => Assert.Throws<ScenarioFormatException>(() => new OpenScenarioFormatAdapter().Parse(Minimal.Replace(from, to)));

    [Fact]
    public void Xml_external_entities_are_prohibited()
        => Assert.Throws<ScenarioFormatException>(() => new OpenScenarioFormatAdapter().Parse("<!DOCTYPE OpenSCENARIO [<!ENTITY file SYSTEM 'file:///C:/Windows/win.ini'>]><OpenSCENARIO>&file;</OpenSCENARIO>"));

    [Fact]
    public void Registry_advertises_only_registered_adapters_and_detects_both_extensions()
    {
        var registry = new ScenarioFormatRegistry([new JsonScenarioFormatAdapter(), new OpenScenarioFormatAdapter()]);
        Assert.Contains("openscenario", registry.SupportedFormats);
        Assert.DoesNotContain("yaml", registry.SupportedFormats);
        Assert.IsType<OpenScenarioFormatAdapter>(registry.ByFileName("scenario.XOSC"));
        Assert.IsType<OpenScenarioFormatAdapter>(registry.ByFileName("scenario.osc"));
    }

    [Fact]
    public void Xml_export_is_rejected_explicitly_instead_of_losing_canonical_events()
        => Assert.Throws<BusinessRuleException>(() => new OpenScenarioFormatAdapter().Serialize(new()));

    [Fact]
    public async Task Demo_xml_import_passes_existing_schema_business_and_map_validation()
    {
        var adapter = new OpenScenarioFormatAdapter();
        var content = adapter.Parse(File.ReadAllText(TestData.ExamplePath("review2_static_route.xosc")));
        var result = await TestData.ValidateAsync(content);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.Message)));
    }
}
