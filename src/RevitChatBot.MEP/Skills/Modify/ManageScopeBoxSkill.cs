using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("manage_scope_box",
    "List, assign, or remove scope boxes on views. Scope boxes control the crop region extent " +
    "across multiple views consistently. Use for BIM management of view extents.")]
[SkillParameter("action", "string",
    "'list' to list all scope boxes, 'assign' to assign a scope box to views, " +
    "'remove' to remove scope box from views, 'info' to show which views use a scope box.",
    isRequired: true,
    allowedValues: new[] { "list", "assign", "remove", "info" })]
[SkillParameter("scope_box_name", "string",
    "Scope box name (partial match). Required for 'assign', 'remove', 'info'.",
    isRequired: false)]
[SkillParameter("view_ids", "string",
    "Comma-separated view IDs to assign/remove scope box. Required for 'assign', 'remove'.",
    isRequired: false)]
[SkillParameter("view_name_pattern", "string",
    "Apply to views matching this name pattern (partial match). Alternative to view_ids.",
    isRequired: false)]
public class ManageScopeBoxSkill : ISkill
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
            return SkillResult.Fail("'action' is required.");

        var sbName = parameters.GetValueOrDefault("scope_box_name")?.ToString();
        var viewIdsStr = parameters.GetValueOrDefault("view_ids")?.ToString();
        var viewPattern = parameters.GetValueOrDefault("view_name_pattern")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var scopeBoxes = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (action == "list")
            {
                var list = scopeBoxes.Select(sb =>
                {
                    var bb = sb.get_BoundingBox(null);
                    return new
                    {
                        id = sb.Id.Value,
                        name = sb.Name,
                        minX = bb?.Min.X ?? 0,
                        minY = bb?.Min.Y ?? 0,
                        maxX = bb?.Max.X ?? 0,
                        maxY = bb?.Max.Y ?? 0,
                    };
                }).ToList();
                return new { status = "ok", message = $"Found {list.Count} scope boxes.", scopeBoxes = list, affectedViews = 0 };
            }

            if (string.IsNullOrWhiteSpace(sbName) && action != "list")
                return new { status = "error", message = "'scope_box_name' required.", scopeBoxes = new List<object>(), affectedViews = 0 };

            var targetSb = scopeBoxes.FirstOrDefault(sb =>
                sb.Name.Contains(sbName!, StringComparison.OrdinalIgnoreCase));

            if (action == "info")
            {
                if (targetSb is null)
                    return new { status = "error", message = $"Scope box '{sbName}' not found.", scopeBoxes = new List<object>(), affectedViews = 0 };

                var viewsUsing = new FilteredElementCollector(document)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .Where(v =>
                    {
                        try
                        {
                            var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                            return p is not null && p.AsElementId() == targetSb.Id;
                        }
                        catch { return false; }
                    })
                    .Select(v => new { id = v.Id.Value, name = v.Name, viewType = v.ViewType.ToString() })
                    .ToList();

                return new { status = "ok", message = $"Scope box '{targetSb.Name}' used by {viewsUsing.Count} views.", scopeBoxes = viewsUsing.Cast<object>().ToList(), affectedViews = viewsUsing.Count };
            }

            if (targetSb is null && action == "assign")
                return new { status = "error", message = $"Scope box '{sbName}' not found.", scopeBoxes = new List<object>(), affectedViews = 0 };

            var views = ResolveViews(document, viewIdsStr, viewPattern);
            if (views.Count == 0)
                return new { status = "error", message = "No views found matching criteria.", scopeBoxes = new List<object>(), affectedViews = 0 };

            using var tx = new Transaction(document, action == "assign" ? "Assign scope box" : "Remove scope box");
            tx.Start();
            try
            {
                int affected = 0;
                foreach (var view in views)
                {
                    var p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (p is null || p.IsReadOnly) continue;

                    if (action == "assign" && targetSb is not null)
                    {
                        p.Set(targetSb.Id);
                        affected++;
                    }
                    else if (action == "remove")
                    {
                        p.Set(ElementId.InvalidElementId);
                        affected++;
                    }
                }
                tx.Commit();

                var verb = action == "assign" ? $"Assigned scope box '{targetSb?.Name}' to" : "Removed scope box from";
                return new { status = "ok", message = $"{verb} {affected} views.", scopeBoxes = new List<object>(), affectedViews = affected };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { status = "error", message = ex.Message, scopeBoxes = new List<object>(), affectedViews = 0 };
            }
        });

        dynamic res = result!;
        return res.status == "ok"
            ? SkillResult.Ok(res.message, result)
            : SkillResult.Fail(res.message);
    }

    private static List<View> ResolveViews(Document doc, string? viewIds, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(viewIds))
        {
            return viewIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => doc.GetElement(new ElementId(long.Parse(s.Trim()))) as View)
                .Where(v => v is not null)
                .ToList()!;
        }

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return [];
    }
}
