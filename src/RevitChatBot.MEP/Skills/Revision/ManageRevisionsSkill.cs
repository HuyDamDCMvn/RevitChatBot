using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Revision;

[Skill("manage_revisions",
    "Create, list, or assign revisions on sheets. Supports listing all revisions, creating new ones, " +
    "and adding/removing revisions from sheets by sheet number.")]
[SkillParameter("action", "string",
    "Action: 'list' all revisions, 'create' a new revision, " +
    "'add_to_sheets' to assign a revision to sheets, 'remove_from_sheets' to unassign.",
    isRequired: true,
    allowedValues: new[] { "list", "create", "add_to_sheets", "remove_from_sheets" })]
[SkillParameter("description", "string", "Revision description (for 'create' action).", isRequired: false)]
[SkillParameter("date", "string", "Revision date string (for 'create' action).", isRequired: false)]
[SkillParameter("revision_number", "string",
    "Revision sequence number to identify the revision (for add/remove). " +
    "Use 'list' action first to find the number.", isRequired: false)]
[SkillParameter("sheet_numbers", "string",
    "Comma-separated sheet numbers to add/remove revision (e.g. 'M-01,M-02,M-03').", isRequired: false)]
public class ManageRevisionsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "list";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            return action switch
            {
                "list" => ListRevisions(document),
                "create" => CreateRevision(document, parameters),
                "add_to_sheets" => ModifySheetsRevision(document, parameters, add: true),
                "remove_from_sheets" => ModifySheetsRevision(document, parameters, add: false),
                _ => (object)new { error = $"Unknown action '{action}'." }
            };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);

        return SkillResult.Ok(data?.message?.ToString() ?? $"Revision action '{action}' completed.", result);
    }

    private static object ListRevisions(Document doc)
    {
        var revisions = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Revisions)
            .WhereElementIsNotElementType()
            .Cast<Autodesk.Revit.DB.Revision>()
            .OrderBy(r => r.SequenceNumber)
            .Select(r => new
            {
                id = r.Id.Value,
                sequenceNumber = r.SequenceNumber,
                description = r.Description,
                date = r.RevisionDate,
                issued = r.Issued,
                revisionNumber = r.RevisionNumber
            })
            .ToList();

        var list = revisions;
        return new { message = $"Found {list.Count} revisions.", count = list.Count, revisions = list };
    }

    private static object CreateRevision(Document doc, Dictionary<string, object?> parameters)
    {
        var description = parameters.GetValueOrDefault("description")?.ToString() ?? "New Revision";
        var date = parameters.GetValueOrDefault("date")?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");

        using var tx = new Transaction(doc, "Create Revision");
        tx.Start();

        var rev = Autodesk.Revit.DB.Revision.Create(doc);
        rev.Description = description;
        rev.RevisionDate = date;

        tx.Commit();

        return new
        {
            message = $"Created revision #{rev.SequenceNumber}: '{description}' ({date})",
            id = rev.Id.Value,
            sequenceNumber = rev.SequenceNumber,
            description,
            date
        };
    }

    private static object ModifySheetsRevision(Document doc, Dictionary<string, object?> parameters, bool add)
    {
        var revNumStr = parameters.GetValueOrDefault("revision_number")?.ToString();
        var sheetNumsStr = parameters.GetValueOrDefault("sheet_numbers")?.ToString();

        if (string.IsNullOrWhiteSpace(revNumStr))
            return new { error = "Parameter 'revision_number' is required for add/remove." };
        if (string.IsNullOrWhiteSpace(sheetNumsStr))
            return new { error = "Parameter 'sheet_numbers' is required." };

        if (!int.TryParse(revNumStr, out var revNum))
            return new { error = $"Invalid revision number: '{revNumStr}'." };

        var targetRevision = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Revisions)
            .WhereElementIsNotElementType()
            .Cast<Autodesk.Revit.DB.Revision>()
            .FirstOrDefault(r => r.SequenceNumber == revNum);

        if (targetRevision is null)
            return new { error = $"Revision with sequence number {revNum} not found." };

        if (targetRevision.Issued)
            return new { error = $"Revision #{revNum} is already issued and cannot be modified." };

        var sheetNumbers = sheetNumsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(s => sheetNumbers.Contains(s.SheetNumber, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (sheets.Count == 0)
            return new { error = $"No sheets found matching: {sheetNumsStr}" };

        int modified = 0;
        var errors = new List<string>();

        using var tx = new Transaction(doc, add ? "Add Revision to Sheets" : "Remove Revision from Sheets");
        tx.Start();

        foreach (var sheet in sheets)
        {
            try
            {
                var current = sheet.GetAdditionalRevisionIds().ToList();
                if (add)
                {
                    if (!current.Contains(targetRevision.Id))
                    {
                        current.Add(targetRevision.Id);
                        sheet.SetAdditionalRevisionIds(current);
                        modified++;
                    }
                }
                else
                {
                    if (current.Remove(targetRevision.Id))
                    {
                        sheet.SetAdditionalRevisionIds(current);
                        modified++;
                    }
                }
            }
            catch (Exception ex) { errors.Add($"{sheet.SheetNumber}: {ex.Message}"); }
        }

        tx.Commit();

        var verb = add ? "added to" : "removed from";
        return new
        {
            message = $"Revision #{revNum} {verb} {modified}/{sheets.Count} sheets." +
                      (errors.Count > 0 ? $" Errors: {errors.Count}" : ""),
            modified,
            totalSheets = sheets.Count,
            errors = errors.Count > 0 ? errors : null
        };
    }
}
