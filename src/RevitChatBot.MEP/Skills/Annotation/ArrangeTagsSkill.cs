using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;
using RevitChatBot.RevitServices.Annotation;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("arrange_tags",
    "Automatically arrange tags in a view to avoid overlapping with each other " +
    "AND with model elements (ducts, pipes, equipment, text notes, dimensions). " +
    "Uses force-directed layout with obstacle avoidance and optional snap-alignment. " +
    "Optionally filters by category (e.g. only duct tags or pipe tags).")]
[SkillParameter("view_id", "string",
    "Element ID of the target view", isRequired: true)]
[SkillParameter("category", "string",
    "Only arrange tags of this category (e.g. 'Ducts', 'Pipes', 'Mechanical Equipment'). " +
    "Leave empty to arrange all tags in the view.",
    isRequired: false)]
[SkillParameter("max_iterations", "string",
    "Maximum force-layout iterations (default 80). Higher = better results but slower.",
    isRequired: false)]
[SkillParameter("preferred_position", "string",
    "Preferred tag zone relative to host element: 'above', 'below', 'left', 'right', 'auto' (default 'auto').",
    isRequired: false,
    allowedValues: new[] { "above", "below", "left", "right", "auto" })]
[SkillParameter("avoid_elements", "string",
    "Avoid overlapping model elements (ducts, pipes, equipment). 'true' or 'false' (default 'true').",
    isRequired: false)]
[SkillParameter("snap_alignment", "string",
    "Snap nearly-aligned tags into exact rows. 'true' or 'false' (default 'true').",
    isRequired: false)]
public class ArrangeTagsSkill : ISkill
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
        var maxIterStr = parameters.GetValueOrDefault("max_iterations")?.ToString();
        var preferredStr = parameters.GetValueOrDefault("preferred_position")?.ToString() ?? "auto";
        var avoidStr = parameters.GetValueOrDefault("avoid_elements")?.ToString();
        var snapStr = parameters.GetValueOrDefault("snap_alignment")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required and must be a valid element ID.");

        int maxIterations = 80;
        if (!string.IsNullOrEmpty(maxIterStr) && int.TryParse(maxIterStr, out var parsed))
            maxIterations = Math.Clamp(parsed, 10, 300);

        bool avoidElements = !string.Equals(avoidStr, "false", StringComparison.OrdinalIgnoreCase);
        bool snapAlignment = !string.Equals(snapStr, "false", StringComparison.OrdinalIgnoreCase);

        var preferredZone = preferredStr?.ToLowerInvariant() switch
        {
            "above" => PreferredZone.Above,
            "below" => PreferredZone.Below,
            "left" => PreferredZone.Left,
            "right" => PreferredZone.Right,
            _ => PreferredZone.Auto
        };

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", arranged = 0, iterations = 0 };

            var tags = new FluentCollector(document)
                .OfTags()
                .WhereElementIsNotElementType()
                .InView(view.Id)
                .ToList<IndependentTag>();

            if (!string.IsNullOrEmpty(categoryFilter))
            {
                var bic = ResolveCategoryByName(categoryFilter);
                if (bic.HasValue)
                {
                    tags = tags.Where(t =>
                    {
                        var taggedIds = t.GetTaggedLocalElementIds();
                        return taggedIds.Any(id =>
                        {
                            var elem = document.GetElement(id);
                            return elem?.Category?.BuiltInCategory == bic.Value;
                        });
                    }).ToList();
                }
            }

            if (tags.Count < 2)
                return new { success = true, message = $"Only {tags.Count} tag(s) found — no arrangement needed.", arranged = 0, iterations = 0 };

            var tagIds = tags.Select(t => t.Id.Value).ToHashSet();
            var obstacleMap = avoidElements
                ? ViewObstacleMap.Build(document, view, tagIds)
                : ViewObstacleMap.Build(document, view, tagIds, cellSize: 1.0);

            var inputs = new List<TagLayoutInput>();
            foreach (var tag in tags)
            {
                var (w, h) = tag.GetTagSize(view);
                var hostElem = tag.GetTaggedElement();
                var hostCenter = hostElem?.GetCenter() ?? tag.TagHeadPosition;

                inputs.Add(new TagLayoutInput
                {
                    Tag = tag,
                    CurrentX = tag.TagHeadPosition.X,
                    CurrentY = tag.TagHeadPosition.Y,
                    Width = w,
                    Height = h,
                    HostElementX = hostCenter.X,
                    HostElementY = hostCenter.Y,
                    TaggedCategory = hostElem?.Category?.BuiltInCategory
                });
            }

            var settings = new ForceLayoutSettings
            {
                MaxIterations = maxIterations,
                PreferredZone = preferredZone,
                EnableSnapAlignment = snapAlignment
            };

            var arranger = new ForceDirectedTagArranger(obstacleMap, settings);
            var results = arranger.Arrange(inputs);

            using var tx = new Transaction(document, "Smart arrange tags");
            tx.Start();

            int movedCount = 0;
            foreach (var r in results.Where(r => r.WasMoved))
            {
                try
                {
                    r.Tag.TagHeadPosition = new XYZ(r.NewX, r.NewY, r.Tag.TagHeadPosition.Z);
                    movedCount++;
                }
                catch { /* tag may be pinned or have constraints */ }
            }

            tx.Commit();

            int iters = results.FirstOrDefault()?.IterationsUsed ?? 0;
            return new
            {
                success = true,
                message = $"Smart-arranged {tags.Count} tags ({movedCount} moved) in {iters} iterations. " +
                          $"Obstacle avoidance: {(avoidElements ? "ON" : "OFF")}, " +
                          $"Snap alignment: {(snapAlignment ? "ON" : "OFF")}.",
                arranged = movedCount,
                iterations = iters
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static BuiltInCategory? ResolveCategoryByName(string name)
    {
        var normalized = name.ToLowerInvariant().Replace(" ", "");
        return normalized switch
        {
            "ducts" or "duct" or "ốnggió" or "ống gió" => BuiltInCategory.OST_DuctCurves,
            "pipes" or "pipe" or "ốngnước" or "ống nước" => BuiltInCategory.OST_PipeCurves,
            "mechanicalequipment" or "equipment" or "thiếtbị" or "thiết bị"
                => BuiltInCategory.OST_MechanicalEquipment,
            "electricalequipment" => BuiltInCategory.OST_ElectricalEquipment,
            "cabletray" or "cabletrays" or "mángcáp" or "máng cáp" => BuiltInCategory.OST_CableTray,
            "conduit" or "conduits" or "ốngluồndây" or "ống luồn dây" => BuiltInCategory.OST_Conduit,
            "sprinkler" or "sprinklers" => BuiltInCategory.OST_Sprinklers,
            "plumbingfixtures" or "plumbingfixture" or "thiếtbịvệsinh" or "thiết bị vệ sinh"
                => BuiltInCategory.OST_PlumbingFixtures,
            "fittings" or "ductfittings" => BuiltInCategory.OST_DuctFitting,
            "pipefittings" or "pipefitting" => BuiltInCategory.OST_PipeFitting,
            _ => null
        };
    }
}
