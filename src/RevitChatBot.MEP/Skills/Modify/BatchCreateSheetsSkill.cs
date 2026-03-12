using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("batch_create_sheets",
    "Create multiple sheets from a numbering pattern and title block. " +
    "Use action='preview' first to verify sheet numbers before creating.")]
[SkillParameter("action", "string", "preview or apply.", isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("start_number", "string",
    "Starting sheet number (e.g. 'M-01', 'E-101').", isRequired: true)]
[SkillParameter("count", "integer", "Number of sheets to create.", isRequired: true)]
[SkillParameter("name_pattern", "string",
    "Sheet name pattern. Use {n} for sequence number (e.g. 'HVAC Plan - Level {n}').",
    isRequired: false)]
[SkillParameter("title_block", "string",
    "Title block family name (partial match). Uses first available if omitted.",
    isRequired: false)]
public class BatchCreateSheetsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";
        var startNum = parameters.GetValueOrDefault("start_number")?.ToString();
        if (string.IsNullOrWhiteSpace(startNum)) return SkillResult.Fail("'start_number' is required.");
        if (!int.TryParse(parameters.GetValueOrDefault("count")?.ToString(), out var count) || count < 1 || count > 100)
            return SkillResult.Fail("'count' must be between 1 and 100.");

        var namePattern = parameters.GetValueOrDefault("name_pattern")?.ToString();
        var titleBlockFilter = parameters.GetValueOrDefault("title_block")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var (prefix, numPart, digitWidth, suffix) = ParseSheetNumber(startNum);
            var planned = Enumerable.Range(0, count).Select(i =>
            {
                var num = numPart + i;
                var sheetNumber = $"{prefix}{num.ToString($"D{digitWidth}")}{suffix}";
                var sheetName = !string.IsNullOrWhiteSpace(namePattern)
                    ? namePattern.Replace("{n}", (i + 1).ToString()) : $"Sheet {sheetNumber}";
                return new { sheetNumber, sheetName };
            }).ToList();

            var existingNumbers = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Select(s => s.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var conflicts = planned.Where(p => existingNumbers.Contains(p.sheetNumber)).Select(p => p.sheetNumber).ToList();
            if (conflicts.Count > 0)
                return new { error = $"Sheet numbers already exist: {string.Join(", ", conflicts)}. Choose different start number.",
                    planned = planned, conflicts };

            if (action == "preview")
                return new { error = (string?)null, message = $"Preview: {planned.Count} sheets will be created.",
                    planned, conflicts = new List<string>() };

            var titleBlockId = FindTitleBlock(document, titleBlockFilter);

            using var tx = new Transaction(document, "Batch Create Sheets");
            tx.Start();
            int created = 0;
            var errors = new List<string>();

            foreach (var p in planned)
            {
                try
                {
                    var sheet = ViewSheet.Create(document, titleBlockId);
                    sheet.SheetNumber = p.sheetNumber;
                    sheet.Name = p.sheetName;
                    created++;
                }
                catch (Exception ex) { errors.Add($"{p.sheetNumber}: {ex.Message}"); }
            }

            tx.Commit();
            return new { error = (string?)null,
                message = $"Created {created}/{planned.Count} sheets." + (errors.Count > 0 ? $" Errors: {errors.Count}" : ""),
                planned, created, errors, conflicts = new List<string>() };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);
        return SkillResult.Ok(data?.message?.ToString() ?? "Done.", result);
    }

    private static (string prefix, int number, int digitWidth, string suffix) ParseSheetNumber(string sheetNum)
    {
        int firstDigit = -1, lastDigit = -1;
        for (int i = 0; i < sheetNum.Length; i++)
        {
            if (char.IsDigit(sheetNum[i]))
            {
                if (firstDigit < 0) firstDigit = i;
                lastDigit = i;
            }
        }
        if (firstDigit < 0) return (sheetNum, 1, 1, "");
        var prefix = sheetNum[..firstDigit];
        var numStr = sheetNum[firstDigit..(lastDigit + 1)];
        var suffix = sheetNum[(lastDigit + 1)..];
        var digitWidth = numStr.Length;
        return (prefix, int.TryParse(numStr, out var n) ? n : 1, digitWidth, suffix);
    }

    private static ElementId FindTitleBlock(Document doc, string? filter)
    {
        var titleBlocks = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var match = titleBlocks.FirstOrDefault(t =>
                t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Id;
        }

        return titleBlocks.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }
}
