using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Revision;

[Skill("find_revision_clouds",
    "Find revision clouds in the model. Lists their locations, associated revisions, and views/sheets. " +
    "Note: clouds in Legend views do not appear in revision schedules.")]
[SkillParameter("revision_number", "string",
    "Filter by revision sequence number.", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model'.",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
[SkillParameter("max_results", "integer", "Max results. Default 50.", isRequired: false)]
public class FindRevisionCloudsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var revNumStr = parameters.GetValueOrDefault("revision_number")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);
        var maxResults = 50;
        if (parameters.TryGetValue("max_results", out var mr) && mr is not null)
            int.TryParse(mr.ToString(), out maxResults);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var collector = ViewScopeHelper.CreateCollector(document, scope)
                .OfCategory(BuiltInCategory.OST_RevisionClouds)
                .WhereElementIsNotElementType();

            var clouds = collector.ToList();

            int? targetRevNum = null;
            if (!string.IsNullOrWhiteSpace(revNumStr) && int.TryParse(revNumStr, out var rn))
                targetRevNum = rn;

            var items = new List<object>();
            foreach (var cloud in clouds)
            {
                if (cloud is not RevisionCloud rc) continue;

                var rev = document.GetElement(rc.RevisionId) as Autodesk.Revit.DB.Revision;
                if (targetRevNum.HasValue && rev?.SequenceNumber != targetRevNum.Value) continue;

                var ownerView = document.GetElement(rc.OwnerViewId) as View;
                var sheetIds = rc.GetSheetIds();
                var sheetNumbers = sheetIds
                    .Select(sid => document.GetElement(sid) as ViewSheet)
                    .Where(s => s is not null)
                    .Select(s => s!.SheetNumber)
                    .ToList();

                items.Add(new
                {
                    id = rc.Id.Value,
                    revisionNumber = rev?.SequenceNumber,
                    revisionDescription = rev?.Description ?? "N/A",
                    viewName = ownerView?.Name ?? "N/A",
                    viewType = ownerView?.ViewType.ToString() ?? "N/A",
                    sheets = sheetNumbers
                });

                if (items.Count >= maxResults) break;
            }

            return new
            {
                totalClouds = clouds.Count,
                returned = items.Count,
                clouds = items
            };
        });

        var data = result as dynamic;
        return SkillResult.Ok($"Found {data?.totalClouds} revision clouds.", result);
    }
}
