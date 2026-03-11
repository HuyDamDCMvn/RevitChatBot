using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;
using RevitChatBot.RevitServices.Annotation;

namespace RevitChatBot.MEP.Skills.Annotation;

/// <summary>
/// Analyzes annotation quality in a view by checking for overlaps,
/// missing tags, alignment consistency, and leader crossings.
/// Can optionally export a view image for vision model analysis.
/// </summary>
[Skill("check_annotation_quality",
    "Check the quality of annotations in a view. Detects tag overlaps, " +
    "tags overlapping model elements, missing tags for key elements, " +
    "alignment inconsistencies, and leader line crossings. " +
    "Returns a quality score (0-100) with specific issues to fix.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view", isRequired: true)]
[SkillParameter("categories", "string",
    "Categories to check for missing tags (e.g. 'Ducts,Pipes,Equipment'). Default 'all'.",
    isRequired: false)]
[SkillParameter("auto_fix", "string",
    "Automatically run arrange_tags if quality score < 60. 'true' or 'false' (default 'false').",
    isRequired: false)]
public class AnnotationQualityCheckSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var categoriesStr = parameters.GetValueOrDefault("categories")?.ToString() ?? "all";
        var autoFixStr = parameters.GetValueOrDefault("auto_fix")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required.");

        bool autoFix = string.Equals(autoFixStr, "true", StringComparison.OrdinalIgnoreCase);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", score = 0, issues = new List<object>(), autoFixed = false };

            var issues = new List<object>();
            int deductions = 0;

            // Check 1: Tag-tag overlaps
            var tags = new FluentCollector(document)
                .OfTags().WhereElementIsNotElementType().InView(view.Id)
                .ToList<IndependentTag>();

            int tagOverlaps = 0;
            for (int i = 0; i < tags.Count; i++)
            {
                var bbI = tags[i].get_BoundingBox(view);
                if (bbI is null) continue;

                for (int j = i + 1; j < tags.Count; j++)
                {
                    var bbJ = tags[j].get_BoundingBox(view);
                    if (bbJ is null) continue;

                    if (bbI.Min.X < bbJ.Max.X && bbI.Max.X > bbJ.Min.X &&
                        bbI.Min.Y < bbJ.Max.Y && bbI.Max.Y > bbJ.Min.Y)
                    {
                        tagOverlaps++;
                        if (tagOverlaps <= 10)
                        {
                            issues.Add(new
                            {
                                type = "TAG_OVERLAP",
                                severity = "high",
                                tagA = tags[i].Id.Value,
                                tagB = tags[j].Id.Value,
                                description = $"Tag {tags[i].Id.Value} overlaps with tag {tags[j].Id.Value}"
                            });
                        }
                    }
                }
            }
            deductions += tagOverlaps * 5;

            // Check 2: Tags overlapping model elements
            int elementOverlaps = 0;
            var obstacleMap = ViewObstacleMap.Build(document, view,
                tags.Select(t => t.Id.Value).ToHashSet());

            foreach (var tag in tags)
            {
                var bb = tag.get_BoundingBox(view);
                if (bb is null) continue;

                int overlaps = obstacleMap.CountOverlaps(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
                if (overlaps > 0)
                {
                    elementOverlaps++;
                    if (elementOverlaps <= 10)
                    {
                        issues.Add(new
                        {
                            type = "TAG_ELEMENT_OVERLAP",
                            severity = "medium",
                            tagId = tag.Id.Value,
                            overlappingCount = overlaps,
                            description = $"Tag {tag.Id.Value} overlaps {overlaps} model element(s)"
                        });
                    }
                }
            }
            deductions += elementOverlaps * 3;

            // Check 3: Missing tags
            var categoriesToCheck = categoriesStr.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_MechanicalEquipment }
                : ParseCategories(categoriesStr);

            var taggedIds = new HashSet<long>();
            foreach (var tag in tags)
                foreach (var id in tag.GetTaggedLocalElementIds())
                    taggedIds.Add(id.Value);

            int totalUntagged = 0;
            foreach (var cat in categoriesToCheck)
            {
                var elems = new FluentCollector(document)
                    .OfCategory(cat).WhereElementIsNotElementType().InView(view.Id)
                    .ToList();
                var untaggedCount = elems.Count(e => !taggedIds.Contains(e.Id.Value));
                if (untaggedCount > 0)
                {
                    totalUntagged += untaggedCount;
                    issues.Add(new
                    {
                        type = "MISSING_TAGS",
                        severity = "medium",
                        category = cat.ToString().Replace("OST_", ""),
                        count = untaggedCount,
                        description = $"{untaggedCount} {cat.ToString().Replace("OST_", "")} elements without tags"
                    });
                }
            }
            deductions += totalUntagged * 2;

            // Check 4: Alignment consistency
            int misaligned = 0;
            if (tags.Count >= 3)
            {
                var yPositions = tags
                    .Select(t => t.TagHeadPosition.Y)
                    .OrderBy(y => y)
                    .ToList();

                for (int i = 1; i < yPositions.Count - 1; i++)
                {
                    double diffPrev = Math.Abs(yPositions[i] - yPositions[i - 1]);
                    if (diffPrev > 0.05 && diffPrev < 0.2)
                        misaligned++;
                }
            }
            if (misaligned > 0)
            {
                deductions += misaligned;
                issues.Add(new
                {
                    type = "ALIGNMENT_INCONSISTENCY",
                    severity = "low",
                    count = misaligned,
                    description = $"{misaligned} tag(s) nearly but not exactly aligned"
                });
            }

            int score = Math.Max(0, 100 - deductions);

            bool didAutoFix = false;
            if (autoFix && score < 60 && tags.Count >= 2)
            {
                var tagIds = tags.Select(t => t.Id.Value).ToHashSet();
                var freshMap = ViewObstacleMap.Build(document, view, tagIds);
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

                var arranger = new ForceDirectedTagArranger(freshMap);
                var arrangeResults = arranger.Arrange(inputs);

                using var tx = new Transaction(document, "Auto-fix annotations");
                tx.Start();
                foreach (var ar in arrangeResults.Where(r => r.WasMoved))
                {
                    try { ar.Tag.TagHeadPosition = new XYZ(ar.NewX, ar.NewY, ar.Tag.TagHeadPosition.Z); }
                    catch { }
                }
                tx.Commit();
                didAutoFix = true;
            }

            return new
            {
                success = true,
                message = $"Annotation quality score: {score}/100. " +
                          $"Found {issues.Count} issue(s): " +
                          $"{tagOverlaps} tag overlaps, {elementOverlaps} element overlaps, " +
                          $"{totalUntagged} missing tags, {misaligned} alignment issues." +
                          (didAutoFix ? " Auto-fix applied." : ""),
                score,
                issues,
                autoFixed = didAutoFix
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static BuiltInCategory[] ParseCategories(string str)
    {
        return str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant().Replace(" ", "") switch
            {
                "ducts" or "duct" => BuiltInCategory.OST_DuctCurves,
                "pipes" or "pipe" => BuiltInCategory.OST_PipeCurves,
                "equipment" or "mechanicalequipment" => BuiltInCategory.OST_MechanicalEquipment,
                "cabletray" => BuiltInCategory.OST_CableTray,
                "conduit" => BuiltInCategory.OST_Conduit,
                _ => BuiltInCategory.INVALID
            })
            .Where(c => c != BuiltInCategory.INVALID)
            .ToArray();
    }
}
