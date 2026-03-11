using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

[Skill("mep_calculation",
    "Perform MEP engineering calculations: duct sizing, pipe sizing, " +
    "airflow summary, and system pressure analysis.")]
[SkillParameter("calculation_type", "string",
    "Type of calculation to perform",
    isRequired: true,
    allowedValues: new[] { "duct_summary", "pipe_summary", "system_airflow", "system_pressure" })]
[SkillParameter("system_name", "string",
    "Optional system name to filter",
    isRequired: false)]
public class MEPCalculationSkill : CalculationSkillBase
{
    protected override string SkillName => "mep_calculation";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var calcType = GetParamString(parameters, context, "calculation_type", "duct_summary");
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            return calcType switch
            {
                "duct_summary" => CalculateDuctSummary(document, systemName),
                "pipe_summary" => CalculatePipeSummary(document, systemName),
                "system_airflow" => CalculateSystemAirflow(document, systemName),
                _ => (object)new { error = "Unknown calculation type" }
            };
        });

        var itemCount = ((dynamic)result!).GetType().GetProperty("totalDucts") is not null
            ? (int)((dynamic)result!).totalDucts
            : ((dynamic)result!).GetType().GetProperty("totalPipes") is not null
                ? (int)((dynamic)result!).totalPipes
                : ((dynamic)result!).GetType().GetProperty("systemCount") is not null
                    ? (int)((dynamic)result!).systemCount
                    : 0;

        var summary = new CalcResultSummary { TotalItems = itemCount, IssueCount = 0 };
        var delta = ComputeDelta(context, summary);
        SaveResultForDelta(context, summary);

        var msg = $"Calculation '{calcType}' completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (calcType == "duct_summary")
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "duct_sizing_analysis",
                Reason = "Check velocity compliance for these ducts"
            });
        else if (calcType == "pipe_summary")
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "pipe_sizing_analysis",
                Reason = "Check velocity compliance for these pipes"
            });
        else if (calcType == "system_airflow")
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "calculate_pressure_drop",
                Reason = "Calculate pressure drop for these systems"
            });

        msg = AppendFollowUps(msg, followUps);
        return SkillResult.Ok(msg, result);
    }

    private static object CalculateDuctSummary(Document doc, string? systemName)
    {
        var ducts = new FilteredElementCollector(doc)
            .OfClass(typeof(Duct))
            .WhereElementIsNotElementType()
            .Cast<Duct>()
            .ToList();

        if (systemName is not null)
            ducts = ducts.Where(d =>
                d.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        var totalLength = ducts.Sum(d =>
            d.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0);

        var sizeGroups = ducts
            .GroupBy(d => d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "Unknown")
            .Select(g => new { Size = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        return new
        {
            totalDucts = ducts.Count,
            totalLengthFeet = Math.Round(totalLength, 2),
            totalLengthMeters = Math.Round(totalLength * 0.3048, 2),
            sizeDistribution = sizeGroups
        };
    }

    private static object CalculatePipeSummary(Document doc, string? systemName)
    {
        var pipes = new FilteredElementCollector(doc)
            .OfClass(typeof(Pipe))
            .WhereElementIsNotElementType()
            .Cast<Pipe>()
            .ToList();

        if (systemName is not null)
            pipes = pipes.Where(p =>
                p.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        var totalLength = pipes.Sum(p =>
            p.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0);

        var sizeGroups = pipes
            .GroupBy(p => p.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "Unknown")
            .Select(g => new { Size = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        return new
        {
            totalPipes = pipes.Count,
            totalLengthFeet = Math.Round(totalLength, 2),
            totalLengthMeters = Math.Round(totalLength * 0.3048, 2),
            sizeDistribution = sizeGroups
        };
    }

    private static object CalculateSystemAirflow(Document doc, string? systemName)
    {
        var systems = new FilteredElementCollector(doc)
            .OfClass(typeof(MechanicalSystem))
            .Cast<MechanicalSystem>()
            .ToList();

        if (systemName is not null)
            systems = systems.Where(s =>
                s.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        var summaries = systems.Select(s => new
        {
            Name = s.Name,
            SystemType = s.SystemType.ToString(),
            Flow = s.GetFlow(),
            ElementCount = s.DuctNetwork?.Size ?? 0
        }).ToList();

        return new
        {
            systemCount = summaries.Count,
            systems = summaries
        };
    }
}
