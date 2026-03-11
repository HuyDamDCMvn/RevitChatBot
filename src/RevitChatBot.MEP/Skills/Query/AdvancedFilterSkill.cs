using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("advanced_filter",
    "Multi-criteria element filter inspired by DiRoots OneFilter. " +
    "Combines category, level, system, parameter value, and room/space filters " +
    "in a single query. Returns matching elements with option to select them in Revit.")]
[SkillParameter("category", "string",
    "Category: 'ducts', 'pipes', 'equipment', 'fittings', 'air_terminals', 'electrical', " +
    "'plumbing', 'cable_trays', 'conduits', 'sprinklers'.",
    isRequired: true)]
[SkillParameter("level", "string",
    "Level name to filter (e.g. 'Level 1'). Supports partial match.",
    isRequired: false)]
[SkillParameter("system_name", "string",
    "System name filter (e.g. 'Supply Air', 'Chilled Water'). Partial match.",
    isRequired: false)]
[SkillParameter("parameter_name", "string",
    "Parameter name to filter by (e.g. 'Size', 'Diameter', 'Mark').",
    isRequired: false)]
[SkillParameter("parameter_operator", "string",
    "Comparison: 'equals', 'contains', 'greater_than', 'less_than', 'not_equals'.",
    isRequired: false,
    allowedValues: new[] { "equals", "contains", "greater_than", "less_than", "not_equals" })]
[SkillParameter("parameter_value", "string",
    "Value to compare against. Required when parameter_name is specified.",
    isRequired: false)]
[SkillParameter("room_name", "string",
    "Filter by room/space name containing this text.",
    isRequired: false)]
[SkillParameter("select_in_revit", "string",
    "'true' to select matching elements in Revit's active view. Default 'false'.",
    isRequired: false)]
[SkillParameter("max_results", "integer",
    "Max elements to return details for. Default 50.",
    isRequired: false)]
public class AdvancedFilterSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = BuiltInCategory.OST_DuctCurves,
        ["pipes"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["fittings"] = BuiltInCategory.OST_DuctFitting,
        ["pipe_fittings"] = BuiltInCategory.OST_PipeFitting,
        ["air_terminals"] = BuiltInCategory.OST_DuctTerminal,
        ["electrical"] = BuiltInCategory.OST_ElectricalEquipment,
        ["plumbing"] = BuiltInCategory.OST_PlumbingFixtures,
        ["cable_trays"] = BuiltInCategory.OST_CableTray,
        ["conduits"] = BuiltInCategory.OST_Conduit,
        ["sprinklers"] = BuiltInCategory.OST_Sprinklers,
        ["flex_ducts"] = BuiltInCategory.OST_FlexDuctCurves,
        ["flex_pipes"] = BuiltInCategory.OST_FlexPipeCurves,
        ["duct_accessories"] = BuiltInCategory.OST_DuctAccessory,
        ["pipe_accessories"] = BuiltInCategory.OST_PipeAccessory,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "";
        if (!CategoryMap.TryGetValue(categoryStr.Trim(), out var bic))
            return SkillResult.Fail($"Unknown category '{categoryStr}'. Supported: {string.Join(", ", CategoryMap.Keys)}");

        var level = parameters.GetValueOrDefault("level")?.ToString();
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var paramName = parameters.GetValueOrDefault("parameter_name")?.ToString();
        var paramOp = parameters.GetValueOrDefault("parameter_operator")?.ToString() ?? "equals";
        var paramValue = parameters.GetValueOrDefault("parameter_value")?.ToString();
        var roomName = parameters.GetValueOrDefault("room_name")?.ToString();
        var selectInRevit = parameters.GetValueOrDefault("select_in_revit")?.ToString()?.ToLower() == "true";
        var maxResults = 50;
        if (parameters.TryGetValue("max_results", out var mr) && mr is not null)
            int.TryParse(mr.ToString(), out maxResults);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementService = new RevitElementService();

            var elements = new FilteredElementCollector(document)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToList();

            var totalBeforeFilter = elements.Count;

            if (!string.IsNullOrWhiteSpace(level))
                elements = FilterByLevel(document, elements, level);

            if (!string.IsNullOrWhiteSpace(systemName))
                elements = FilterBySystem(elements, systemName);

            if (!string.IsNullOrWhiteSpace(paramName) && !string.IsNullOrWhiteSpace(paramValue))
                elements = FilterByParameter(elements, paramName, paramOp, paramValue);

            if (!string.IsNullOrWhiteSpace(roomName))
                elements = FilterByRoom(document, elements, roomName);

            var matchedIds = elements.Select(e => e.Id).ToList();

            var selectedInView = false;
            if (selectInRevit && matchedIds.Count > 0)
            {
                try
                {
                    var view = document.ActiveView;
                    if (view is not null)
                    {
                        using var tx = new Transaction(document, "Isolate filtered elements");
                        tx.Start();
                        view.IsolateElementsTemporary(matchedIds);
                        tx.Commit();
                        selectedInView = true;
                    }
                }
                catch { }
            }

            var summaries = elements.Take(maxResults).Select(e =>
            {
                var ps = elementService.GetElementParameters(e);
                return new
                {
                    id = e.Id.Value,
                    name = e.Name,
                    type = document.GetElement(e.GetTypeId())?.Name ?? "",
                    level = GetElementLevel(document, e),
                    system = ps.GetValueOrDefault("System Name",
                             ps.GetValueOrDefault("System Type", "")),
                    size = ps.GetValueOrDefault("Size",
                           ps.GetValueOrDefault("Diameter", "")),
                };
            }).ToList();

            return new
            {
                totalInCategory = totalBeforeFilter,
                matchedCount = elements.Count,
                returnedCount = summaries.Count,
                selectedInRevit = selectedInView,
                filters = new
                {
                    category = categoryStr,
                    level,
                    systemName,
                    parameter = paramName is not null ? $"{paramName} {paramOp} {paramValue}" : null,
                    roomName
                },
                elements = summaries
            };
        });

        dynamic res = result!;
        return SkillResult.Ok(
            $"Found {res.matchedCount} elements matching filters (from {res.totalInCategory} total in category).",
            result);
    }

    private static List<Element> FilterByLevel(Document doc, List<Element> elements, string levelName)
    {
        var levelIds = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Where(l => l.Name.Contains(levelName, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Id.Value)
            .ToHashSet();

        return elements.Where(e =>
        {
            if (e.LevelId is { } lid && lid != ElementId.InvalidElementId)
                return levelIds.Contains(lid.Value);
            var refLevel = e.LookupParameter("Reference Level") ?? e.LookupParameter("Level");
            var val = refLevel?.AsValueString() ?? "";
            return val.Contains(levelName, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    private static List<Element> FilterBySystem(List<Element> elements, string systemName)
    {
        return elements.Where(e =>
        {
            var sysParam = e.LookupParameter("System Name")
                           ?? e.LookupParameter("System Type")
                           ?? e.LookupParameter("System Classification");
            var val = sysParam?.AsString() ?? sysParam?.AsValueString() ?? "";
            return val.Contains(systemName, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    private static List<Element> FilterByParameter(
        List<Element> elements, string paramName, string op, string value)
    {
        return elements.Where(e =>
        {
            var param = e.LookupParameter(paramName);
            if (param is null || !param.HasValue) return false;

            var actual = param.AsValueString() ?? param.AsString() ?? param.AsDouble().ToString("F2");

            return op switch
            {
                "equals" => actual.Equals(value, StringComparison.OrdinalIgnoreCase),
                "contains" => actual.Contains(value, StringComparison.OrdinalIgnoreCase),
                "not_equals" => !actual.Equals(value, StringComparison.OrdinalIgnoreCase),
                "greater_than" => double.TryParse(ExtractNumber(actual), out var a)
                                  && double.TryParse(ExtractNumber(value), out var b) && a > b,
                "less_than" => double.TryParse(ExtractNumber(actual), out var a2)
                               && double.TryParse(ExtractNumber(value), out var b2) && a2 < b2,
                _ => actual.Contains(value, StringComparison.OrdinalIgnoreCase)
            };
        }).ToList();
    }

    private static List<Element> FilterByRoom(Document doc, List<Element> elements, string roomName)
    {
        var rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Autodesk.Revit.DB.Architecture.Room>()
            .Where(r => r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                        ?.Contains(roomName, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (rooms.Count == 0)
        {
            var spaces = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Mechanical.Space>()
                .Where(s => s.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                            ?.Contains(roomName, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            return FilterByBoundingBoxOverlap(doc, elements,
                spaces.Select(s => s.get_BoundingBox(null)).Where(bb => bb is not null).ToList()!);
        }

        return FilterByBoundingBoxOverlap(doc, elements,
            rooms.Select(r => r.get_BoundingBox(null)).Where(bb => bb is not null).ToList()!);
    }

    private static List<Element> FilterByBoundingBoxOverlap(
        Document doc, List<Element> elements, List<BoundingBoxXYZ> boxes)
    {
        if (boxes.Count == 0) return elements;

        return elements.Where(e =>
        {
            var ebb = e.get_BoundingBox(null);
            if (ebb is null) return false;

            return boxes.Any(box =>
                ebb.Min.X <= box.Max.X && ebb.Max.X >= box.Min.X &&
                ebb.Min.Y <= box.Max.Y && ebb.Max.Y >= box.Min.Y &&
                ebb.Min.Z <= box.Max.Z && ebb.Max.Z >= box.Min.Z);
        }).ToList();
    }

    private static string GetElementLevel(Document doc, Element e)
    {
        if (e.LevelId is { } lid && lid != ElementId.InvalidElementId)
            return doc.GetElement(lid)?.Name ?? "";
        return e.LookupParameter("Reference Level")?.AsValueString()
               ?? e.LookupParameter("Level")?.AsValueString()
               ?? "";
    }

    private static string ExtractNumber(string s)
    {
        var num = "";
        foreach (var c in s)
        {
            if (char.IsDigit(c) || c == '.' || c == '-') num += c;
            else if (num.Length > 0) break;
        }
        return num;
    }
}
