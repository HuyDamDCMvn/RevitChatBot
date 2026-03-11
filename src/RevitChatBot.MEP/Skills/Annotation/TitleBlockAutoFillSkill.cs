using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("fill_title_block",
    "Auto-fill title block parameters on sheets from project information. " +
    "Batch updates project name, number, date, author, discipline, " +
    "and custom parameters across selected or all sheets.")]
[SkillParameter("sheet_ids", "string",
    "Comma-separated sheet IDs, or 'all' for all sheets", isRequired: true)]
[SkillParameter("fields", "string",
    "Comma-separated field=value pairs to override (e.g. 'Drawn By=JD,Checked By=AH'). " +
    "If not specified, pulls from Project Information.",
    isRequired: false)]
[SkillParameter("date_format", "string",
    "Date format for the date field (default 'yyyy-MM-dd')",
    isRequired: false)]
public class TitleBlockAutoFillSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var sheetIdsStr = parameters.GetValueOrDefault("sheet_ids")?.ToString();
        var fieldsStr = parameters.GetValueOrDefault("fields")?.ToString();
        var dateFormat = parameters.GetValueOrDefault("date_format")?.ToString() ?? "yyyy-MM-dd";

        if (string.IsNullOrWhiteSpace(sheetIdsStr))
            return SkillResult.Fail("sheet_ids is required.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var sheets = ResolveSheets(document, sheetIdsStr);
            if (sheets.Count == 0)
                return new { success = false, message = "No sheets found.", updated = 0 };

            var projectInfo = document.ProjectInformation;
            var overrides = ParseFieldOverrides(fieldsStr);

            using var tx = new Transaction(document, "Auto-fill title blocks");
            tx.Start();

            int updated = 0;
            foreach (var sheet in sheets)
            {
                var titleBlock = new FilteredElementCollector(document, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();

                if (titleBlock is null) continue;

                bool sheetUpdated = false;

                // Project Info fields
                sheetUpdated |= TryFill(titleBlock, "Project Name",
                    overrides.GetValueOrDefault("Project Name") ?? projectInfo?.Name ?? "");
                sheetUpdated |= TryFill(titleBlock, "Project Number",
                    overrides.GetValueOrDefault("Project Number") ?? projectInfo?.Number ?? "");
                sheetUpdated |= TryFill(titleBlock, "Client Name",
                    overrides.GetValueOrDefault("Client Name") ?? projectInfo?.ClientName ?? "");
                sheetUpdated |= TryFill(titleBlock, "Project Address",
                    overrides.GetValueOrDefault("Project Address") ?? projectInfo?.Address ?? "");

                // Date
                sheetUpdated |= TryFill(titleBlock, "Date",
                    overrides.GetValueOrDefault("Date") ?? DateTime.Now.ToString(dateFormat));
                sheetUpdated |= TryFill(titleBlock, "Issue Date",
                    overrides.GetValueOrDefault("Issue Date") ?? DateTime.Now.ToString(dateFormat));

                // People
                foreach (var field in new[] { "Drawn By", "Designed By", "Checked By", "Approved By" })
                {
                    if (overrides.TryGetValue(field, out var value))
                        sheetUpdated |= TryFill(titleBlock, field, value);
                }

                // Custom overrides
                foreach (var (key, value) in overrides)
                {
                    sheetUpdated |= TryFill(titleBlock, key, value);
                }

                // Sheet-level parameters
                sheetUpdated |= TryFill(sheet, "Sheet Issue Date",
                    overrides.GetValueOrDefault("Sheet Issue Date") ?? DateTime.Now.ToString(dateFormat));

                if (sheetUpdated) updated++;
            }

            tx.Commit();

            return new
            {
                success = true,
                message = $"Updated title blocks on {updated}/{sheets.Count} sheets.",
                updated
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static bool TryFill(Element elem, string paramName, string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var param = elem.LookupParameter(paramName);
        if (param is null || param.IsReadOnly || param.StorageType != StorageType.String)
            return false;
        try
        {
            var current = param.AsString() ?? "";
            if (current == value) return false;
            param.Set(value);
            return true;
        }
        catch { return false; }
    }

    private static List<ViewSheet> ResolveSheets(Document doc, string spec)
    {
        if (spec.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();
        }

        return spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => long.TryParse(s, out _))
            .Select(s => doc.GetElement(new ElementId(long.Parse(s))))
            .OfType<ViewSheet>()
            .ToList();
    }

    private static Dictionary<string, string> ParseFieldOverrides(string? fieldsStr)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(fieldsStr)) return result;

        foreach (var pair in fieldsStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
                result[parts[0].Trim()] = parts[1].Trim();
        }
        return result;
    }
}
