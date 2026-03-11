using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("tag_section_callouts",
    "Automatically number and organize section marks, callout marks, and " +
    "elevation marks in a view or on a sheet. Assigns sequential numbers " +
    "using a specified pattern and ensures consistent positioning.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view or sheet", isRequired: true)]
[SkillParameter("numbering_pattern", "string",
    "Numbering pattern: 'numeric' (1,2,3...), 'alpha' (A,B,C...), " +
    "or a prefix like 'S-' for S-1, S-2, etc. Default 'numeric'.",
    isRequired: false)]
[SkillParameter("reset_numbering", "string",
    "Reset numbering from 1/A. 'true' or 'false' (default 'true').",
    isRequired: false)]
public class SectionCalloutTaggingSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var pattern = parameters.GetValueOrDefault("numbering_pattern")?.ToString() ?? "numeric";
        var resetStr = parameters.GetValueOrDefault("reset_numbering")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required.");

        bool reset = !string.Equals(resetStr, "false", StringComparison.OrdinalIgnoreCase);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", numbered = 0 };

            // Collect section/callout/elevation views referenced from this view
            var viewports = new List<Viewport>();
            if (view is ViewSheet sheet)
            {
                viewports = sheet.GetAllViewports()
                    .Select(id => document.GetElement(id))
                    .OfType<Viewport>()
                    .ToList();
            }

            // Collect section marks in the view
            var sectionViews = new FilteredElementCollector(document, view.Id)
                .OfCategory(BuiltInCategory.OST_Viewers)
                .WhereElementIsNotElementType()
                .ToList();

            var callouts = new FilteredElementCollector(document, view.Id)
                .OfCategory(BuiltInCategory.OST_Callouts)
                .WhereElementIsNotElementType()
                .ToList();

            var allMarkers = new List<Element>();
            allMarkers.AddRange(sectionViews);
            allMarkers.AddRange(callouts);

            // Sort by position (left to right, top to bottom)
            allMarkers = allMarkers
                .OrderBy(e =>
                {
                    var bb = e.get_BoundingBox(view);
                    return bb?.Min.X ?? 0;
                })
                .ThenByDescending(e =>
                {
                    var bb = e.get_BoundingBox(view);
                    return bb?.Min.Y ?? 0;
                })
                .ToList();

            if (allMarkers.Count == 0 && viewports.Count == 0)
                return new { success = true, message = "No section marks or callouts found.", numbered = 0 };

            using var tx = new Transaction(document, "Number sections/callouts");
            tx.Start();

            int numbered = 0;
            int counter = reset ? 1 : GetNextNumber(allMarkers, document);

            foreach (var marker in allMarkers)
            {
                try
                {
                    string number = GenerateNumber(counter, pattern);

                    var detailParam = marker.get_Parameter(BuiltInParameter.VIEWER_DETAIL_NUMBER);
                    if (detailParam is not null && !detailParam.IsReadOnly)
                    {
                        detailParam.Set(number);
                        numbered++;
                        counter++;
                    }
                }
                catch { }
            }

            // Number viewports on sheet
            foreach (var vp in viewports.OrderBy(v => v.GetBoxCenter().X))
            {
                try
                {
                    string number = GenerateNumber(counter, pattern);
                    var detailParam = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                    if (detailParam is not null && !detailParam.IsReadOnly)
                    {
                        detailParam.Set(number);
                        numbered++;
                        counter++;
                    }
                }
                catch { }
            }

            tx.Commit();

            return new
            {
                success = true,
                message = $"Numbered {numbered} section marks, callouts, and viewports " +
                    $"(pattern: {pattern}, {sectionViews.Count} sections, {callouts.Count} callouts, " +
                    $"{viewports.Count} viewports).",
                numbered
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static string GenerateNumber(int index, string pattern)
    {
        var normalized = pattern.ToLowerInvariant();
        if (normalized == "alpha")
        {
            if (index <= 26) return ((char)('A' + index - 1)).ToString();
            return $"A{index - 26}";
        }

        if (normalized == "numeric")
            return index.ToString();

        // Prefix pattern like "S-"
        return $"{pattern}{index}";
    }

    private static int GetNextNumber(List<Element> markers, Document doc)
    {
        int max = 0;
        foreach (var m in markers)
        {
            var p = m.get_Parameter(BuiltInParameter.VIEWER_DETAIL_NUMBER);
            if (p?.AsString() is string s && int.TryParse(s, out int n) && n > max)
                max = n;
        }
        return max + 1;
    }
}
