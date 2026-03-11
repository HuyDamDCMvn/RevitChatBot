using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;
using RevitChatBot.RevitServices.Annotation;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("batch_annotate",
    "Tag and arrange annotations across multiple views at once. " +
    "Supports filtering views by type, level, or name pattern. " +
    "Runs place_tags → arrange_tags → auto_dimension pipeline per view.")]
[SkillParameter("view_ids", "string",
    "Comma-separated view IDs, or special values: 'all_plans', 'all_sections', 'all_elevations', " +
    "or 'by_level:<level name>' to select plan views on a specific level.",
    isRequired: true)]
[SkillParameter("categories", "string",
    "Comma-separated categories to tag (e.g. 'Ducts,Pipes,Equipment'). Default 'all'.",
    isRequired: false)]
[SkillParameter("operations", "string",
    "Comma-separated operations: 'tag', 'arrange', 'dimension'. Default 'tag,arrange'.",
    isRequired: false)]
[SkillParameter("preferred_position", "string",
    "Tag position preference: 'above', 'below', 'left', 'right', 'auto'",
    isRequired: false)]
public class BatchAnnotateSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdsStr = parameters.GetValueOrDefault("view_ids")?.ToString();
        var categoriesStr = parameters.GetValueOrDefault("categories")?.ToString() ?? "all";
        var opsStr = parameters.GetValueOrDefault("operations")?.ToString() ?? "tag,arrange";
        var preferredStr = parameters.GetValueOrDefault("preferred_position")?.ToString() ?? "auto";

        if (string.IsNullOrWhiteSpace(viewIdsStr))
            return SkillResult.Fail("view_ids is required.");

        var operations = opsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant()).ToHashSet();
        var categoryList = categoriesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewIds = ResolveViewIds(document, viewIdsStr);

            if (viewIds.Count == 0)
                return new { success = false, message = "No matching views found.", viewsProcessed = 0, totalTagged = 0, totalArranged = 0, totalDimensioned = 0 };

            int totalTagged = 0, totalArranged = 0, totalDimensioned = 0;
            var viewReports = new List<object>();

            foreach (var viewId in viewIds)
            {
                var viewElem = document.GetElement(viewId);
                if (viewElem is not View view || view.IsTemplate) continue;

                int viewTagged = 0, viewArranged = 0, viewDimensioned = 0;
                string viewName = view.Name;

                foreach (var category in categoryList)
                {
                    var bic = ResolveCategoryByName(category.Trim());
                    if (!bic.HasValue && !category.Equals("all", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var bics = category.Equals("all", StringComparison.OrdinalIgnoreCase)
                        ? GetAllMepCategories()
                        : [bic!.Value];

                    foreach (var cat in bics)
                    {
                        if (operations.Contains("tag"))
                        {
                            viewTagged += PlaceTagsForCategory(document, view, cat, preferredStr);
                        }
                    }
                }

                if (operations.Contains("arrange") && viewTagged > 0)
                {
                    viewArranged = ArrangeTagsInView(document, view, preferredStr);
                }

                if (operations.Contains("dimension"))
                {
                    viewDimensioned = AutoDimensionInView(document, view);
                }

                totalTagged += viewTagged;
                totalArranged += viewArranged;
                totalDimensioned += viewDimensioned;

                viewReports.Add(new
                {
                    viewName,
                    viewId = viewId.Value,
                    tagged = viewTagged,
                    arranged = viewArranged,
                    dimensioned = viewDimensioned
                });
            }

            return new
            {
                success = true,
                message = $"Batch annotation complete: {viewIds.Count} views processed. " +
                          $"Tagged: {totalTagged}, Arranged: {totalArranged}, Dimensioned: {totalDimensioned}.",
                viewsProcessed = viewIds.Count,
                totalTagged,
                totalArranged,
                totalDimensioned
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static int PlaceTagsForCategory(Document doc, View view, BuiltInCategory bic, string preferredStr)
    {
        var elements = new FluentCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .InView(view.Id)
            .ToList();

        var existingTags = new FluentCollector(doc)
            .OfTags().WhereElementIsNotElementType().InView(view.Id)
            .ToList<IndependentTag>();

        var taggedIds = new HashSet<long>();
        foreach (var tag in existingTags)
            foreach (var id in tag.GetTaggedLocalElementIds())
                taggedIds.Add(id.Value);

        var untagged = elements.Where(e => !taggedIds.Contains(e.Id.Value)).ToList();
        if (untagged.Count == 0) return 0;

        var obstacleMap = ViewObstacleMap.Build(doc, view);
        var scorer = new TagPositionScorer(obstacleMap);

        var preferredZone = preferredStr switch
        {
            "above" => PreferredZone.Above,
            "below" => PreferredZone.Below,
            "left" => PreferredZone.Left,
            "right" => PreferredZone.Right,
            _ => PreferredZone.Auto
        };

        using var tx = new Transaction(doc, $"Batch tag {bic}");
        tx.Start();

        int count = 0;
        foreach (var elem in untagged)
        {
            try
            {
                var center = elem.GetCenter();
                if (center is null) continue;

                var (bestX, bestY, _) = scorer.FindBestPosition(
                    center.X, center.Y, 0.4, 0.15, 2.5, preferredZone, bic);

                IndependentTag.Create(doc, view.Id, new Reference(elem),
                    false, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal,
                    new XYZ(bestX, bestY, center.Z));

                scorer.RegisterPlacedTag(bestX - 0.2, bestY - 0.075, bestX + 0.2, bestY + 0.075, bic);
                count++;
            }
            catch { }
        }

        tx.Commit();
        return count;
    }

    private static int ArrangeTagsInView(Document doc, View view, string preferredStr)
    {
        var tags = new FluentCollector(doc)
            .OfTags().WhereElementIsNotElementType().InView(view.Id)
            .ToList<IndependentTag>();

        if (tags.Count < 2) return 0;

        var tagIds = tags.Select(t => t.Id.Value).ToHashSet();
        var obstacleMap = ViewObstacleMap.Build(doc, view, tagIds);

        var preferredZone = preferredStr switch
        {
            "above" => PreferredZone.Above, "below" => PreferredZone.Below,
            "left" => PreferredZone.Left, "right" => PreferredZone.Right,
            _ => PreferredZone.Auto
        };

        var inputs = tags.Select(t =>
        {
            var (w, h) = t.GetTagSize(view);
            var host = t.GetTaggedElement();
            var hostCenter = host?.GetCenter() ?? t.TagHeadPosition;
            return new TagLayoutInput
            {
                Tag = t, CurrentX = t.TagHeadPosition.X, CurrentY = t.TagHeadPosition.Y,
                Width = w, Height = h,
                HostElementX = hostCenter.X, HostElementY = hostCenter.Y,
                TaggedCategory = host?.Category?.BuiltInCategory
            };
        }).ToList();

        var arranger = new ForceDirectedTagArranger(obstacleMap,
            new ForceLayoutSettings { PreferredZone = preferredZone });
        var results = arranger.Arrange(inputs);

        using var tx = new Transaction(doc, "Batch arrange tags");
        tx.Start();
        int moved = 0;
        foreach (var r in results.Where(r => r.WasMoved))
        {
            try
            {
                r.Tag.TagHeadPosition = new XYZ(r.NewX, r.NewY, r.Tag.TagHeadPosition.Z);
                moved++;
            }
            catch { }
        }
        tx.Commit();
        return moved;
    }

    private static int AutoDimensionInView(Document doc, View view)
    {
        var linearElements = new[] {
            BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit
        }.SelectMany(cat => new FluentCollector(doc)
            .OfCategory(cat).WhereElementIsNotElementType().InView(view.Id).ToList())
        .Where(e => e.Location is LocationCurve)
        .ToList();

        if (linearElements.Count < 2) return 0;

        // Simple approach: dimension chain along major runs
        using var tx = new Transaction(doc, "Batch auto-dimension");
        tx.Start();
        int created = 0;

        try
        {
            var refArray = new ReferenceArray();
            foreach (var elem in linearElements.Take(20))
                refArray.Append(new Reference(elem));

            if (refArray.Size >= 2)
            {
                var first = linearElements.First().GetCenter() ?? XYZ.Zero;
                var last = linearElements.Last().GetCenter() ?? XYZ.Zero;
                var dimLine = Line.CreateBound(
                    new XYZ(first.X - 0.5, first.Y + 1.5, 0),
                    new XYZ(last.X + 0.5, first.Y + 1.5, 0));
                doc.Create.NewDimension(view, dimLine, refArray);
                created++;
            }
        }
        catch { }

        tx.Commit();
        return created;
    }

    private static List<ElementId> ResolveViewIds(Document doc, string spec)
    {
        var normalized = spec.Trim().ToLowerInvariant();

        if (normalized.StartsWith("by_level:"))
        {
            var levelName = spec[9..].Trim();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.GenLevel?.Name
                    .Contains(levelName, StringComparison.OrdinalIgnoreCase) == true)
                .Select(v => v.Id).ToList();
        }

        return normalized switch
        {
            "all_plans" => new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                .Select(v => v.Id).ToList(),

            "all_sections" => new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection)).Cast<ViewSection>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Id).ToList(),

            "all_elevations" => new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection)).Cast<ViewSection>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.Elevation)
                .Select(v => v.Id).ToList(),

            _ => spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => long.TryParse(s, out _))
                .Select(s => new ElementId(long.Parse(s)))
                .ToList()
        };
    }

    private static BuiltInCategory? ResolveCategoryByName(string name)
    {
        var n = name.ToLowerInvariant().Replace(" ", "");
        return n switch
        {
            "ducts" or "duct" => BuiltInCategory.OST_DuctCurves,
            "pipes" or "pipe" => BuiltInCategory.OST_PipeCurves,
            "equipment" or "mechanicalequipment" => BuiltInCategory.OST_MechanicalEquipment,
            "cabletray" or "cabletrays" => BuiltInCategory.OST_CableTray,
            "conduit" or "conduits" => BuiltInCategory.OST_Conduit,
            "sprinklers" or "sprinkler" => BuiltInCategory.OST_Sprinklers,
            "plumbingfixtures" => BuiltInCategory.OST_PlumbingFixtures,
            "electricalequipment" => BuiltInCategory.OST_ElectricalEquipment,
            _ => null
        };
    }

    private static List<BuiltInCategory> GetAllMepCategories() =>
    [
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit,
        BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_PlumbingFixtures
    ];
}
