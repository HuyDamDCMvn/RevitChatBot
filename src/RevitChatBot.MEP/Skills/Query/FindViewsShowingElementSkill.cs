using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("find_views_showing_element",
    "Find all views and sheets where a specific element appears. " +
    "Checks plan, section, elevation, and 3D views (skips schedules and legends for performance).")]
[SkillParameter("element_id", "string", "Element ID to search for.", isRequired: false)]
[SkillParameter("source", "string", "'selected' to use current selection.", isRequired: false,
    allowedValues: new[] { "selected", "id" })]
[SkillParameter("max_views", "integer", "Max views to check. Default 100.", isRequired: false)]
public class FindViewsShowingElementSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var elemIdStr = parameters.GetValueOrDefault("element_id")?.ToString();
        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "selected";
        var maxViews = 100;
        if (parameters.TryGetValue("max_views", out var mv) && mv is not null)
            int.TryParse(mv.ToString(), out maxViews);

        long targetId;
        if (!string.IsNullOrWhiteSpace(elemIdStr) && long.TryParse(elemIdStr, out var parsed))
            targetId = parsed;
        else
        {
            var sel = context.GetCurrentSelectionIds();
            if (sel is null || sel.Count == 0)
                return SkillResult.Fail("No element specified. Select an element or provide element_id.");
            targetId = sel[0];
        }

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementId = new ElementId(targetId);
            var element = document.GetElement(elementId);
            if (element is null) return new { error = $"Element {targetId} not found.", views = Array.Empty<object>(), checkedViews = 0 };

            var views = new FilteredElementCollector(document)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v is not ViewSchedule
                    && v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan
                       or ViewType.Section or ViewType.Elevation or ViewType.ThreeD
                       or ViewType.EngineeringPlan or ViewType.AreaPlan or ViewType.Detail)
                .Take(maxViews)
                .ToList();

            var foundViews = new List<object>();
            int checked_ = 0;

            foreach (var view in views)
            {
                checked_++;
                try
                {
                    var collector = new FilteredElementCollector(document, view.Id);
                    if (collector.Any(e => e.Id == elementId))
                    {
                        string? sheetNumber = null;
                        var viewports = new FilteredElementCollector(document)
                            .OfClass(typeof(Viewport)).Cast<Viewport>()
                            .Where(vp => vp.ViewId == view.Id).ToList();

                        if (viewports.Count > 0)
                        {
                            var sheet = document.GetElement(viewports[0].SheetId) as ViewSheet;
                            sheetNumber = sheet?.SheetNumber;
                        }

                        foundViews.Add(new
                        {
                            viewName = view.Name,
                            viewType = view.ViewType.ToString(),
                            sheetNumber,
                            viewId = view.Id.Value
                        });

                        if (foundViews.Count >= 20) break;
                    }
                }
                catch { }
            }

            return new
            {
                error = (string?)null,
                elementName = element.Name,
                elementId = targetId,
                checkedViews = checked_,
                foundCount = foundViews.Count,
                views = foundViews
            };
        });

        var data = result as dynamic;
        if (data?.error is string err2 && !string.IsNullOrEmpty(err2))
            return SkillResult.Fail(err2);
        return SkillResult.Ok($"Element '{data?.elementName}' found in {data?.foundCount} views ({data?.checkedViews} checked).", result);
    }
}
