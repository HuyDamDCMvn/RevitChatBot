using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Finds tags, dimensions, and viewports referencing a given element.
/// Note: Revit API does not support finding all generic constraints — only tags and dimensions.
/// </summary>
[Skill("find_referencing_elements",
    "Find tags, dimensions, and other annotations that reference a specific element. " +
    "Note: generic constraints cannot be found via the Revit API.")]
[SkillParameter("element_id", "string", "Element ID to find references for.", isRequired: false)]
[SkillParameter("source", "string", "'selected' to use current selection.", isRequired: false,
    allowedValues: new[] { "selected", "id" })]
[SkillParameter("scope", "string", "'active_view' or 'entire_model'.", isRequired: false,
    allowedValues: new[] { "active_view", "entire_model" })]
public class FindReferencingElementsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var elemIdStr = parameters.GetValueOrDefault("element_id")?.ToString();
        long targetId;
        if (!string.IsNullOrWhiteSpace(elemIdStr) && long.TryParse(elemIdStr, out var parsed))
            targetId = parsed;
        else
        {
            var sel = context.GetCurrentSelectionIds();
            if (sel is null || sel.Count == 0)
                return SkillResult.Fail("No element specified.");
            targetId = sel[0];
        }

        var scope = parameters.GetValueOrDefault("scope")?.ToString() ?? "active_view";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementId = new ElementId(targetId);
            var element = document.GetElement(elementId);
            if (element is null) return new { error = $"Element {targetId} not found." };

            var tags = new List<object>();
            var dimensions = new List<object>();

            FilteredElementCollector CreateScopedCollector()
            {
                if (scope == "active_view" && document.ActiveView is { } av)
                    return new FilteredElementCollector(document, av.Id);
                return new FilteredElementCollector(document);
            }

            // Find tags referencing this element
            try
            {
                foreach (var tag in CreateScopedCollector().OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                {
                    try
                    {
                        var taggedIds = tag.GetTaggedLocalElementIds();
                        if (taggedIds.Any(id => id.Value == targetId))
                        {
                            tags.Add(new
                            {
                                tagId = tag.Id.Value,
                                tagType = tag.Name,
                                viewName = (document.GetElement(tag.OwnerViewId) as View)?.Name ?? "N/A"
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Find dimensions referencing this element
            try
            {
                foreach (var dim in CreateScopedCollector().OfClass(typeof(Dimension)).Cast<Dimension>())
                {
                    try
                    {
                        var refs = dim.References;
                        if (refs is null) continue;
                        foreach (Reference r in refs)
                        {
                            if (r.ElementId.Value == targetId)
                            {
                                dimensions.Add(new
                                {
                                    dimensionId = dim.Id.Value,
                                    dimensionType = dim.DimensionType?.Name ?? "Unknown",
                                    value = dim.ValueString ?? "N/A"
                                });
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return new
            {
                error = (string?)null,
                elementName = element.Name,
                elementId = targetId,
                tagCount = tags.Count,
                dimensionCount = dimensions.Count,
                tags = tags.Take(30).ToList(),
                dimensions = dimensions.Take(30).ToList(),
                apiLimitation = "Generic constraints (alignment, locking) cannot be queried via Revit API."
            };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);
        return SkillResult.Ok(
            $"Found {data?.tagCount} tags and {data?.dimensionCount} dimensions referencing '{data?.elementName}'.",
            result);
    }
}
