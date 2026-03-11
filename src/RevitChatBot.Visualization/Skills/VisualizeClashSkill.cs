using RevitChatBot.Core.Skills;

namespace RevitChatBot.Visualization.Skills;

/// <summary>
/// Specialized visualization skill for clash detection results.
/// Takes pairs of clashing element IDs and renders:
/// - Both elements in red
/// - The overlap zone in bright red (critical)
///
/// Designed to be chained after clash detection skills:
///   detect_clashes → visualize_clashes (auto-highlight all pairs)
/// </summary>
[Skill("visualize_clashes",
    "Visualize clash detection results in the 3D view. " +
    "Highlights clashing element pairs in red and shows overlap zones. " +
    "Pass pairs of element IDs from clash detection results. " +
    "Format: 'id1:id2,id3:id4' where each pair is separated by comma.")]
[SkillParameter("clash_pairs", "string",
    "Colon-separated pairs of clashing element IDs, comma-separated. " +
    "Format: 'elementA_id:elementB_id,elementC_id:elementD_id' (e.g., '123:456,789:012')",
    isRequired: true)]
[SkillParameter("clear_previous", "boolean",
    "Whether to clear previous clash visualizations. Default: true.",
    isRequired: false)]
public class VisualizeClashSkill : ISkill
{
    private readonly VisualizationManager _vizManager;

    public VisualizeClashSkill(VisualizationManager vizManager)
    {
        _vizManager = vizManager;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var pairsStr = parameters.GetValueOrDefault("clash_pairs")?.ToString();
        if (string.IsNullOrWhiteSpace(pairsStr))
            return SkillResult.Fail("Parameter 'clash_pairs' is required.");

        var clearPrevious = parameters.GetValueOrDefault("clear_previous")?.ToString() != "false";

        if (!_vizManager.IsRegistered)
            return SkillResult.Fail("Visualization not available.");

        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var result = await context.RevitApiInvoker(docObj =>
        {
            var doc = (Autodesk.Revit.DB.Document)docObj;
            const string tag = "clash_viz";
            if (clearPrevious)
                _vizManager.ClearByTag(tag);

            var pairs = ParseClashPairs(pairsStr);
            int visualized = 0;
            var errors = new List<string>();

            foreach (var (idA, idB) in pairs)
            {
                var elemA = doc.GetElement(new Autodesk.Revit.DB.ElementId(idA));
                var elemB = doc.GetElement(new Autodesk.Revit.DB.ElementId(idB));

                if (elemA is null || elemB is null)
                {
                    errors.Add($"{idA}:{idB} (element not found)");
                    continue;
                }

                _vizManager.VisualizeClash(elemA, elemB, tag);
                visualized++;
            }

            _vizManager.RefreshViews();

            return new { visualized, total = pairs.Count, errors };
        });

        if (result is null)
            return SkillResult.Fail("Failed to visualize clashes.");

        dynamic r = result;
        int count = r.visualized;
        int total = r.total;

        return SkillResult.Ok(
            $"Visualized {count}/{total} clash pairs in 3D view. " +
            "Clashing elements shown in red, overlap zones in bright red.",
            new { visualized = count, total });
    }

    private static List<(long IdA, long IdB)> ParseClashPairs(string pairsStr)
    {
        var result = new List<(long, long)>();

        foreach (var pair in pairsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[0].Trim(), out var idA)
                && long.TryParse(parts[1].Trim(), out var idB))
            {
                result.Add((idA, idB));
            }
        }

        return result;
    }
}
