using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;
using RevitChatBot.RevitServices.Annotation;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("place_tags",
    "Automatically place tags on untagged elements in a view. " +
    "Uses smart positioning to find optimal tag locations that avoid overlapping " +
    "with model elements, other tags, text notes, and dimensions. " +
    "Supports MEP categories: ducts, pipes, equipment, cable trays, conduits, sprinklers, plumbing fixtures.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view where tags will be placed", isRequired: true)]
[SkillParameter("category", "string",
    "Element category to tag (e.g. 'Ducts', 'Pipes', 'Mechanical Equipment')",
    isRequired: true)]
[SkillParameter("add_leader", "string",
    "Whether to add a leader line to tags ('true' or 'false', default 'false')",
    isRequired: false)]
[SkillParameter("tag_orientation", "string",
    "Tag orientation: 'horizontal' or 'vertical' (default 'horizontal')",
    isRequired: false)]
[SkillParameter("preferred_position", "string",
    "Preferred tag position: 'above', 'below', 'left', 'right', 'auto' (default 'auto')",
    isRequired: false,
    allowedValues: new[] { "above", "below", "left", "right", "auto" })]
[SkillParameter("auto_arrange", "string",
    "Run smart arrangement after placing tags. 'true' or 'false' (default 'true')",
    isRequired: false)]
public class PlaceTagsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();
        var addLeaderStr = parameters.GetValueOrDefault("add_leader")?.ToString();
        var orientationStr = parameters.GetValueOrDefault("tag_orientation")?.ToString();
        var preferredStr = parameters.GetValueOrDefault("preferred_position")?.ToString() ?? "auto";
        var autoArrangeStr = parameters.GetValueOrDefault("auto_arrange")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required and must be a valid element ID.");
        if (string.IsNullOrWhiteSpace(categoryStr))
            return SkillResult.Fail("category is required.");

        var bic = ResolveCategoryByName(categoryStr);
        if (!bic.HasValue)
            return SkillResult.Fail($"Unsupported category: '{categoryStr}'. Use: Ducts, Pipes, Mechanical Equipment, Cable Trays, Conduits, Sprinklers, Plumbing Fixtures.");

        bool addLeader = string.Equals(addLeaderStr, "true", StringComparison.OrdinalIgnoreCase);
        bool autoArrange = !string.Equals(autoArrangeStr, "false", StringComparison.OrdinalIgnoreCase);
        var orientation = string.Equals(orientationStr, "vertical", StringComparison.OrdinalIgnoreCase)
            ? TagOrientation.Vertical
            : TagOrientation.Horizontal;
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
                return new { success = false, message = "Invalid view ID.", tagged = 0, skipped = 0, arranged = 0 };

            var elementsInView = new FluentCollector(document)
                .OfCategory(bic.Value)
                .WhereElementIsNotElementType()
                .InView(view.Id)
                .ToList();

            var existingTags = new FluentCollector(document)
                .OfTags()
                .WhereElementIsNotElementType()
                .InView(view.Id)
                .ToList<IndependentTag>();

            var alreadyTaggedIds = new HashSet<long>();
            foreach (var tag in existingTags)
                foreach (var id in tag.GetTaggedLocalElementIds())
                    alreadyTaggedIds.Add(id.Value);

            var untagged = elementsInView
                .Where(e => !alreadyTaggedIds.Contains(e.Id.Value))
                .ToList();

            if (untagged.Count == 0)
                return new
                {
                    success = true,
                    message = $"All {elementsInView.Count} elements in the view already have tags.",
                    tagged = 0, skipped = elementsInView.Count, arranged = 0
                };

            var obstacleMap = ViewObstacleMap.Build(document, view);
            var scorer = new TagPositionScorer(obstacleMap);

            double defaultTagW = orientation == TagOrientation.Horizontal ? 0.4 : 0.15;
            double defaultTagH = orientation == TagOrientation.Horizontal ? 0.15 : 0.4;

            using var tx = new Transaction(document, "Smart place tags");
            tx.Start();

            int taggedCount = 0;
            int failedCount = 0;
            var newTagIds = new List<ElementId>();

            foreach (var element in untagged)
            {
                try
                {
                    var center = element.GetCenter();
                    if (center is null) { failedCount++; continue; }

                    var (bestX, bestY, _) = scorer.FindBestPosition(
                        center.X, center.Y,
                        defaultTagW, defaultTagH,
                        searchRadius: 2.5,
                        preferredZone: preferredZone,
                        taggedCategory: bic.Value);

                    var tagPoint = new XYZ(bestX, bestY, center.Z);

                    var reference = new Reference(element);
                    var newTag = IndependentTag.Create(
                        document, view.Id, reference,
                        addLeader, TagMode.TM_ADDBY_CATEGORY,
                        orientation, tagPoint);

                    scorer.RegisterPlacedTag(
                        bestX - defaultTagW / 2, bestY - defaultTagH / 2,
                        bestX + defaultTagW / 2, bestY + defaultTagH / 2,
                        bic.Value);

                    obstacleMap.AddObstacle(newTag.Id.Value,
                        bestX - defaultTagW / 2, bestY - defaultTagH / 2,
                        bestX + defaultTagW / 2, bestY + defaultTagH / 2);

                    newTagIds.Add(newTag.Id);
                    taggedCount++;
                }
                catch
                {
                    failedCount++;
                }
            }

            tx.Commit();

            int arrangedCount = 0;
            if (autoArrange && taggedCount >= 2)
            {
                var allTags = new FluentCollector(document)
                    .OfTags()
                    .WhereElementIsNotElementType()
                    .InView(view.Id)
                    .ToList<IndependentTag>();

                if (!string.IsNullOrEmpty(categoryStr))
                {
                    allTags = allTags.Where(t =>
                    {
                        var ids = t.GetTaggedLocalElementIds();
                        return ids.Any(id => document.GetElement(id)?.Category?.BuiltInCategory == bic.Value);
                    }).ToList();
                }

                if (allTags.Count >= 2)
                {
                    var tagIdSet = allTags.Select(t => t.Id.Value).ToHashSet();
                    var freshMap = ViewObstacleMap.Build(document, view, tagIdSet);
                    var inputs = allTags.Select(t =>
                    {
                        var (w, h) = t.GetTagSize(view);
                        var hostElem = t.GetTaggedElement();
                        var hostCenter = hostElem?.GetCenter() ?? t.TagHeadPosition;
                        return new TagLayoutInput
                        {
                            Tag = t,
                            CurrentX = t.TagHeadPosition.X,
                            CurrentY = t.TagHeadPosition.Y,
                            Width = w, Height = h,
                            HostElementX = hostCenter.X,
                            HostElementY = hostCenter.Y,
                            TaggedCategory = hostElem?.Category?.BuiltInCategory
                        };
                    }).ToList();

                    var arranger = new ForceDirectedTagArranger(freshMap, new ForceLayoutSettings
                    {
                        PreferredZone = preferredZone
                    });
                    var arrangeResults = arranger.Arrange(inputs);

                    using var tx2 = new Transaction(document, "Auto-arrange after placement");
                    tx2.Start();
                    foreach (var ar in arrangeResults.Where(r => r.WasMoved))
                    {
                        try
                        {
                            ar.Tag.TagHeadPosition = new XYZ(ar.NewX, ar.NewY, ar.Tag.TagHeadPosition.Z);
                            arrangedCount++;
                        }
                        catch { }
                    }
                    tx2.Commit();
                }
            }

            return new
            {
                success = true,
                message = $"Placed {taggedCount} smart-positioned tags on {categoryStr}. " +
                          $"{alreadyTaggedIds.Count} already tagged, {failedCount} failed" +
                          (arrangedCount > 0 ? $", {arrangedCount} tags auto-arranged." : "."),
                tagged = taggedCount,
                skipped = alreadyTaggedIds.Count,
                arranged = arrangedCount
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
            "cabletrays" or "cabletray" or "mángcáp" or "máng cáp" => BuiltInCategory.OST_CableTray,
            "conduits" or "conduit" or "ốngluồndây" or "ống luồn dây" => BuiltInCategory.OST_Conduit,
            "sprinklers" or "sprinkler" => BuiltInCategory.OST_Sprinklers,
            "plumbingfixtures" or "plumbingfixture" or "thiếtbịvệsinh" or "thiết bị vệ sinh"
                => BuiltInCategory.OST_PlumbingFixtures,
            "fittings" or "ductfittings" => BuiltInCategory.OST_DuctFitting,
            "pipefittings" or "pipefitting" => BuiltInCategory.OST_PipeFitting,
            "flexducts" or "flexduct" => BuiltInCategory.OST_FlexDuctCurves,
            "flexpipes" or "flexpipe" => BuiltInCategory.OST_FlexPipeCurves,
            _ => null
        };
    }
}
