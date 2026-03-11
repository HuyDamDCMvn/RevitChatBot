using System.Text;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Report;

[Skill("export_report",
    "Generate a summary report of MEP elements. Returns formatted text data " +
    "for ducts, pipes, equipment, or project overview.")]
[SkillParameter("report_type", "string",
    "Type of report to generate",
    isRequired: true,
    allowedValues: new[] { "project_overview", "duct_schedule", "pipe_schedule", "equipment_list", "system_summary" })]
[SkillParameter("system_name", "string",
    "Optional system name to filter",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to limit to elements visible in the current view, " +
    "'entire_model' to include all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class ExportReportSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var reportType = parameters.GetValueOrDefault("report_type")?.ToString() ?? "project_overview";
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            return reportType switch
            {
                "project_overview" => GenerateProjectOverview(document),
                "duct_schedule" => GenerateElementSchedule(document, BuiltInCategory.OST_DuctCurves, "Duct", scope),
                "pipe_schedule" => GenerateElementSchedule(document, BuiltInCategory.OST_PipeCurves, "Pipe", scope),
                "equipment_list" => GenerateElementSchedule(document, BuiltInCategory.OST_MechanicalEquipment, "Equipment", scope),
                "system_summary" => GenerateSystemSummary(document),
                _ => (object)"Unknown report type"
            };
        });

        return SkillResult.Ok($"Report '{reportType}' generated.", result);
    }

    private static object GenerateProjectOverview(Document doc)
    {
        var docService = new RevitDocumentService();
        var mepService = new RevitMEPService();

        var info = docService.GetProjectInfo(doc);
        var levels = docService.GetLevelNames(doc);
        var systems = mepService.GetMEPSystems(doc);
        var ducts = mepService.GetDucts(doc);
        var pipes = mepService.GetPipes(doc);
        var equipment = mepService.GetMEPEquipment(doc);

        return new
        {
            projectInfo = info,
            levels,
            mepSummary = new
            {
                systemCount = systems.Count,
                ductCount = ducts.Count,
                pipeCount = pipes.Count,
                equipmentCount = equipment.Count
            }
        };
    }

    private static object GenerateElementSchedule(
        Document doc, BuiltInCategory category, string label, string scope)
    {
        var elements = ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .ToList();

        var service = new RevitElementService();
        var rows = elements.Take(100).Select(e =>
        {
            var ps = service.GetElementParameters(e);
            return new
            {
                Id = e.Id.Value,
                Name = e.Name,
                Type = doc.GetElement(e.GetTypeId())?.Name ?? "N/A",
                Size = ps.GetValueOrDefault("Size", "N/A"),
                Length = ps.GetValueOrDefault("Length", "N/A"),
                System = ps.GetValueOrDefault("System Name", ps.GetValueOrDefault("System Type", "N/A"))
            };
        }).ToList();

        return new
        {
            label,
            totalCount = elements.Count,
            returnedCount = rows.Count,
            elements = rows
        };
    }

    private static object GenerateSystemSummary(Document doc)
    {
        var mepService = new RevitMEPService();
        var systems = mepService.GetMEPSystems(doc);

        var summaries = systems.Select(s => mepService.GetMEPSystemInfo(s)).ToList();

        return new
        {
            totalSystems = systems.Count,
            systems = summaries
        };
    }
}
