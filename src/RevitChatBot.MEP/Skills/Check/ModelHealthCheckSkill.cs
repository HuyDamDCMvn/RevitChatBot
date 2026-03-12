using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("model_health_check",
    "Comprehensive model health assessment. Reports file size, Revit warnings count, " +
    "total element count, family count, view count, linked model count, workset count, " +
    "and group count. Use for model performance assessment and BIM management audits.")]
[SkillParameter("include_details", "string",
    "'true' for detailed breakdown by category, 'false' for summary only. Default: 'false'.",
    isRequired: false)]
public class ModelHealthCheckSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var includeDetails = parameters.GetValueOrDefault("include_details")?.ToString()?.ToLower() == "true";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            long fileSizeBytes = 0;
            var filePath = document.PathName;
            if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                fileSizeBytes = new System.IO.FileInfo(filePath).Length;

            var warnings = document.GetWarnings();
            var warningCount = warnings.Count;
            var criticalWarnings = warnings
                .Where(w => w.GetSeverity() == FailureSeverity.Error)
                .ToList();

            var allElements = new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .ToElements();
            var totalElements = allElements.Count;

            var families = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.Family))
                .GetElementCount();

            var allViews = new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .ToList();
            var viewCount = allViews.Count;
            var viewTemplateCount = new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(v => v.IsTemplate);

            var sheets = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet))
                .GetElementCount();

            var links = new FilteredElementCollector(document)
                .OfClass(typeof(RevitLinkInstance))
                .GetElementCount();

            var groups = new FilteredElementCollector(document)
                .OfClass(typeof(Group))
                .GetElementCount();

            var importInstances = new FilteredElementCollector(document)
                .OfClass(typeof(ImportInstance))
                .GetElementCount();

            var worksetCount = 0;
            if (document.IsWorkshared)
            {
                var worksetTable = document.GetWorksetTable();
                worksetCount = new FilteredWorksetCollector(document)
                    .OfKind(WorksetKind.UserWorkset)
                    .ToWorksetIds()
                    .Count;
            }

            var health = new Dictionary<string, object>
            {
                ["filePath"] = filePath ?? "(not saved)",
                ["fileSizeMB"] = Math.Round(fileSizeBytes / 1024.0 / 1024.0, 1),
                ["warningCount"] = warningCount,
                ["criticalWarningCount"] = criticalWarnings.Count,
                ["totalElements"] = totalElements,
                ["familyCount"] = families,
                ["viewCount"] = viewCount,
                ["viewTemplateCount"] = viewTemplateCount,
                ["sheetCount"] = sheets,
                ["linkedModelCount"] = links,
                ["groupCount"] = groups,
                ["importedDwgCount"] = importInstances,
                ["worksetCount"] = worksetCount,
                ["isWorkshared"] = document.IsWorkshared,
            };

            var alerts = new List<string>();
            if (fileSizeBytes > 300 * 1024 * 1024)
                alerts.Add($"File size ({Math.Round(fileSizeBytes / 1024.0 / 1024.0, 0)} MB) exceeds 300 MB threshold");
            if (warningCount > 500)
                alerts.Add($"Warning count ({warningCount}) is high — consider reviewing");
            if (families > 1000)
                alerts.Add($"Family count ({families}) is high — consider purging unused");
            if (viewCount > 500)
                alerts.Add($"View count ({viewCount}) is high — consider cleanup");
            if (importInstances > 0)
                alerts.Add($"Found {importInstances} imported DWG instances — consider removing");
            if (groups > 50)
                alerts.Add($"Group count ({groups}) is high — may impact performance");

            health["alerts"] = alerts;
            health["healthScore"] = CalculateScore(fileSizeBytes, warningCount, families, viewCount, importInstances, groups);

            if (includeDetails)
            {
                var topWarnings = warnings
                    .GroupBy(w => w.GetDescriptionText())
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => new { description = g.Key, count = g.Count() })
                    .ToList();
                health["topWarnings"] = topWarnings;

                var unusedViewCount = allViews.Count(v =>
                {
                    try { return !v.IsTemplate && v is not ViewSheet && v.GetDependentElements(null).Count == 0; }
                    catch { return false; }
                });
                health["potentialUnusedViews"] = unusedViewCount;
            }

            return health;
        });

        var data = (Dictionary<string, object>)result!;
        var score = data["healthScore"];
        var alertList = (List<string>)data["alerts"];
        var summary = $"Model health score: {score}/100. " +
                      $"{data["fileSizeMB"]} MB, {data["warningCount"]} warnings, " +
                      $"{data["totalElements"]} elements, {data["familyCount"]} families, " +
                      $"{data["viewCount"]} views.";
        if (alertList.Count > 0)
            summary += " Alerts: " + string.Join("; ", alertList);

        return SkillResult.Ok(summary, result);
    }

    private static int CalculateScore(long fileSize, int warnings, int families, int views, int imports, int groups)
    {
        var score = 100;
        if (fileSize > 500 * 1024 * 1024) score -= 25;
        else if (fileSize > 300 * 1024 * 1024) score -= 15;
        else if (fileSize > 150 * 1024 * 1024) score -= 5;

        if (warnings > 1000) score -= 25;
        else if (warnings > 500) score -= 15;
        else if (warnings > 200) score -= 5;

        if (families > 2000) score -= 15;
        else if (families > 1000) score -= 8;

        if (views > 1000) score -= 10;
        else if (views > 500) score -= 5;

        if (imports > 10) score -= 10;
        else if (imports > 0) score -= 5;

        if (groups > 100) score -= 10;
        else if (groups > 50) score -= 5;

        return Math.Max(0, score);
    }
}
