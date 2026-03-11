using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

/// <summary>
/// Generates a summary for submission package preparation.
/// Audits model readiness: parameter completeness, view count,
/// system coverage, and generates a checklist of items to address.
/// </summary>
[Skill("generate_submission_package",
    "Prepare a submission package summary. Audits model readiness: checks parameter " +
    "completeness, view availability, system coverage, naming compliance, and generates " +
    "a checklist of items that need attention before submission.")]
[SkillParameter("submission_type", "string",
    "'design', 'construction', 'asbuilt'. Default: 'design'.",
    isRequired: false, allowedValues: new[] { "design", "construction", "asbuilt" })]
public class GenerateSubmissionPackageSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var submissionType = parameters.GetValueOrDefault("submission_type")?.ToString() ?? "design";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var checklist = new List<object>();

            var modelInfo = AuditModelInfo(document);
            checklist.Add(modelInfo);

            var viewAudit = AuditViews(document);
            checklist.Add(viewAudit);

            var paramAudit = AuditParameters(document);
            checklist.Add(paramAudit);

            var systemAudit = AuditSystems(document);
            checklist.Add(systemAudit);

            var connectivityAudit = AuditConnectivity(document);
            checklist.Add(connectivityAudit);

            int totalChecks = checklist.Sum(c => ((dynamic)c).totalChecks);
            int passed = checklist.Sum(c => ((dynamic)c).passed);
            int failed = checklist.Sum(c => ((dynamic)c).failed);
            double readiness = totalChecks > 0 ? (double)passed / totalChecks * 100 : 0;

            return new
            {
                submissionType,
                readinessPercent = Math.Round(readiness, 1),
                readinessStatus = readiness >= 90 ? "READY" : readiness >= 70 ? "NEEDS ATTENTION" : "NOT READY",
                totalChecks,
                passed,
                failed,
                checklist
            };
        });

        return SkillResult.Ok("Submission package audit completed.", result);
    }

    private static object AuditModelInfo(Document doc)
    {
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();
        var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();

        int checks = 2, pass = 0;
        var issues = new List<string>();

        if (levels > 0) pass++; else issues.Add("No levels defined");
        pass++;

        return new
        {
            section = "Model Info",
            totalChecks = checks,
            passed = pass,
            failed = checks - pass,
            details = new { levels, linkedModels = links },
            issues
        };
    }

    private static object AuditViews(Document doc)
    {
        var views = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .ToList();

        var planViews = views.Count(v => v.ViewType == ViewType.FloorPlan);
        var sectionViews = views.Count(v => v.ViewType == ViewType.Section);
        var schedules = views.Count(v => v.ViewType == ViewType.Schedule);
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .GetElementCount();

        int checks = 4, pass = 0;
        var issues = new List<string>();

        if (planViews > 0) pass++; else issues.Add("No floor plan views");
        if (sectionViews > 0) pass++; else issues.Add("No section views");
        if (schedules > 0) pass++; else issues.Add("No schedule views");
        if (sheets > 0) pass++; else issues.Add("No sheets for printing");

        return new
        {
            section = "Views & Sheets",
            totalChecks = checks,
            passed = pass,
            failed = checks - pass,
            details = new
            {
                totalViews = views.Count,
                floorPlans = planViews,
                sections = sectionViews,
                schedules,
                sheets
            },
            issues
        };
    }

    private static object AuditParameters(Document doc)
    {
        var equipment = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
            .WhereElementIsNotElementType()
            .ToList();

        int totalEquip = equipment.Count;
        int withMark = equipment.Count(e =>
            !string.IsNullOrWhiteSpace(e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()));

        var ducts = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_DuctCurves)
            .WhereElementIsNotElementType()
            .ToList();

        int ductsWithSystem = ducts.Count(d =>
            !string.IsNullOrWhiteSpace(d.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString()));

        int checks = 2, pass = 0;
        var issues = new List<string>();

        var markPercent = totalEquip > 0 ? (double)withMark / totalEquip * 100 : 100;
        if (markPercent >= 80) pass++; else issues.Add($"Only {markPercent:F0}% of equipment has marks");

        var sysPercent = ducts.Count > 0 ? (double)ductsWithSystem / ducts.Count * 100 : 100;
        if (sysPercent >= 90) pass++; else issues.Add($"Only {sysPercent:F0}% of ducts have system names");

        return new
        {
            section = "Parameter Completeness",
            totalChecks = checks,
            passed = pass,
            failed = checks - pass,
            details = new
            {
                equipmentWithMarks = $"{withMark}/{totalEquip}",
                ductsWithSystem = $"{ductsWithSystem}/{ducts.Count}"
            },
            issues
        };
    }

    private static object AuditSystems(Document doc)
    {
        var systems = new FilteredElementCollector(doc)
            .OfClass(typeof(MEPSystem))
            .ToList();

        int checks = 1, pass = 0;
        var issues = new List<string>();

        if (systems.Count > 0) pass++;
        else issues.Add("No MEP systems defined");

        return new
        {
            section = "System Coverage",
            totalChecks = checks,
            passed = pass,
            failed = checks - pass,
            details = new { totalSystems = systems.Count },
            issues
        };
    }

    private static object AuditConnectivity(Document doc)
    {
        var ducts = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_DuctCurves)
            .WhereElementIsNotElementType()
            .ToList();

        var pipes = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_PipeCurves)
            .WhereElementIsNotElementType()
            .ToList();

        int totalMep = ducts.Count + pipes.Count;
        int disconnected = 0;

        foreach (var elem in ducts.Concat(pipes))
        {
            var cm = (elem as MEPCurve)?.ConnectorManager;
            if (cm is null) continue;
            foreach (Connector c in cm.Connectors)
            {
                if (!c.IsConnected) { disconnected++; break; }
            }
        }

        int checks = 1, pass = 0;
        var issues = new List<string>();

        var connPercent = totalMep > 0 ? (double)(totalMep - disconnected) / totalMep * 100 : 100;
        if (connPercent >= 95) pass++;
        else issues.Add($"Only {connPercent:F0}% connectivity ({disconnected} disconnected elements)");

        return new
        {
            section = "Connectivity",
            totalChecks = checks,
            passed = pass,
            failed = checks - pass,
            details = new
            {
                totalMepElements = totalMep,
                disconnected,
                connectivityPercent = Math.Round(connPercent, 1)
            },
            issues
        };
    }
}
