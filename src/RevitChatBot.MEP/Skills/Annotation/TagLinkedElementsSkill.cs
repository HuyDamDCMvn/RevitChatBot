using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;
using RevitChatBot.RevitServices.Annotation;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("tag_linked_elements",
    "Place tags on elements from linked Revit models in the current view. " +
    "Transforms linked element coordinates to host model space, " +
    "then uses smart positioning to avoid overlapping.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view", isRequired: true)]
[SkillParameter("link_name", "string",
    "Name of the linked model (partial match) or 'all' for all links",
    isRequired: true)]
[SkillParameter("category", "string",
    "Category to tag in linked model: 'Ducts', 'Pipes', 'Equipment', etc.",
    isRequired: true)]
[SkillParameter("add_leader", "string",
    "Whether to add leader lines: 'true' or 'false' (default 'false')",
    isRequired: false)]
public class TagLinkedElementsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var linkNameStr = parameters.GetValueOrDefault("link_name")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();
        var addLeaderStr = parameters.GetValueOrDefault("add_leader")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required.");
        if (string.IsNullOrWhiteSpace(linkNameStr))
            return SkillResult.Fail("link_name is required.");
        if (string.IsNullOrWhiteSpace(categoryStr))
            return SkillResult.Fail("category is required.");

        var bic = ResolveCategoryByName(categoryStr);
        if (!bic.HasValue)
            return SkillResult.Fail($"Unsupported category: '{categoryStr}'.");

        bool addLeader = string.Equals(addLeaderStr, "true", StringComparison.OrdinalIgnoreCase);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", tagged = 0, links = 0 };

            var links = new FluentCollector(document)
                .OfLinks()
                .WhereElementIsNotElementType()
                .ToList<RevitLinkInstance>();

            bool matchAll = linkNameStr.Equals("all", StringComparison.OrdinalIgnoreCase);
            if (!matchAll)
            {
                links = links.Where(l =>
                    l.Name.Contains(linkNameStr, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (links.Count == 0)
                return new { success = false, message = $"No linked models found matching '{linkNameStr}'.", tagged = 0, links = 0 };

            var obstacleMap = ViewObstacleMap.Build(document, view);
            var scorer = new TagPositionScorer(obstacleMap);

            using var tx = new Transaction(document, "Tag linked elements");
            tx.Start();

            int totalTagged = 0;
            foreach (var link in links)
            {
                var linkDoc = link.GetLinkDocument();
                if (linkDoc is null) continue;

                var transform = link.GetTotalTransform();

                var elementsInLink = new FilteredElementCollector(linkDoc)
                    .OfCategory(bic.Value)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var elem in elementsInLink)
                {
                    try
                    {
                        var center = elem.GetCenter();
                        if (center is null) continue;

                        var hostCoord = transform.OfPoint(center);

                        var (bestX, bestY, _) = scorer.FindBestPosition(
                            hostCoord.X, hostCoord.Y, 0.4, 0.15, 2.5,
                            PreferredZone.Auto, bic.Value);

                        var tagPoint = new XYZ(bestX, bestY, hostCoord.Z);

                        var linkRef = new Reference(elem)
                            .CreateLinkReference(link);

                        if (linkRef is not null)
                        {
                            IndependentTag.Create(document, view.Id, linkRef,
                                addLeader, TagMode.TM_ADDBY_CATEGORY,
                                TagOrientation.Horizontal, tagPoint);

                            scorer.RegisterPlacedTag(
                                bestX - 0.2, bestY - 0.075,
                                bestX + 0.2, bestY + 0.075, bic.Value);
                            totalTagged++;
                        }
                    }
                    catch { }
                }
            }

            tx.Commit();

            return new
            {
                success = true,
                message = $"Tagged {totalTagged} elements from {links.Count} linked model(s) " +
                          $"(category: {categoryStr}).",
                tagged = totalTagged,
                links = links.Count
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static BuiltInCategory? ResolveCategoryByName(string name)
    {
        var n = name.ToLowerInvariant().Replace(" ", "");
        return n switch
        {
            "ducts" or "duct" => BuiltInCategory.OST_DuctCurves,
            "pipes" or "pipe" => BuiltInCategory.OST_PipeCurves,
            "equipment" or "mechanicalequipment" => BuiltInCategory.OST_MechanicalEquipment,
            "electricalequipment" => BuiltInCategory.OST_ElectricalEquipment,
            "cabletray" or "cabletrays" => BuiltInCategory.OST_CableTray,
            "conduit" or "conduits" => BuiltInCategory.OST_Conduit,
            "sprinklers" or "sprinkler" => BuiltInCategory.OST_Sprinklers,
            "plumbingfixtures" => BuiltInCategory.OST_PlumbingFixtures,
            _ => null
        };
    }
}
