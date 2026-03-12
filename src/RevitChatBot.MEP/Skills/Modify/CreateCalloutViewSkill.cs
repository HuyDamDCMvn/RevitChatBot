using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("create_callout_view",
    "Create a callout or dependent view from an existing view. Callouts create an enlarged " +
    "detail of a region. Dependent views share the same base view but with independent crop. " +
    "Supports auto-sizing around elements or manual coordinates.")]
[SkillParameter("source_view_id", "string",
    "ID of the parent view to create callout/dependent from. " +
    "If omitted, uses the active view.",
    isRequired: false)]
[SkillParameter("mode", "string",
    "'callout' to create a callout view, 'dependent' to create a dependent view. Default: 'callout'.",
    isRequired: false,
    allowedValues: new[] { "callout", "dependent" })]
[SkillParameter("around_element_id", "string",
    "Create the callout region around this element (auto-sizes). Optional.",
    isRequired: false)]
[SkillParameter("min_x_mm", "number", "Manual region: min X in mm.", isRequired: false)]
[SkillParameter("min_y_mm", "number", "Manual region: min Y in mm.", isRequired: false)]
[SkillParameter("max_x_mm", "number", "Manual region: max X in mm.", isRequired: false)]
[SkillParameter("max_y_mm", "number", "Manual region: max Y in mm.", isRequired: false)]
[SkillParameter("padding_mm", "number",
    "Extra padding around the element in mm. Default: 1000.",
    isRequired: false)]
[SkillParameter("view_name", "string",
    "Custom name for the new view. Optional.",
    isRequired: false)]
public class CreateCalloutViewSkill : ISkill
{
    private const double MmToFeet = 1.0 / 304.8;

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var mode = parameters.GetValueOrDefault("mode")?.ToString()?.ToLower() ?? "callout";
        var sourceViewIdStr = parameters.GetValueOrDefault("source_view_id")?.ToString();
        var aroundElemIdStr = parameters.GetValueOrDefault("around_element_id")?.ToString();
        var paddingMm = Convert.ToDouble(parameters.GetValueOrDefault("padding_mm") ?? 1000);
        var paddingFt = paddingMm * MmToFeet;
        var customName = parameters.GetValueOrDefault("view_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            View? sourceView = null;
            if (!string.IsNullOrWhiteSpace(sourceViewIdStr) && long.TryParse(sourceViewIdStr, out var svid))
                sourceView = document.GetElement(new ElementId(svid)) as View;
            sourceView ??= document.ActiveView;

            if (sourceView is null)
                return new { status = "error", message = "No source view available.", newViewId = -1L, viewName = "" };

            if (mode == "dependent")
                return CreateDependentView(document, sourceView, aroundElemIdStr, paddingFt, customName);

            return CreateCallout(document, sourceView, aroundElemIdStr, paddingFt, customName, parameters);
        });

        dynamic res = result!;
        return res.status == "ok"
            ? SkillResult.Ok(res.message, result)
            : SkillResult.Fail(res.message);
    }

    private static object CreateDependentView(Document doc, View parent, string? aroundElemId, double padding, string? name)
    {
        using var tx = new Transaction(doc, "Create dependent view");
        tx.Start();
        try
        {
            var newViewId = parent.Duplicate(ViewDuplicateOption.AsDependent);
            var newView = doc.GetElement(newViewId) as View;

            if (newView is not null && !string.IsNullOrWhiteSpace(name))
            {
                try { newView.Name = name!; } catch { }
            }

            if (newView is not null && !string.IsNullOrWhiteSpace(aroundElemId) && long.TryParse(aroundElemId, out var eid))
            {
                var elem = doc.GetElement(new ElementId(eid));
                if (elem is not null)
                {
                    var bb = elem.get_BoundingBox(parent) ?? elem.get_BoundingBox(null);
                    if (bb is not null)
                    {
                        var crop = newView.GetCropRegionShapeManager();
                        var min = new XYZ(bb.Min.X - padding, bb.Min.Y - padding, 0);
                        var max = new XYZ(bb.Max.X + padding, bb.Max.Y + padding, 0);
                        var lines = new List<Curve>
                        {
                            Line.CreateBound(new XYZ(min.X, min.Y, 0), new XYZ(max.X, min.Y, 0)),
                            Line.CreateBound(new XYZ(max.X, min.Y, 0), new XYZ(max.X, max.Y, 0)),
                            Line.CreateBound(new XYZ(max.X, max.Y, 0), new XYZ(min.X, max.Y, 0)),
                            Line.CreateBound(new XYZ(min.X, max.Y, 0), new XYZ(min.X, min.Y, 0)),
                        };
                        var loop = CurveLoop.Create(lines);
                        crop.SetCropShape(loop);
                        newView.CropBoxActive = true;
                    }
                }
            }

            tx.Commit();
            return new
            {
                status = "ok",
                message = $"Dependent view '{newView?.Name ?? ""}' created from '{parent.Name}'.",
                newViewId = newViewId.Value,
                viewName = newView?.Name ?? ""
            };
        }
        catch (Exception ex)
        {
            if (tx.HasStarted()) tx.RollBack();
            return new { status = "error", message = ex.Message, newViewId = -1L, viewName = "" };
        }
    }

    private static object CreateCallout(Document doc, View parent, string? aroundElemId, double padding, string? name, Dictionary<string, object?> parameters)
    {
        BoundingBoxXYZ? region = null;

        if (!string.IsNullOrWhiteSpace(aroundElemId) && long.TryParse(aroundElemId, out var eid))
        {
            var elem = doc.GetElement(new ElementId(eid));
            if (elem is not null)
            {
                var bb = elem.get_BoundingBox(parent) ?? elem.get_BoundingBox(null);
                if (bb is not null)
                {
                    region = new BoundingBoxXYZ
                    {
                        Min = new XYZ(bb.Min.X - padding, bb.Min.Y - padding, bb.Min.Z),
                        Max = new XYZ(bb.Max.X + padding, bb.Max.Y + padding, bb.Max.Z)
                    };
                }
            }
        }
        else
        {
            var minX = Convert.ToDouble(parameters.GetValueOrDefault("min_x_mm") ?? 0) * MmToFeet;
            var minY = Convert.ToDouble(parameters.GetValueOrDefault("min_y_mm") ?? 0) * MmToFeet;
            var maxX = Convert.ToDouble(parameters.GetValueOrDefault("max_x_mm") ?? 10000) * MmToFeet;
            var maxY = Convert.ToDouble(parameters.GetValueOrDefault("max_y_mm") ?? 10000) * MmToFeet;

            region = new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, -100),
                Max = new XYZ(maxX, maxY, 100)
            };
        }

        if (region is null)
            return new { status = "error", message = "Cannot determine callout region.", newViewId = -1L, viewName = "" };

        using var tx = new Transaction(doc, "Create callout");
        tx.Start();
        try
        {
            var callout = ViewSection.CreateCallout(
                doc, parent.Id, parent.GetTypeId(),
                region.Min, region.Max);

            if (callout is not null && !string.IsNullOrWhiteSpace(name))
            {
                try { callout.Name = name!; } catch { }
            }

            tx.Commit();
            return new
            {
                status = "ok",
                message = $"Callout view '{callout?.Name ?? ""}' created from '{parent.Name}'.",
                newViewId = callout?.Id.Value ?? -1L,
                viewName = callout?.Name ?? ""
            };
        }
        catch (Exception ex)
        {
            if (tx.HasStarted()) tx.RollBack();
            return new { status = "error", message = ex.Message, newViewId = -1L, viewName = "" };
        }
    }
}
