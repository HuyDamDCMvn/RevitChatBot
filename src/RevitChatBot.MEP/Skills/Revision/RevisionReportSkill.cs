using System.Text;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Revision;

[Skill("revision_report",
    "Generate a revision report as CSV listing all revisions and their associated sheets. " +
    "Saves to ChatBot_Exports folder.")]
[SkillParameter("format", "string",
    "Output format: 'csv' or 'summary'. Default 'summary'.",
    isRequired: false, allowedValues: new[] { "csv", "summary" })]
public class RevisionReportSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var format = parameters.GetValueOrDefault("format")?.ToString() ?? "summary";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var revisions = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Revisions)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Revision>()
                .OrderBy(r => r.SequenceNumber)
                .ToList();

            var sheets = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            var rows = new List<object>();
            foreach (var rev in revisions)
            {
                var sheetsWithRev = sheets
                    .Where(s => s.GetAllRevisionIds().Contains(rev.Id))
                    .Select(s => s.SheetNumber)
                    .OrderBy(n => n)
                    .ToList();

                rows.Add(new
                {
                    sequenceNumber = rev.SequenceNumber,
                    description = rev.Description,
                    date = rev.RevisionDate,
                    issued = rev.Issued,
                    sheetCount = sheetsWithRev.Count,
                    sheets = sheetsWithRev
                });
            }

            string? filePath = null;
            if (format == "csv")
            {
                var projectDir = Path.GetDirectoryName(document.PathName);
                if (string.IsNullOrWhiteSpace(projectDir))
                    projectDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                var exportDir = Path.Combine(projectDir, "ChatBot_Exports");
                Directory.CreateDirectory(exportDir);
                filePath = Path.Combine(exportDir, $"RevisionReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var csv = new StringBuilder();
                csv.AppendLine("Revision#,Description,Date,Issued,SheetNumber");
                foreach (var rev in revisions)
                {
                    var sheetsWithRev = sheets
                        .Where(s => s.GetAllRevisionIds().Contains(rev.Id))
                        .ToList();

                    if (sheetsWithRev.Count == 0)
                    {
                        csv.AppendLine($"{rev.SequenceNumber},\"{rev.Description}\",{rev.RevisionDate},{rev.Issued},");
                    }
                    else
                    {
                        foreach (var s in sheetsWithRev)
                            csv.AppendLine($"{rev.SequenceNumber},\"{rev.Description}\",{rev.RevisionDate},{rev.Issued},{s.SheetNumber}");
                    }
                }
                File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
            }

            return new
            {
                totalRevisions = revisions.Count,
                totalSheets = sheets.Count,
                revisions = rows,
                filePath
            };
        });

        var data = result as dynamic;
        var msg = $"Revision report: {data?.totalRevisions} revisions across {data?.totalSheets} sheets.";
        if (data?.filePath is string fp)
            msg += $"\nExported to: {fp}";

        return SkillResult.Ok(msg, result);
    }
}
