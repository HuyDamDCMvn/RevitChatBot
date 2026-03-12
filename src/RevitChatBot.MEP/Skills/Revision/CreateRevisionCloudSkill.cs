using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Revision;

[Skill("create_revision_cloud",
    "Create a revision cloud in the active view around specified elements or at given coordinates. " +
    "Automatically assigns the latest or specified revision. Use for marking areas of change.")]
[SkillParameter("around_element_ids", "string",
    "Comma-separated element IDs to draw the cloud around (auto-sizes). Optional.",
    isRequired: false)]
[SkillParameter("min_x_mm", "number", "Manual region: min X in mm.", isRequired: false)]
[SkillParameter("min_y_mm", "number", "Manual region: min Y in mm.", isRequired: false)]
[SkillParameter("max_x_mm", "number", "Manual region: max X in mm.", isRequired: false)]
[SkillParameter("max_y_mm", "number", "Manual region: max Y in mm.", isRequired: false)]
[SkillParameter("revision_number", "string",
    "Revision number or name to assign. If omitted, uses the latest revision.",
    isRequired: false)]
[SkillParameter("comments", "string",
    "Comment text for the revision cloud. Optional.",
    isRequired: false)]
[SkillParameter("padding_mm", "number",
    "Extra padding around elements in mm. Default: 500.",
    isRequired: false)]
public class CreateRevisionCloudSkill : ISkill
{
    private const double MmToFeet = 1.0 / 304.8;

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var aroundIds = parameters.GetValueOrDefault("around_element_ids")?.ToString();
        var revNumberStr = parameters.GetValueOrDefault("revision_number")?.ToString();
        var comments = parameters.GetValueOrDefault("comments")?.ToString();
        var paddingFt = Convert.ToDouble(parameters.GetValueOrDefault("padding_mm") ?? 500) * MmToFeet;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var view = document.ActiveView;
            if (view is null)
                return new { status = "error", message = "No active view.", cloudId = -1L };

            var revisions = Autodesk.Revit.DB.Revision.GetAllRevisionIds(document)
                .Select(id => document.GetElement(id) as Autodesk.Revit.DB.Revision)
                .Where(r => r is not null)
                .ToList();

            if (revisions.Count == 0)
                return new { status = "error", message = "No revisions in the project. Create one first with manage_revisions.", cloudId = -1L };

            Autodesk.Revit.DB.Revision? targetRevision = null;
            if (!string.IsNullOrWhiteSpace(revNumberStr))
            {
                targetRevision = revisions.FirstOrDefault(r =>
                    r!.SequenceNumber.ToString() == revNumberStr ||
                    r.RevisionNumber == revNumberStr ||
                    r.Description?.Contains(revNumberStr, StringComparison.OrdinalIgnoreCase) == true);
            }
            targetRevision ??= revisions.Last();

            BoundingBoxXYZ region;
            if (!string.IsNullOrWhiteSpace(aroundIds))
            {
                var elems = aroundIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => document.GetElement(new ElementId(long.Parse(s.Trim()))))
                    .Where(e => e is not null)
                    .ToList();

                if (elems.Count == 0)
                    return new { status = "error", message = "No valid elements found.", cloudId = -1L };

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var elem in elems)
                {
                    var bb = elem!.get_BoundingBox(view) ?? elem.get_BoundingBox(null);
                    if (bb is null) continue;
                    minX = Math.Min(minX, bb.Min.X);
                    minY = Math.Min(minY, bb.Min.Y);
                    maxX = Math.Max(maxX, bb.Max.X);
                    maxY = Math.Max(maxY, bb.Max.Y);
                }

                region = new BoundingBoxXYZ
                {
                    Min = new XYZ(minX - paddingFt, minY - paddingFt, 0),
                    Max = new XYZ(maxX + paddingFt, maxY + paddingFt, 0)
                };
            }
            else
            {
                var minX = Convert.ToDouble(parameters.GetValueOrDefault("min_x_mm") ?? 0) * MmToFeet;
                var minY = Convert.ToDouble(parameters.GetValueOrDefault("min_y_mm") ?? 0) * MmToFeet;
                var maxX = Convert.ToDouble(parameters.GetValueOrDefault("max_x_mm") ?? 5000) * MmToFeet;
                var maxY = Convert.ToDouble(parameters.GetValueOrDefault("max_y_mm") ?? 5000) * MmToFeet;
                region = new BoundingBoxXYZ
                {
                    Min = new XYZ(minX, minY, 0),
                    Max = new XYZ(maxX, maxY, 0)
                };
            }

            using var tx = new Transaction(document, "Create revision cloud");
            tx.Start();
            try
            {
                var curves = new List<Curve>
                {
                    Line.CreateBound(new XYZ(region.Min.X, region.Min.Y, 0), new XYZ(region.Max.X, region.Min.Y, 0)),
                    Line.CreateBound(new XYZ(region.Max.X, region.Min.Y, 0), new XYZ(region.Max.X, region.Max.Y, 0)),
                    Line.CreateBound(new XYZ(region.Max.X, region.Max.Y, 0), new XYZ(region.Min.X, region.Max.Y, 0)),
                    Line.CreateBound(new XYZ(region.Min.X, region.Max.Y, 0), new XYZ(region.Min.X, region.Min.Y, 0)),
                };

                var cloud = RevisionCloud.Create(document, view, targetRevision!.Id, curves);

                if (!string.IsNullOrWhiteSpace(comments))
                {
                    var commentParam = cloud.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    commentParam?.Set(comments!);
                }

                tx.Commit();
                return new
                {
                    status = "ok",
                    message = $"Revision cloud created in '{view.Name}' for revision '{targetRevision.RevisionNumber}'.",
                    cloudId = cloud.Id.Value
                };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { status = "error", message = ex.Message, cloudId = -1L };
            }
        });

        dynamic res = result!;
        return res.status == "ok"
            ? SkillResult.Ok(res.message, result)
            : SkillResult.Fail(res.message);
    }
}
