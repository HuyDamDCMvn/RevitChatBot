using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Validates insulation thickness against design criteria and recommends
/// required thickness based on pipe/duct size and system type.
/// References TCVN and ASHRAE 90.1 insulation tables.
/// </summary>
[Skill("insulation_thickness_calc",
    "Validate and recommend insulation thickness for ducts and pipes. " +
    "Cross-references actual insulation against TCVN/ASHRAE 90.1 requirements " +
    "based on pipe DN, system type (CHW, HW, SA), and climate zone.")]
[SkillParameter("category", "string",
    "'duct', 'pipe', or 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "all" })]
[SkillParameter("level", "string", "Filter by level name (optional)", isRequired: false)]
[SkillParameter("system_name", "string", "Filter by system name (optional)", isRequired: false)]
public class InsulationThicknessSkill : CalculationSkillBase
{
    protected override string SkillName => "insulation_thickness_calc";

    // Minimum insulation thickness per DN range for CHW systems (mm)
    private static readonly (double maxDn, double minThickMm)[] PipeInsulationTable =
    {
        (25, 25), (50, 30), (100, 40), (200, 50), (double.MaxValue, 60)
    };

    private const double DuctInsulationMinMm = 25;
    private const double DuctInsulationOutdoorMm = 50;

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var category = GetParamString(parameters, context, "category", "all");
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var issues = new List<object>();
            int totalChecked = 0;

            if (category is "pipe" or "all")
            {
                var pipes = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (!string.IsNullOrWhiteSpace(levelFilter))
                    pipes = pipes.Where(e => GetLevelName(document, e)
                        .Contains(levelFilter!, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!string.IsNullOrWhiteSpace(systemName))
                    pipes = pipes.Where(e =>
                        (e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "")
                        .Contains(systemName!, StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var pipe in pipes)
                {
                    var sysClassification = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
                    if (!RequiresInsulation(sysClassification)) continue;

                    totalChecked++;
                    var diaFt = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0;
                    var diaMm = diaFt * 304.8;
                    var requiredMm = GetRequiredPipeInsulationMm(diaMm);

                    var insTypeId = pipe.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_TYPE)?.AsElementId();
                    var hasIns = insTypeId is not null && insTypeId != ElementId.InvalidElementId;
                    var insThickFt = pipe.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS)?.AsDouble() ?? 0;
                    var insThickMm = insThickFt * 304.8;

                    if (!hasIns || insThickMm < requiredMm * 0.9)
                    {
                        issues.Add(new
                        {
                            id = pipe.Id.Value,
                            type = "Pipe",
                            sysName = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned",
                            sizeMm = Math.Round(diaMm, 0),
                            actualThickMm = Math.Round(insThickMm, 1),
                            requiredThickMm = requiredMm,
                            hasInsulation = hasIns,
                            status = !hasIns ? "NO_INSULATION" : "UNDER_INSULATED"
                        });
                    }
                }
            }

            if (category is "duct" or "all")
            {
                var ducts = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (!string.IsNullOrWhiteSpace(levelFilter))
                    ducts = ducts.Where(e => GetLevelName(document, e)
                        .Contains(levelFilter!, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!string.IsNullOrWhiteSpace(systemName))
                    ducts = ducts.Where(e =>
                        (e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "")
                        .Contains(systemName!, StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var duct in ducts)
                {
                    var sysClassification = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
                    if (!sysClassification.Contains("Supply", StringComparison.OrdinalIgnoreCase)) continue;

                    totalChecked++;
                    var requiredMm = DuctInsulationMinMm;

                    var insTypeId = duct.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_TYPE)?.AsElementId();
                    var hasIns = insTypeId is not null && insTypeId != ElementId.InvalidElementId;
                    var insThickFt = duct.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS)?.AsDouble() ?? 0;
                    var insThickMm = insThickFt * 304.8;

                    if (!hasIns || insThickMm < requiredMm * 0.9)
                    {
                        var sizeName = duct.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                        issues.Add(new
                        {
                            id = duct.Id.Value,
                            type = "Duct",
                            sysName = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned",
                            sizeMm = sizeName,
                            actualThickMm = Math.Round(insThickMm, 1),
                            requiredThickMm = requiredMm,
                            hasInsulation = hasIns,
                            status = !hasIns ? "NO_INSULATION" : "UNDER_INSULATED"
                        });
                    }
                }
            }

            return new
            {
                totalChecked,
                issueCount = issues.Count,
                noInsulationCount = issues.Count(i => ((dynamic)i!).status == "NO_INSULATION"),
                underInsulatedCount = issues.Count(i => ((dynamic)i!).status == "UNDER_INSULATED"),
                referenceStandard = "TCVN / ASHRAE 90.1",
                issues = issues.Take(30).ToList()
            };
        });

        var totalChecked = (int)((dynamic)result!).totalChecked;
        var issueCount = (int)((dynamic)result!).issueCount;
        var calcSummary = new CalcResultSummary { TotalItems = totalChecked, IssueCount = issueCount };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Insulation thickness validation completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (issueCount > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "energy_analysis",
                Reason = $"{issueCount} insulation issue(s) — review energy impact"
            });

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalChecked, Math.Min(issueCount, 30), "elements");
    }

    private static bool RequiresInsulation(string sysClassification)
    {
        return sysClassification.Contains("Supply", StringComparison.OrdinalIgnoreCase) ||
               sysClassification.Contains("Return Hydronic", StringComparison.OrdinalIgnoreCase) ||
               sysClassification.Contains("Domestic Hot", StringComparison.OrdinalIgnoreCase) ||
               sysClassification.Contains("Domestic Cold", StringComparison.OrdinalIgnoreCase);
    }

    private static double GetRequiredPipeInsulationMm(double diaMm)
    {
        foreach (var (maxDn, minThick) in PipeInsulationTable)
        {
            if (diaMm <= maxDn) return minThick;
        }
        return 60;
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }
}
