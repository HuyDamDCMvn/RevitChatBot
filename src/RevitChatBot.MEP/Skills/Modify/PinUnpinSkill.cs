using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("pin_unpin_elements",
    "Pin or unpin elements to prevent/allow accidental movement. " +
    "Commonly used for linked models, grids, levels, and reference planes. " +
    "Supports batch pin/unpin by element IDs or by category.")]
[SkillParameter("action", "string",
    "'pin' to pin elements, 'unpin' to unpin elements, 'status' to check pin status.",
    isRequired: true,
    allowedValues: new[] { "pin", "unpin", "status" })]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs. Optional if using category filter.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Category to pin/unpin: 'links', 'grids', 'levels', 'reference_planes', " +
    "'equipment', 'all_pinned' (for unpin action only).",
    isRequired: false)]
public class PinUnpinSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString()?.ToLower();
        if (string.IsNullOrWhiteSpace(action))
            return SkillResult.Fail("'action' is required: 'pin', 'unpin', or 'status'.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString()?.ToLower();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elements = CollectTargetElements(document, idsStr, categoryStr, action);

            if (action == "status")
            {
                var pinned = elements.Where(e => e.Pinned).Select(e => new { id = e.Id.Value, name = e.Name, category = e.Category?.Name ?? "" }).ToList();
                var unpinned = elements.Where(e => !e.Pinned).Select(e => new { id = e.Id.Value, name = e.Name, category = e.Category?.Name ?? "" }).ToList();
                return new
                {
                    status = "ok",
                    pinnedCount = pinned.Count,
                    unpinnedCount = unpinned.Count,
                    pinned = pinned.Take(20).ToList(),
                    unpinned = unpinned.Take(20).ToList(),
                    message = $"{pinned.Count} pinned, {unpinned.Count} unpinned out of {elements.Count} elements.",
                    changed = 0
                };
            }

            bool shouldPin = action == "pin";
            using var tx = new Transaction(document, shouldPin ? "Pin elements" : "Unpin elements");
            tx.Start();
            try
            {
                int changed = 0;
                foreach (var elem in elements)
                {
                    if (elem.Pinned != shouldPin)
                    {
                        elem.Pinned = shouldPin;
                        changed++;
                    }
                }
                tx.Commit();
                return new
                {
                    status = "ok",
                    pinnedCount = 0,
                    unpinnedCount = 0,
                    pinned = new List<object>(),
                    unpinned = new List<object>(),
                    message = $"{(shouldPin ? "Pinned" : "Unpinned")} {changed} elements.",
                    changed
                };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new
                {
                    status = "error",
                    pinnedCount = 0, unpinnedCount = 0,
                    pinned = new List<object>(), unpinned = new List<object>(),
                    message = ex.Message, changed = 0
                };
            }
        });

        dynamic res = result!;
        return res.status == "ok"
            ? SkillResult.Ok(res.message, result)
            : SkillResult.Fail(res.message);
    }

    private static List<Element> CollectTargetElements(Document doc, string? idsStr, string? category, string action)
    {
        if (!string.IsNullOrWhiteSpace(idsStr))
        {
            return idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => doc.GetElement(new ElementId(long.Parse(s.Trim()))))
                .Where(e => e is not null)
                .ToList()!;
        }

        if (category == "all_pinned" && action == "unpin")
        {
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Pinned)
                .Take(5000)
                .ToList();
        }

        BuiltInCategory? bic = category switch
        {
            "links" => BuiltInCategory.OST_RvtLinks,
            "grids" => BuiltInCategory.OST_Grids,
            "levels" => BuiltInCategory.OST_Levels,
            "reference_planes" => BuiltInCategory.OST_CLines,
            "equipment" => BuiltInCategory.OST_MechanicalEquipment,
            _ => null
        };

        if (bic.HasValue)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(bic.Value)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
        }

        return [];
    }
}
