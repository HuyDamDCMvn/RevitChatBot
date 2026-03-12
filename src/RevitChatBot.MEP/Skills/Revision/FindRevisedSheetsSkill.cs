using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Revision;

[Skill("find_revised_sheets",
    "Find sheets that have a specific revision or any revision assigned. " +
    "Helps track which sheets are part of a revision submission.")]
[SkillParameter("revision_description", "string",
    "Search by revision description (partial match).", isRequired: false)]
[SkillParameter("revision_number", "string",
    "Search by revision sequence number.", isRequired: false)]
[SkillParameter("any_revision", "string",
    "'true' to find all sheets with any revision. Default 'false'.",
    isRequired: false, allowedValues: new[] { "true", "false" })]
public class FindRevisedSheetsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var revDescription = parameters.GetValueOrDefault("revision_description")?.ToString();
        var revNumStr = parameters.GetValueOrDefault("revision_number")?.ToString();
        var anyRevision = parameters.GetValueOrDefault("any_revision")?.ToString()?.ToLower() == "true";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            ElementId? targetRevId = null;
            if (!anyRevision)
            {
                var revisions = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Revisions)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Revision>()
                    .ToList();

                if (!string.IsNullOrWhiteSpace(revNumStr) && int.TryParse(revNumStr, out var num))
                    targetRevId = revisions.FirstOrDefault(r => r.SequenceNumber == num)?.Id;
                else if (!string.IsNullOrWhiteSpace(revDescription))
                    targetRevId = revisions.FirstOrDefault(r =>
                        r.Description.Contains(revDescription, StringComparison.OrdinalIgnoreCase))?.Id;
                else
                    anyRevision = true;
            }

            var sheets = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            var matchedSheets = new List<object>();
            foreach (var sheet in sheets)
            {
                var revIds = sheet.GetAllRevisionIds();
                if (revIds.Count == 0) continue;

                if (!anyRevision && targetRevId is not null && !revIds.Contains(targetRevId))
                    continue;

                var revNames = revIds.Select(rid =>
                {
                    var rev = document.GetElement(rid) as Autodesk.Revit.DB.Revision;
                    return rev != null ? $"#{rev.SequenceNumber} {rev.Description}" : rid.Value.ToString();
                }).ToList();

                matchedSheets.Add(new
                {
                    sheetNumber = sheet.SheetNumber,
                    sheetName = sheet.Name,
                    revisionCount = revIds.Count,
                    revisions = revNames
                });
            }

            return new
            {
                totalSheets = sheets.Count,
                matchedCount = matchedSheets.Count,
                sheets = matchedSheets.OrderBy(s => ((dynamic)s).sheetNumber).ToList()
            };
        });

        var data = result as dynamic;
        return SkillResult.Ok($"Found {data?.matchedCount} sheets with revisions.", result);
    }
}
