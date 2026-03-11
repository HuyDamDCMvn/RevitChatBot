using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("isolate_elements",
    "Isolate or hide elements in the active Revit view using Temporary View Properties. " +
    "Isolate mode: only the specified elements are visible, everything else is hidden. " +
    "Hide mode: the specified elements are hidden, everything else remains visible. " +
    "This is non-destructive and can be reset with reset_isolation.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to isolate or hide (e.g., '123456,789012')",
    isRequired: true)]
[SkillParameter("mode", "string",
    "Action mode: 'isolate' (show only these) or 'hide' (hide these). Default: isolate.",
    isRequired: false, allowedValues: new[] { "isolate", "hide" })]
public class IsolateElementsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        if (string.IsNullOrWhiteSpace(idsStr))
            return SkillResult.Fail("Parameter 'element_ids' is required.");

        var mode = parameters.GetValueOrDefault("mode")?.ToString()?.ToLowerInvariant() ?? "isolate";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var view = document.ActiveView;
            if (view is null) return new { affected = 0, notFound = new List<string>(), mode };

            var ids = ParseElementIds(idsStr);
            var validIds = new List<ElementId>();
            var notFound = new List<string>();

            foreach (var id in ids)
            {
                var elemId = new ElementId(id);
                if (document.GetElement(elemId) is not null)
                    validIds.Add(elemId);
                else
                    notFound.Add(id.ToString());
            }

            if (validIds.Count == 0)
                return new { affected = 0, notFound, mode };

            using var tx = new Transaction(document, mode == "hide" ? "Hide elements" : "Isolate elements");
            tx.Start();

            if (mode == "hide")
            {
                view.HideElements(validIds);
            }
            else
            {
                var targetSet = new HashSet<ElementId>(validIds);
                using var collector = new FilteredElementCollector(document, view.Id);
                var allVisible = collector
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Where(id => !targetSet.Contains(id))
                    .ToList();
                if (allVisible.Count > 0)
                    view.HideElements(allVisible);
            }

            tx.Commit();
            return new { affected = validIds.Count, notFound, mode };
        });

        dynamic res = result!;
        int count = res.affected;
        string appliedMode = res.mode;
        List<string> missing = res.notFound;

        if (count == 0)
            return SkillResult.Fail("No valid elements found to " + appliedMode + ".");

        var msg = appliedMode == "hide"
            ? $"Hidden {count} element(s) in the active view."
            : $"Isolated {count} element(s) — only these are visible in the active view.";

        if (missing.Count > 0)
            msg += $" Not found: {string.Join(", ", missing.Take(5))}" +
                   (missing.Count > 5 ? $" +{missing.Count - 5} more" : "");

        return SkillResult.Ok(msg, new { affected = count, mode = appliedMode, notFound = missing });
    }

    private static List<long> ParseElementIds(string idsStr)
    {
        return idsStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s.Trim(), out var id) ? id : -1)
            .Where(id => id > 0)
            .ToList();
    }
}
