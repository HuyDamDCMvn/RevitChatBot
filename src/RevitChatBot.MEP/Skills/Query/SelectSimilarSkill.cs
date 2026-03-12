using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Related: advanced_filter (different scope — this matches based on current selection)
/// </summary>
[Skill("select_similar",
    "Select all elements similar to the current selection. " +
    "Matches by type, family, or category. Uses isolate mode by default; " +
    "use select_mode='select' to highlight in Revit selection.")]
[SkillParameter("match_by", "string",
    "Match criteria: 'type' (same type), 'family' (same family), 'category' (same category).",
    isRequired: false, allowedValues: new[] { "type", "family", "category" })]
[SkillParameter("scope", "string", "'active_view' or 'entire_model'.",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
[SkillParameter("select_mode", "string",
    "'isolate' to temporarily isolate in view, 'select' to set Revit selection (requires UI access).",
    isRequired: false, allowedValues: new[] { "isolate", "select" })]
public class SelectSimilarSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var selectionIds = context.GetCurrentSelectionIds();
        if (selectionIds is null || selectionIds.Count == 0)
            return SkillResult.Fail("No elements selected. Please select an element first.");

        var matchBy = parameters.GetValueOrDefault("match_by")?.ToString() ?? "type";
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);
        var selectMode = parameters.GetValueOrDefault("select_mode")?.ToString() ?? "isolate";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var sourceElem = document.GetElement(new ElementId(selectionIds[0]));
            if (sourceElem is null)
                return new { error = "Source element not found.", count = 0 };

            var collector = ViewScopeHelper.CreateFluent(document, scope)
                .WhereElementIsNotElementType();

            if (sourceElem.Category is not null)
                collector.OfCategory(sourceElem.Category.BuiltInCategory);

            var allElements = collector.ToList();

            List<Element> matched = matchBy switch
            {
                "type" => allElements.Where(e => e.GetTypeId() == sourceElem.GetTypeId()).ToList(),
                "family" => sourceElem is FamilyInstance fi
                    ? allElements.Where(e => e is FamilyInstance f &&
                        f.Symbol?.Family?.Id == fi.Symbol?.Family?.Id).ToList()
                    : allElements.Where(e => e.GetTypeId() == sourceElem.GetTypeId()).ToList(),
                "category" => allElements,
                _ => allElements.Where(e => e.GetTypeId() == sourceElem.GetTypeId()).ToList()
            };

            var matchedIds = matched.Select(e => e.Id).ToList();
            var allMatchedIds = matchedIds.Select(id => id.Value).ToList();
            bool isolated = false;

            if (selectMode == "isolate" && matchedIds.Count > 0)
            {
                try
                {
                    var view = document.ActiveView;
                    if (view is not null)
                    {
                        using var tx = new Transaction(document, "Select Similar - Isolate");
                        tx.Start();
                        view.IsolateElementsTemporary(matchedIds.ToList());
                        tx.Commit();
                        isolated = true;
                    }
                }
                catch { }
            }

            return new
            {
                error = (string?)null,
                count = matched.Count,
                matchBy,
                sourceElement = new { id = sourceElem.Id.Value, name = sourceElem.Name, type = sourceElem.GetTypeId().Value },
                isolated,
                sampleIds = matchedIds.Take(20).Select(id => id.Value).ToList(),
                allMatchedIds
            };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);

        if (selectMode == "select" && context.RevitUIInvoker is not null)
        {
            try
            {
                var idsToSelect = (data?.allMatchedIds as System.Collections.IEnumerable)?.Cast<long>().ToList()
                    ?? (data?.sampleIds as System.Collections.IEnumerable)?.Cast<long>().ToList()
                    ?? [];
                if (idsToSelect.Count > 0)
                {
                    await context.RevitUIInvoker(uiDocObj =>
                    {
                        dynamic uiDoc = uiDocObj;
                        uiDoc.Selection.SetElementIds(idsToSelect.Select(id => new ElementId(id)).ToList());
                        return (object?)null;
                    });
                }
            }
            catch { }
        }

        return SkillResult.Ok($"Found {data?.count} similar elements (matched by {matchBy}).", result);
    }
}
