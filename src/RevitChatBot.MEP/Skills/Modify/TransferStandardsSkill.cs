using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("transfer_standards",
    "Transfer project standards (view templates, filters, line styles, fill patterns, materials) " +
    "from a template/source Revit file to the current project. " +
    "Inspired by DiStem Project Standards. " +
    "Run with action='list' first to see what's available, then 'transfer' to apply.")]
[SkillParameter("source_path", "string",
    "Full path to the source/template .rvt file.",
    isRequired: true)]
[SkillParameter("action", "string",
    "'list' to show available standards in the source, 'transfer' to copy them.",
    isRequired: true,
    allowedValues: new[] { "list", "transfer" })]
[SkillParameter("standard_type", "string",
    "What to transfer: 'view_templates', 'filters', 'line_styles', 'materials', 'fill_patterns', 'all'. " +
    "Default 'all'.",
    isRequired: false,
    allowedValues: new[] { "view_templates", "filters", "line_styles", "materials", "fill_patterns", "all" })]
[SkillParameter("name_filter", "string",
    "Only transfer items whose name contains this text.",
    isRequired: false)]
[SkillParameter("overwrite", "string",
    "'true' to overwrite existing items with same name, 'false' to skip duplicates. Default 'false'.",
    isRequired: false)]
public class TransferStandardsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var sourcePath = parameters.GetValueOrDefault("source_path")?.ToString();
        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "list";
        var standardType = parameters.GetValueOrDefault("standard_type")?.ToString() ?? "all";
        var nameFilter = parameters.GetValueOrDefault("name_filter")?.ToString();
        var overwrite = parameters.GetValueOrDefault("overwrite")?.ToString()?.ToLower() == "true";

        if (string.IsNullOrWhiteSpace(sourcePath))
            return SkillResult.Fail("source_path is required.");

        if (!File.Exists(sourcePath))
            return SkillResult.Fail($"Source file not found: {sourcePath}");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var app = document.Application;
            Document? sourceDoc = null;

            try
            {
                sourceDoc = app.OpenDocumentFile(sourcePath);
                if (sourceDoc is null)
                    return new TransferResult { Message = "Failed to open source document." };

                if (action == "list")
                    return ListAvailableStandards(sourceDoc, standardType, nameFilter);

                return TransferStandards(document, sourceDoc, standardType, nameFilter, overwrite);
            }
            finally
            {
                sourceDoc?.Close(false);
            }
        });

        var res = result as TransferResult;
        if (res is null)
            return SkillResult.Ok("Operation completed.", result);

        return res.Success
            ? SkillResult.Ok(res.Message, result)
            : SkillResult.Fail(res.Message);
    }

    private static TransferResult ListAvailableStandards(
        Document sourceDoc, string standardType, string? nameFilter)
    {
        var standards = new Dictionary<string, List<string>>();

        if (standardType is "view_templates" or "all")
        {
            standards["view_templates"] = sourceDoc.GetElements()
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && MatchesFilter(v.Name, nameFilter))
                .Select(v => v.Name)
                .OrderBy(n => n)
                .ToList();
        }

        if (standardType is "filters" or "all")
        {
            standards["filters"] = sourceDoc.GetElements()
                .OfClass(typeof(ParameterFilterElement))
                .Where(f => MatchesFilter(f.Name, nameFilter))
                .Select(f => f.Name)
                .OrderBy(n => n)
                .ToList();
        }

        if (standardType is "materials" or "all")
        {
            standards["materials"] = sourceDoc.GetElements()
                .OfClass(typeof(Material))
                .Where(m => MatchesFilter(m.Name, nameFilter))
                .Select(m => m.Name)
                .OrderBy(n => n)
                .ToList();
        }

        if (standardType is "fill_patterns" or "all")
        {
            standards["fill_patterns"] = sourceDoc.GetElements()
                .OfClass(typeof(FillPatternElement))
                .Where(fp => MatchesFilter(fp.Name, nameFilter))
                .Select(fp => fp.Name)
                .OrderBy(n => n)
                .ToList();
        }

        var totalCount = standards.Values.Sum(l => l.Count);

        return new TransferResult
        {
            Success = true,
            Message = $"Source contains {totalCount} transferable standards.",
            Standards = standards,
            TransferredCount = 0
        };
    }

    private static TransferResult TransferStandards(
        Document targetDoc, Document sourceDoc, string standardType, string? nameFilter, bool overwrite)
    {
        var idsToTransfer = new List<ElementId>();

        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!overwrite)
        {
            foreach (var elem in targetDoc.GetElements().OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate))
                existingNames.Add(elem.Name);
            foreach (var elem in targetDoc.GetElements().OfClass(typeof(ParameterFilterElement)))
                existingNames.Add(elem.Name);
            foreach (var elem in targetDoc.GetElements().OfClass(typeof(Material)))
                existingNames.Add(elem.Name);
            foreach (var elem in targetDoc.GetElements().OfClass(typeof(FillPatternElement)))
                existingNames.Add(elem.Name);
        }

        if (standardType is "view_templates" or "all")
        {
            idsToTransfer.AddRange(
                sourceDoc.GetElements()
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate
                                && MatchesFilter(v.Name, nameFilter)
                                && (overwrite || !existingNames.Contains(v.Name)))
                    .Select(v => v.Id));
        }

        if (standardType is "filters" or "all")
        {
            idsToTransfer.AddRange(
                sourceDoc.GetElements()
                    .OfClass(typeof(ParameterFilterElement))
                    .Where(f => MatchesFilter(f.Name, nameFilter)
                                && (overwrite || !existingNames.Contains(f.Name)))
                    .Select(f => f.Id));
        }

        if (standardType is "materials" or "all")
        {
            idsToTransfer.AddRange(
                sourceDoc.GetElements()
                    .OfClass(typeof(Material))
                    .Where(m => MatchesFilter(m.Name, nameFilter)
                                && (overwrite || !existingNames.Contains(m.Name)))
                    .Select(m => m.Id));
        }

        if (standardType is "fill_patterns" or "all")
        {
            idsToTransfer.AddRange(
                sourceDoc.GetElements()
                    .OfClass(typeof(FillPatternElement))
                    .Where(fp => MatchesFilter(fp.Name, nameFilter)
                                 && (overwrite || !existingNames.Contains(fp.Name)))
                    .Select(fp => fp.Id));
        }

        if (idsToTransfer.Count == 0)
            return new TransferResult
            {
                Success = true,
                Message = "No new standards to transfer (all already exist or none match filter)."
            };

        using var tx = new Transaction(targetDoc, "Transfer project standards");
        tx.Start();

        var copyOpts = new CopyPasteOptions();
        if (overwrite)
            copyOpts.SetDuplicateTypeNamesHandler(new OverwriteDuplicateHandler());

        var transferred = 0;
        var failed = 0;

        try
        {
            var copied = ElementTransformUtils.CopyElements(
                sourceDoc, idsToTransfer, targetDoc, null, copyOpts);
            transferred = copied?.Count ?? 0;
        }
        catch
        {
            foreach (var id in idsToTransfer)
            {
                try
                {
                    ElementTransformUtils.CopyElements(
                        sourceDoc, new List<ElementId> { id }, targetDoc, null, copyOpts);
                    transferred++;
                }
                catch { failed++; }
            }
        }

        tx.Commit();

        return new TransferResult
        {
            Success = true,
            Message = $"Transferred {transferred} standards ({failed} failed).",
            TransferredCount = transferred,
            FailedCount = failed
        };
    }

    private static bool MatchesFilter(string name, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private class TransferResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public Dictionary<string, List<string>> Standards { get; set; } = new();
        public int TransferredCount { get; set; }
        public int FailedCount { get; set; }
    }

    private class OverwriteDuplicateHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }
}
