using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("reset_view_overrides",
    "Reset visual overrides in the active view. " +
    "Can clear color overrides on specific elements, unhide/unisolate all elements, " +
    "or do both. Use after override_element_color or isolate_elements to restore defaults.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to reset color overrides on. " +
    "If omitted, resets all overrides in the view.",
    isRequired: false)]
[SkillParameter("reset_color", "boolean",
    "Reset graphic (color) overrides. Default: true.",
    isRequired: false)]
[SkillParameter("reset_isolation", "boolean",
    "Disable temporary isolation/hiding, making all elements visible again. Default: true.",
    isRequired: false)]
public class ResetViewOverridesSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var resetColor = parameters.GetValueOrDefault("reset_color")?.ToString() != "false";
        var resetIso = parameters.GetValueOrDefault("reset_isolation")?.ToString() != "false";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var view = document.ActiveView;
            if (view is null) return new { colorReset = 0, isolationReset = false };

            int colorReset = 0;
            bool isolationReset = false;
            var defaultOgs = new OverrideGraphicSettings();

            using var tx = new Transaction(document, "Reset view overrides");
            tx.Start();

            if (resetColor)
            {
                if (!string.IsNullOrWhiteSpace(idsStr))
                {
                    var ids = idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var s in ids)
                    {
                        if (long.TryParse(s, out var id))
                        {
                            view.SetElementOverrides(new ElementId(id), defaultOgs);
                            colorReset++;
                        }
                    }
                }
                else
                {
                    using var collector = new FilteredElementCollector(document, view.Id);
                    var allIds = collector.WhereElementIsNotElementType().ToElementIds();
                    foreach (var eid in allIds)
                    {
                        view.SetElementOverrides(eid, defaultOgs);
                        colorReset++;
                    }
                }
            }

            if (resetIso)
            {
                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                isolationReset = true;
            }

            tx.Commit();
            return new { colorReset, isolationReset };
        });

        dynamic res = result!;
        int colorCount = res.colorReset;
        bool isoReset = res.isolationReset;

        var parts = new List<string>();
        if (resetColor) parts.Add($"Reset color overrides on {colorCount} element(s)");
        if (isoReset) parts.Add("Disabled temporary isolation/hiding");

        return SkillResult.Ok(
            string.Join(". ", parts) + ".",
            new { colorReset = colorCount, isolationReset = isoReset });
    }
}
