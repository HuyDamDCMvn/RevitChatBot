using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;
using RevitChatBot.RevitServices.Annotation;

namespace RevitChatBot.MEP.Skills.Annotation;

/// <summary>
/// Preview tag positions using DirectContext3D visualization before committing.
/// Shows ghost bounding boxes and leader lines at proposed positions so the
/// user can verify the layout before actually placing/arranging tags.
/// </summary>
[Skill("preview_annotation",
    "Preview proposed tag positions as colored overlays before committing changes. " +
    "Shows where tags would be placed or moved to using transparent boxes. " +
    "User can approve and apply, or cancel. Uses the visualization overlay system.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view", isRequired: true)]
[SkillParameter("category", "string",
    "Category to preview tags for: 'Ducts', 'Pipes', 'Equipment', 'all'",
    isRequired: false)]
[SkillParameter("action", "string",
    "Preview action: 'place' (show where new tags would go), " +
    "'arrange' (show where existing tags would move). Default 'place'.",
    isRequired: false,
    allowedValues: new[] { "place", "arrange" })]
public class AnnotationPreviewSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "place";

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required.");

        var vizManager = context.VisualizationManager;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", previewCount = 0, positions = new List<object>() };

            var previewPositions = new List<object>();

            if (action == "place")
            {
                previewPositions = PreviewPlacement(document, view, categoryStr);
            }
            else
            {
                previewPositions = PreviewArrangement(document, view, categoryStr);
            }

            // If VisualizationManager is available, draw preview boxes
            if (vizManager is not null)
            {
                try
                {
                    dynamic vm = vizManager;
                    vm.ClearByTag("annotation_preview");

                    foreach (dynamic pos in previewPositions)
                    {
                        double x = (double)pos.x;
                        double y = (double)pos.y;
                        double w = (double)pos.width;
                        double h = (double)pos.height;

                        var min = new XYZ(x - w / 2, y - h / 2, 0);
                        var max = new XYZ(x + w / 2, y + h / 2, 0.01);

                        vm.AddBoundingBox(min, max, "annotation_preview",
                            new { Color = new byte[] { 0, 150, 255 }, Transparency = 128 });
                    }

                    vm.RefreshViews();
                }
                catch { }
            }

            return new
            {
                success = true,
                message = $"Preview generated: {previewPositions.Count} tag positions " +
                    $"({action} mode for {categoryStr}). " +
                    (vizManager is not null
                        ? "Overlay shown in view — approve by running the actual command."
                        : "Visualization not available — positions returned as data."),
                previewCount = previewPositions.Count,
                positions = previewPositions
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static List<object> PreviewPlacement(Document doc, View view, string categoryStr)
    {
        var categories = categoryStr.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? new List<BuiltInCategory> {
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_MechanicalEquipment }
            : new List<BuiltInCategory> { ResolveCat(categoryStr) ?? BuiltInCategory.OST_DuctCurves };

        var existingTags = new FluentCollector(doc)
            .OfTags().WhereElementIsNotElementType().InView(view.Id)
            .ToList<IndependentTag>();

        var taggedIds = new HashSet<long>();
        foreach (var tag in existingTags)
            foreach (var id in tag.GetTaggedLocalElementIds())
                taggedIds.Add(id.Value);

        var obstacleMap = ViewObstacleMap.Build(doc, view);
        var scorer = new TagPositionScorer(obstacleMap);
        var positions = new List<object>();

        foreach (var cat in categories)
        {
            var elems = new FluentCollector(doc)
                .OfCategory(cat).WhereElementIsNotElementType().InView(view.Id)
                .ToList()
                .Where(e => !taggedIds.Contains(e.Id.Value));

            foreach (var elem in elems)
            {
                var center = elem.GetCenter();
                if (center is null) continue;

                var (bx, by, score) = scorer.FindBestPosition(
                    center.X, center.Y, 0.4, 0.15, 2.5, PreferredZone.Auto, cat);

                positions.Add(new
                {
                    elementId = elem.Id.Value,
                    x = bx, y = by,
                    width = 0.4, height = 0.15,
                    score,
                    type = "new_tag"
                });

                scorer.RegisterPlacedTag(bx - 0.2, by - 0.075, bx + 0.2, by + 0.075, cat);
            }
        }

        return positions;
    }

    private static List<object> PreviewArrangement(Document doc, View view, string categoryStr)
    {
        var tags = new FluentCollector(doc)
            .OfTags().WhereElementIsNotElementType().InView(view.Id)
            .ToList<IndependentTag>();

        if (tags.Count < 2) return [];

        var tagIds = tags.Select(t => t.Id.Value).ToHashSet();
        var obstacleMap = ViewObstacleMap.Build(doc, view, tagIds);

        var inputs = tags.Select(t =>
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

        var arranger = new ForceDirectedTagArranger(obstacleMap);
        var results = arranger.Arrange(inputs);

        return results.Where(r => r.WasMoved).Select(r => (object)new
        {
            elementId = r.Tag.Id.Value,
            x = r.NewX, y = r.NewY,
            width = inputs.First(i => i.Tag.Id == r.Tag.Id).Width,
            height = inputs.First(i => i.Tag.Id == r.Tag.Id).Height,
            fromX = inputs.First(i => i.Tag.Id == r.Tag.Id).CurrentX,
            fromY = inputs.First(i => i.Tag.Id == r.Tag.Id).CurrentY,
            type = "move_tag"
        }).ToList();
    }

    private static BuiltInCategory? ResolveCat(string name)
    {
        var n = name.ToLowerInvariant().Replace(" ", "");
        return n switch
        {
            "ducts" or "duct" => BuiltInCategory.OST_DuctCurves,
            "pipes" or "pipe" => BuiltInCategory.OST_PipeCurves,
            "equipment" => BuiltInCategory.OST_MechanicalEquipment,
            _ => null
        };
    }
}
