using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("selection_set",
    "Manage named selection sets of elements. Save the current Revit selection, " +
    "recall a saved set, list all sets, or clear them. Useful for multi-step workflows " +
    "where you need to refer back to a group of elements.")]
[SkillParameter("action", "string",
    "'save' to store current selection or given IDs, 'recall' to restore a set, " +
    "'list' to show all saved sets, 'delete' to remove a set, 'clear_all' to remove all sets.",
    isRequired: true,
    allowedValues: new[] { "save", "recall", "list", "delete", "clear_all" })]
[SkillParameter("name", "string",
    "Name for the selection set. Required for save/recall/delete.",
    isRequired: false)]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to save. If omitted during 'save', uses current Revit selection.",
    isRequired: false)]
public class SelectionSetSkill : ISkill
{
    private static readonly Dictionary<string, HashSet<long>> SavedSets = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "list";
        var name = parameters.GetValueOrDefault("name")?.ToString();
        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();

        switch (action)
        {
            case "save":
                return await SaveSet(context, name, idsStr);
            case "recall":
                return await RecallSet(context, name);
            case "list":
                return ListSets();
            case "delete":
                return DeleteSet(name);
            case "clear_all":
                SavedSets.Clear();
                return SkillResult.Ok("All selection sets cleared.");
            default:
                return SkillResult.Fail($"Unknown action '{action}'.");
        }
    }

    private async Task<SkillResult> SaveSet(SkillContext context, string? name, string? idsStr)
    {
        if (string.IsNullOrWhiteSpace(name))
            return SkillResult.Fail("Parameter 'name' is required for save.");

        HashSet<long> ids;

        if (string.IsNullOrWhiteSpace(idsStr))
        {
            var selIds = context.GetCurrentSelectionIds();
            if (selIds is null || selIds.Count == 0)
                return SkillResult.Fail("No element_ids provided and no elements currently selected in Revit.");
            ids = selIds.ToHashSet();
        }
        else
        {
            ids = idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => long.TryParse(s, out _))
                .Select(long.Parse)
                .ToHashSet();
        }

        if (ids.Count == 0)
            return SkillResult.Fail("No valid element IDs resolved.");

        SavedSets[name] = ids;
        return SkillResult.Ok($"Saved selection set '{name}' with {ids.Count} elements.", new
        {
            name,
            count = ids.Count,
            elementIds = ids.Take(20).ToList()
        });
    }

    private async Task<SkillResult> RecallSet(SkillContext context, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return SkillResult.Fail("Parameter 'name' is required for recall.");

        if (!SavedSets.TryGetValue(name, out var ids))
            return SkillResult.Fail($"Selection set '{name}' not found. Use action='list' to see available sets.");

        if (context.RevitApiInvoker is null)
            return SkillResult.Ok($"Selection set '{name}' has {ids.Count} elements.",
                new { name, count = ids.Count, elementIds = ids.Take(50).ToList() });

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var validIds = ids
                .Select(id => new ElementId(id))
                .Where(eid => document.GetElement(eid) is not null)
                .ToList();

            var isolated = false;
            if (validIds.Count > 0)
            {
                try
                {
                    var view = document.ActiveView;
                    if (view is not null)
                    {
                        using var tx = new Transaction(document, "Recall selection set");
                        tx.Start();
                        view.IsolateElementsTemporary(validIds);
                        tx.Commit();
                        isolated = true;
                    }
                }
                catch { }
            }

            return new
            {
                found = validIds.Count,
                missing = ids.Count - validIds.Count,
                isolated,
                elementIds = validIds.Select(id => id.Value).Take(50).ToList()
            };
        });

        dynamic r = result!;
        var msg = $"Recalled '{name}': {r.found} elements found ({r.missing} no longer exist).";
        if ((bool)r.isolated) msg += " Elements isolated in active view.";

        return SkillResult.Ok(msg, new
        {
            name,
            found = (int)r.found,
            missing = (int)r.missing,
            isolated = (bool)r.isolated,
            totalStored = ids.Count
        });
    }

    private static SkillResult ListSets()
    {
        if (SavedSets.Count == 0)
            return SkillResult.Ok("No selection sets saved yet.");

        var sets = SavedSets.Select(kv => new
        {
            name = kv.Key,
            count = kv.Value.Count,
            sampleIds = kv.Value.Take(5).ToList()
        }).ToList();

        return SkillResult.Ok($"{SavedSets.Count} selection sets available.", new { sets });
    }

    private static SkillResult DeleteSet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return SkillResult.Fail("Parameter 'name' is required for delete.");

        if (SavedSets.Remove(name))
            return SkillResult.Ok($"Deleted selection set '{name}'.");

        return SkillResult.Fail($"Selection set '{name}' not found.");
    }
}
