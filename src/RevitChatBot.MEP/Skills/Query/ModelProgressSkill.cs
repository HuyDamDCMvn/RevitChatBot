using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("model_progress",
    "Track modeling progress by analyzing parameter completeness, element counts, " +
    "and system connectivity per level. Reports percentage of elements with all required " +
    "parameters filled, connected systems ratio, and overall progress estimate.")]
[SkillParameter("discipline", "string",
    "Discipline to check: 'mechanical', 'plumbing', 'electrical', 'fire_protection', or 'all'. " +
    "Default: 'all'.",
    isRequired: false,
    allowedValues: new[] { "mechanical", "plumbing", "electrical", "fire_protection", "all" })]
[SkillParameter("level", "string",
    "Filter by level name (partial match). Optional — omit for entire model.",
    isRequired: false)]
[SkillParameter("required_parameters", "string",
    "Comma-separated parameter names that must be filled to count as 'complete'. " +
    "Default: 'System Type,System Name,Size,Mark'.",
    isRequired: false)]
public class ModelProgressSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory[]> DisciplineCategories = new()
    {
        ["mechanical"] = [BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_MechanicalEquipment],
        ["plumbing"] = [BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PlumbingFixtures],
        ["electrical"] = [BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures],
        ["fire_protection"] = [BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_PipeCurves],
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var discipline = parameters.GetValueOrDefault("discipline")?.ToString()?.ToLower() ?? "all";
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var requiredParamsStr = parameters.GetValueOrDefault("required_parameters")?.ToString()
            ?? "System Type,System Name,Size,Mark";
        var requiredParams = requiredParamsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (!string.IsNullOrWhiteSpace(levelFilter))
                levels = levels.Where(l => l.Name.Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var categories = new List<BuiltInCategory>();
            if (discipline == "all")
                categories.AddRange(DisciplineCategories.Values.SelectMany(c => c).Distinct());
            else if (DisciplineCategories.TryGetValue(discipline, out var cats))
                categories.AddRange(cats);

            var progressByLevel = new List<object>();
            int totalElements = 0, totalComplete = 0, totalConnected = 0, totalWithConnectors = 0;

            foreach (var level in levels)
            {
                int levelTotal = 0, levelComplete = 0, levelConnected = 0, levelWithConnectors = 0;

                foreach (var cat in categories)
                {
                    var elements = new FilteredElementCollector(document)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .WherePasses(new ElementLevelFilter(level.Id))
                        .ToElements();

                    foreach (var elem in elements)
                    {
                        levelTotal++;
                        bool allFilled = requiredParams.All(pName =>
                        {
                            var param = elem.LookupParameter(pName);
                            return param is not null && param.HasValue && !string.IsNullOrWhiteSpace(param.AsValueString());
                        });
                        if (allFilled) levelComplete++;

                        if (elem is Autodesk.Revit.DB.MEPCurve mepCurve)
                        {
                            levelWithConnectors++;
                            var connMgr = mepCurve.ConnectorManager;
                            if (connMgr is not null)
                            {
                                var allConnected = connMgr.Connectors.Cast<Connector>().All(c => c.IsConnected);
                                if (allConnected) levelConnected++;
                            }
                        }
                    }
                }

                totalElements += levelTotal;
                totalComplete += levelComplete;
                totalConnected += levelConnected;
                totalWithConnectors += levelWithConnectors;

                if (levelTotal > 0)
                {
                    progressByLevel.Add(new
                    {
                        level = level.Name,
                        elementCount = levelTotal,
                        parameterCompletePct = Math.Round(100.0 * levelComplete / levelTotal, 1),
                        connectivityPct = levelWithConnectors > 0
                            ? Math.Round(100.0 * levelConnected / levelWithConnectors, 1)
                            : 100.0,
                    });
                }
            }

            var overallParamPct = totalElements > 0 ? Math.Round(100.0 * totalComplete / totalElements, 1) : 0;
            var overallConnPct = totalWithConnectors > 0 ? Math.Round(100.0 * totalConnected / totalWithConnectors, 1) : 0;
            var overallProgress = Math.Round((overallParamPct * 0.4 + overallConnPct * 0.6), 1);

            return new Dictionary<string, object>
            {
                ["discipline"] = discipline,
                ["totalElements"] = totalElements,
                ["parameterCompleteness"] = overallParamPct,
                ["connectivityCompleteness"] = overallConnPct,
                ["overallProgress"] = overallProgress,
                ["byLevel"] = progressByLevel,
                ["requiredParameters"] = requiredParams
            };
        });

        var data = (Dictionary<string, object>)result!;
        var summary = $"Model progress ({data["discipline"]}): " +
                      $"{data["totalElements"]} elements, " +
                      $"parameter fill: {data["parameterCompleteness"]}%, " +
                      $"connectivity: {data["connectivityCompleteness"]}%, " +
                      $"overall: {data["overallProgress"]}%.";
        return SkillResult.Ok(summary, result);
    }
}
