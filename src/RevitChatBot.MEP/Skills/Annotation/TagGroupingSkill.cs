using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("group_tags",
    "Group nearby tags that show the same value into a single representative tag. " +
    "Reduces visual clutter in busy drawings by removing redundant tags. " +
    "Keeps one tag per group and deletes the rest, optionally adding a count note.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view", isRequired: true)]
[SkillParameter("category", "string",
    "Only group tags for this category (e.g. 'Ducts', 'Pipes'). Default groups all.",
    isRequired: false)]
[SkillParameter("grouping_radius", "string",
    "Max distance between tags to consider them a group, in feet (default '3.0')",
    isRequired: false)]
[SkillParameter("min_group_size", "string",
    "Minimum tags in a group before consolidation (default '3')",
    isRequired: false)]
public class TagGroupingSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var categoryFilter = parameters.GetValueOrDefault("category")?.ToString();
        var radiusStr = parameters.GetValueOrDefault("grouping_radius")?.ToString();
        var minSizeStr = parameters.GetValueOrDefault("min_group_size")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required.");

        double radius = 3.0;
        if (!string.IsNullOrEmpty(radiusStr) && double.TryParse(radiusStr, out var pr))
            radius = Math.Max(0.5, pr);

        int minGroupSize = 3;
        if (!string.IsNullOrEmpty(minSizeStr) && int.TryParse(minSizeStr, out var ms))
            minGroupSize = Math.Max(2, ms);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", grouped = 0, deleted = 0 };

            var tags = new FluentCollector(document)
                .OfTags().WhereElementIsNotElementType().InView(view.Id)
                .ToList<IndependentTag>();

            if (!string.IsNullOrEmpty(categoryFilter))
            {
                var bic = ResolveCategoryByName(categoryFilter);
                if (bic.HasValue)
                {
                    tags = tags.Where(t =>
                    {
                        var ids = t.GetTaggedLocalElementIds();
                        return ids.Any(id => document.GetElement(id)?.Category?.BuiltInCategory == bic.Value);
                    }).ToList();
                }
            }

            // Build tag data with displayed value
            var tagData = tags.Select(t =>
            {
                var text = GetTagDisplayValue(t, document);
                return new { Tag = t, Position = t.TagHeadPosition, Text = text };
            }).Where(td => td.Text is not null).ToList();

            // Group tags by displayed value AND proximity
            var groups = new List<List<IndependentTag>>();
            var assigned = new HashSet<long>();

            foreach (var td in tagData)
            {
                if (assigned.Contains(td.Tag.Id.Value)) continue;

                var group = new List<IndependentTag> { td.Tag };
                assigned.Add(td.Tag.Id.Value);

                foreach (var other in tagData)
                {
                    if (assigned.Contains(other.Tag.Id.Value)) continue;
                    if (other.Text != td.Text) continue;

                    double dist = td.Position.DistanceTo(other.Position);
                    if (dist <= radius)
                    {
                        group.Add(other.Tag);
                        assigned.Add(other.Tag.Id.Value);
                    }
                }

                if (group.Count >= minGroupSize)
                    groups.Add(group);
            }

            if (groups.Count == 0)
                return new { success = true, message = "No tag groups found for consolidation.", grouped = 0, deleted = 0 };

            using var tx = new Transaction(document, "Group tags");
            tx.Start();

            int totalDeleted = 0;
            foreach (var group in groups)
            {
                // Keep the tag closest to the center of the group
                var center = new XYZ(
                    group.Average(t => t.TagHeadPosition.X),
                    group.Average(t => t.TagHeadPosition.Y),
                    group.Average(t => t.TagHeadPosition.Z));

                var keeper = group.OrderBy(t => t.TagHeadPosition.DistanceTo(center)).First();

                foreach (var tag in group)
                {
                    if (tag.Id == keeper.Id) continue;
                    try
                    {
                        document.Delete(tag.Id);
                        totalDeleted++;
                    }
                    catch { }
                }

                // Move keeper to group center
                try { keeper.TagHeadPosition = center; } catch { }
            }

            tx.Commit();

            return new
            {
                success = true,
                message = $"Grouped {groups.Count} tag clusters, deleted {totalDeleted} redundant tags. " +
                    $"Kept {groups.Count} representative tags.",
                grouped = groups.Count,
                deleted = totalDeleted
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static string? GetTagDisplayValue(IndependentTag tag, Document doc)
    {
        try
        {
            var tagText = tag.TagText;
            if (!string.IsNullOrWhiteSpace(tagText)) return tagText;

            var host = tag.GetTaggedElement();
            if (host is null) return null;

            return host.GetParamString(BuiltInParameter.RBS_CALCULATED_SIZE)
                ?? host.GetParamString(BuiltInParameter.ALL_MODEL_MARK)
                ?? host.Name;
        }
        catch
        {
            return null;
        }
    }

    private static BuiltInCategory? ResolveCategoryByName(string name)
    {
        var n = name.ToLowerInvariant().Replace(" ", "");
        return n switch
        {
            "ducts" or "duct" => BuiltInCategory.OST_DuctCurves,
            "pipes" or "pipe" => BuiltInCategory.OST_PipeCurves,
            "equipment" => BuiltInCategory.OST_MechanicalEquipment,
            "cabletray" => BuiltInCategory.OST_CableTray,
            "conduit" => BuiltInCategory.OST_Conduit,
            _ => null
        };
    }
}
