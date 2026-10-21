using System.Xml;
using System.Xml.Linq;

namespace AutoScenarioX.AuthApi.Services;

public static class OpenScenarioSource
{
    public static string? Validate(string source, bool requireCriteria = false)
    {
        try
        {
            using var input = new StringReader(source);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader);
            if (document.Root?.Name.LocalName != "OpenSCENARIO" ||
                !document.Descendants().Any(element => element.Name.LocalName == "Storyboard"))
                return "The file must be OpenSCENARIO XML containing a Storyboard.";
            var entities = document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "Entities");
            if (entities is null || !entities.Elements().Any(element => element.Name.LocalName == "ScenarioObject"))
                return "The OpenSCENARIO source must define actual ScenarioObject actors in Entities. Placeholder XML with only an actor count cannot be executed.";
            var storyboard = document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "Storyboard");
            if (storyboard is null || !storyboard.Elements().Any(element => element.Name.LocalName == "Init"))
                return "The OpenSCENARIO source must define a Storyboard Init section for actor setup.";
            if (requireCriteria && !storyboard.Descendants().Any(element => element.Name.LocalName == "Condition" &&
                ((string?)element.Attribute("name"))?.StartsWith("criteria_", StringComparison.Ordinal) == true))
                return "The OpenSCENARIO source must include ScenarioRunner criteria_ conditions before a safety verdict can be evaluated.";
            return null;
        }
        catch (XmlException)
        {
            return "The selected scenario file is not valid XML.";
        }
    }
}
