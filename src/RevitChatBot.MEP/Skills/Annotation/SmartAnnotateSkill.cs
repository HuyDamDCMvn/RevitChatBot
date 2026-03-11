using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;
using RevitChatBot.RevitServices.Annotation;

namespace RevitChatBot.MEP.Skills.Annotation;

/// <summary>
/// High-level smart annotation skill that runs the full annotation pipeline
/// in one shot: place tags → arrange → dimension → quality check.
/// The agent can call this with a natural language instruction and the skill
/// handles the entire workflow with quality feedback.
/// </summary>
[Skill("smart_annotate",
    "One-click smart annotation: automatically tag, arrange, and dimension " +
    "a view in a single operation. Runs the full pipeline: " +
    "1) Place tags on untagged elements, 2) Smart-arrange to avoid overlaps, " +
    "3) Optionally add dimensions, 4) Quality check with auto-retry. " +
    "Just provide a view and categories — the skill handles everything.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view. If omitted, uses the active view.",
    isRequired: false)]
[SkillParameter("categories", "string",
    "Categories to annotate: comma-separated (e.g. 'Ducts,Pipes') or 'all'. Default 'all'.",
    isRequired: false)]
[SkillParameter("add_dimensions", "string",
    "Also create dimensions. 'true' or 'false' (default 'false').",
    isRequired: false)]
[SkillParameter("preferred_position", "string",
    "Tag position: 'above', 'below', 'left', 'right', 'auto'",
    isRequired: false)]
[SkillParameter("quality_threshold", "string",
    "Minimum quality score (0-100). If below, auto-retry arrange. Default '60'.",
    isRequired: false)]
public class SmartAnnotateSkill : ISkill
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
        var addDimStr = parameters.GetValueOrDefault("add_dimensions")?.ToString();
        var preferredStr = parameters.GetValueOrDefault("preferred_position")?.ToString() ?? "auto";
        var thresholdStr = parameters.GetValueOrDefault("quality_threshold")?.ToString();

        bool addDimensions = string.Equals(addDimStr, "true", StringComparison.OrdinalIgnoreCase);
        int qualityThreshold = 60;
        if (!string.IsNullOrEmpty(thresholdStr) && int.TryParse(thresholdStr, out var qt))
            qualityThreshold = Math.Clamp(qt, 0, 100);

        var preferredZone = preferredStr switch
        {
            "above" => PreferredZone.Above, "below" => PreferredZone.Below,
            "left" => PreferredZone.Left, "right" => PreferredZone.Right,
            _ => PreferredZone.Auto
        };

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            View? view = null;
            if (!string.IsNullOrWhiteSpace(viewIdStr) && long.TryParse(viewIdStr, out var viewIdLong))
            {
                var elem = document.GetElement(new ElementId(viewIdLong));
                view = elem as View;
            }

            if (view is null)
                return new { success = false, message = "Could not resolve view. Provide a valid view_id.", totalTagged = 0, totalArranged = 0, totalDimensioned = 0, qualityScore = 0, retried = false };

            var categories = categoriesStr.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? GetAllMepCategories()
                : categoriesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ResolveCategoryByName).Where(c => c.HasValue).Select(c => c!.Value).ToList();

            // Step 1: Place tags
            int totalTagged = 0;
            {
                var existingTags = new FluentCollector(document)
                    .OfTags().WhereElementIsNotElementType().InView(view.Id)
                    .ToList<IndependentTag>();

                var taggedIds = new HashSet<long>();
                foreach (var tag in existingTags)
                    foreach (var id in tag.GetTaggedLocalElementIds())
                        taggedIds.Add(id.Value);

                var obstacleMap = ViewObstacleMap.Build(document, view);
                var scorer = new TagPositionScorer(obstacleMap);

                using var tx = new Transaction(document, "Smart annotate — place tags");
                tx.Start();

                foreach (var cat in categories)
                {
                    var elems = new FluentCollector(document)
                        .OfCategory(cat).WhereElementIsNotElementType().InView(view.Id)
                        .ToList()
                        .Where(e => !taggedIds.Contains(e.Id.Value));

                    foreach (var elem in elems)
                    {
                        try
                        {
                            var center = elem.GetCenter();
                            if (center is null) continue;

                            var (bx, by, _) = scorer.FindBestPosition(
                                center.X, center.Y, 0.4, 0.15, 2.5, preferredZone, cat);

                            IndependentTag.Create(document, view.Id,
                                new Reference(elem), false,
                                TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal,
                                new XYZ(bx, by, center.Z));

                            scorer.RegisterPlacedTag(bx - 0.2, by - 0.075, bx + 0.2, by + 0.075, cat);
                            totalTagged++;
                        }
                        catch { }
                    }
                }

                tx.Commit();
            }

            // Step 2: Arrange tags
            int totalArranged = 0;
            {
                var allTags = new FluentCollector(document)
                    .OfTags().WhereElementIsNotElementType().InView(view.Id)
                    .ToList<IndependentTag>();

                if (allTags.Count >= 2)
                {
                    var tagIdSet = allTags.Select(t => t.Id.Value).ToHashSet();
                    var freshMap = ViewObstacleMap.Build(document, view, tagIdSet);

                    var inputs = allTags.Select(t =>
                    {
                        var (w, h) = t.GetTagSize(view);
                        var host = t.GetTaggedElement();
                        var hc = host?.GetCenter() ?? t.TagHeadPosition;
                        return new TagLayoutInput
                        {
                            Tag = t, CurrentX = t.TagHeadPosition.X, CurrentY = t.TagHeadPosition.Y,
                            Width = w, Height = h,
                            HostElementX = hc.X, HostElementY = hc.Y,
                            TaggedCategory = host?.Category?.BuiltInCategory
                        };
                    }).ToList();

                    var arranger = new ForceDirectedTagArranger(freshMap,
                        new ForceLayoutSettings { PreferredZone = preferredZone });
                    var arranged = arranger.Arrange(inputs);

                    using var tx = new Transaction(document, "Smart annotate — arrange");
                    tx.Start();
                    foreach (var ar in arranged.Where(a => a.WasMoved))
                    {
                        try
                        {
                            ar.Tag.TagHeadPosition = new XYZ(ar.NewX, ar.NewY, ar.Tag.TagHeadPosition.Z);
                            totalArranged++;
                        }
                        catch { }
                    }
                    tx.Commit();
                }
            }

            // Step 3: Optional dimensioning
            int totalDimensioned = 0;
            if (addDimensions)
            {
                var linears = categories
                    .Where(c => c is BuiltInCategory.OST_DuctCurves or BuiltInCategory.OST_PipeCurves
                        or BuiltInCategory.OST_CableTray or BuiltInCategory.OST_Conduit)
                    .SelectMany(cat => new FluentCollector(document)
                        .OfCategory(cat).WhereElementIsNotElementType().InView(view.Id).ToList())
                    .Where(e => e.Location is LocationCurve)
                    .ToList();

                if (linears.Count >= 2)
                {
                    using var tx = new Transaction(document, "Smart annotate — dimension");
                    tx.Start();
                    try
                    {
                        var refArray = new ReferenceArray();
                        foreach (var elem in linears.Take(25))
                            refArray.Append(new Reference(elem));

                        if (refArray.Size >= 2)
                        {
                            var first = linears.First().GetCenter() ?? XYZ.Zero;
                            var last = linears.Last().GetCenter() ?? XYZ.Zero;
                            var dimLine = Line.CreateBound(
                                new XYZ(first.X - 0.5, first.Y + 2.0, 0),
                                new XYZ(last.X + 0.5, first.Y + 2.0, 0));
                            document.Create.NewDimension(view, dimLine, refArray);
                            totalDimensioned++;
                        }
                    }
                    catch { }
                    tx.Commit();
                }
            }

            // Step 4: Quality check + auto-retry
            int qualityScore = ComputeQualityScore(document, view);
            bool retried = false;

            if (qualityScore < qualityThreshold && totalArranged > 0)
            {
                var retryTags = new FluentCollector(document)
                    .OfTags().WhereElementIsNotElementType().InView(view.Id)
                    .ToList<IndependentTag>();

                if (retryTags.Count >= 2)
                {
                    var tagIdSet = retryTags.Select(t => t.Id.Value).ToHashSet();
                    var retryMap = ViewObstacleMap.Build(document, view, tagIdSet);
                    var retryInputs = retryTags.Select(t =>
                    {
                        var (w, h) = t.GetTagSize(view);
                        var host = t.GetTaggedElement();
                        var hc = host?.GetCenter() ?? t.TagHeadPosition;
                        return new TagLayoutInput
                        {
                            Tag = t, CurrentX = t.TagHeadPosition.X, CurrentY = t.TagHeadPosition.Y,
                            Width = w, Height = h,
                            HostElementX = hc.X, HostElementY = hc.Y,
                            TaggedCategory = host?.Category?.BuiltInCategory
                        };
                    }).ToList();

                    var arranger2 = new ForceDirectedTagArranger(retryMap, new ForceLayoutSettings
                    {
                        MaxIterations = 120, PreferredZone = preferredZone,
                        RepulsiveStrength = 0.6, DampingFactor = 0.35
                    });
                    var retry = arranger2.Arrange(retryInputs);

                    using var tx = new Transaction(document, "Smart annotate — retry arrange");
                    tx.Start();
                    foreach (var ar in retry.Where(a => a.WasMoved))
                    {
                        try { ar.Tag.TagHeadPosition = new XYZ(ar.NewX, ar.NewY, ar.Tag.TagHeadPosition.Z); }
                        catch { }
                    }
                    tx.Commit();

                    qualityScore = ComputeQualityScore(document, view);
                    retried = true;
                }
            }

            return new
            {
                success = true,
                message = $"Smart annotation complete. " +
                          $"Tagged: {totalTagged}, Arranged: {totalArranged}, " +
                          $"Dimensioned: {totalDimensioned}. " +
                          $"Quality score: {qualityScore}/100" +
                          (retried ? " (after auto-retry)." : "."),
                totalTagged, totalArranged, totalDimensioned, qualityScore, retried
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static int ComputeQualityScore(Document doc, View view)
    {
        var tags = new FluentCollector(doc)
            .OfTags().WhereElementIsNotElementType().InView(view.Id)
            .ToList<IndependentTag>();

        int deductions = 0;
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
                    deductions += 5;
            }
        }

        return Math.Max(0, 100 - deductions);
    }

    private static BuiltInCategory? ResolveCategoryByName(string name)
    {
        var n = name.ToLowerInvariant().Replace(" ", "");
        return n switch
        {
            "ducts" or "duct" => BuiltInCategory.OST_DuctCurves,
            "pipes" or "pipe" => BuiltInCategory.OST_PipeCurves,
            "equipment" or "mechanicalequipment" => BuiltInCategory.OST_MechanicalEquipment,
            "cabletray" => BuiltInCategory.OST_CableTray,
            "conduit" => BuiltInCategory.OST_Conduit,
            "sprinklers" => BuiltInCategory.OST_Sprinklers,
            "plumbingfixtures" => BuiltInCategory.OST_PlumbingFixtures,
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
