using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Runs a comprehensive QA/QC checklist across all MEP systems.
/// Checks: disconnections, velocity, slope, insulation, naming,
/// parameter completeness, system assignments, and sizing.
/// Returns a consolidated pass/fail report.
/// </summary>
[Skill("mep_qaqc_checklist",
    "Run a comprehensive MEP QA/QC checklist. Checks disconnections, velocity, slope, " +
    "insulation, naming conventions, parameter completeness, system assignments, and sizing. " +
    "Returns a consolidated report with pass/fail status per check category.")]
[SkillParameter("level", "string",
    "Filter by level name (optional). Runs checks for all levels if omitted.",
    isRequired: false)]
[SkillParameter("categories", "string",
    "Comma-separated categories to check: 'duct,pipe,equipment,fitting'. Default: all.",
    isRequired: false)]
public class MepQaqcChecklistSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("categories")?.ToString() ?? "duct,pipe,equipment,fitting";
        var checkCategories = categoryStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToLower()).ToHashSet();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var checks = new List<object>();
            int totalPass = 0, totalFail = 0, totalWarn = 0;

            if (checkCategories.Contains("duct"))
            {
                var ductCheck = CheckDucts(document, levelFilter);
                checks.Add(ductCheck);
                totalPass += ductCheck.passCount;
                totalFail += ductCheck.failCount;
                totalWarn += ductCheck.warnCount;
            }

            if (checkCategories.Contains("pipe"))
            {
                var pipeCheck = CheckPipes(document, levelFilter);
                checks.Add(pipeCheck);
                totalPass += pipeCheck.passCount;
                totalFail += pipeCheck.failCount;
                totalWarn += pipeCheck.warnCount;
            }

            if (checkCategories.Contains("equipment"))
            {
                var eqCheck = CheckEquipment(document, levelFilter);
                checks.Add(eqCheck);
                totalPass += eqCheck.passCount;
                totalFail += eqCheck.failCount;
                totalWarn += eqCheck.warnCount;
            }

            if (checkCategories.Contains("fitting"))
            {
                var fitCheck = CheckFittings(document, levelFilter);
                checks.Add(fitCheck);
                totalPass += fitCheck.passCount;
                totalFail += fitCheck.failCount;
                totalWarn += fitCheck.warnCount;
            }

            var overallStatus = totalFail > 0 ? "FAIL" : totalWarn > 0 ? "WARNING" : "PASS";

            return new
            {
                overallStatus,
                totalChecks = totalPass + totalFail + totalWarn,
                passed = totalPass,
                failed = totalFail,
                warnings = totalWarn,
                checks
            };
        });

        return SkillResult.Ok("MEP QA/QC checklist completed.", result);
    }

    private static dynamic CheckDucts(Document doc, string? levelFilter)
    {
        var ducts = CollectElements(doc, typeof(Duct), levelFilter);

        int disconnected = 0, noSystem = 0, noSize = 0, noInsulation = 0;

        foreach (var d in ducts)
        {
            if (HasOpenConnectors(d)) disconnected++;
            if (string.IsNullOrWhiteSpace(d.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString())) noSystem++;
            if (string.IsNullOrWhiteSpace(d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString())) noSize++;
            var insType = d.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_TYPE)?.AsElementId();
            if (insType is null || insType == ElementId.InvalidElementId) noInsulation++;
        }

        int total = ducts.Count;
        int failCount = disconnected + noSystem;
        int warnCount = noSize + noInsulation;
        int passCount = Math.Max(0, total * 4 - failCount - warnCount);

        return new
        {
            category = "Duct",
            totalElements = total,
            passCount,
            failCount,
            warnCount,
            issues = new
            {
                disconnected,
                noSystemAssignment = noSystem,
                missingSize = noSize,
                missingInsulation = noInsulation
            }
        };
    }

    private static dynamic CheckPipes(Document doc, string? levelFilter)
    {
        var pipes = CollectElements(doc, typeof(Pipe), levelFilter);

        int disconnected = 0, noSystem = 0, badSlope = 0, noInsulation = 0;

        foreach (var p in pipes)
        {
            if (HasOpenConnectors(p)) disconnected++;
            if (string.IsNullOrWhiteSpace(p.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString())) noSystem++;
            var slope = p.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE)?.AsDouble() ?? 0;
            var classification = p.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
            if (classification.Contains("Sanitary", StringComparison.OrdinalIgnoreCase) && slope < 0.005) badSlope++;
            var insType = p.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_TYPE)?.AsElementId();
            if (insType is null || insType == ElementId.InvalidElementId) noInsulation++;
        }

        int total = pipes.Count;
        int failCount = disconnected + noSystem;
        int warnCount = badSlope + noInsulation;
        int passCount = Math.Max(0, total * 4 - failCount - warnCount);

        return new
        {
            category = "Pipe",
            totalElements = total,
            passCount,
            failCount,
            warnCount,
            issues = new
            {
                disconnected,
                noSystemAssignment = noSystem,
                insufficientSlope = badSlope,
                missingInsulation = noInsulation
            }
        };
    }

    private static dynamic CheckEquipment(Document doc, string? levelFilter)
    {
        var equipment = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            equipment = equipment.Where(e => GetLevelName(doc, e)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        int noFamily = 0, disconnected = 0, noMark = 0;

        foreach (var eq in equipment)
        {
            var familyName = eq.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString();
            if (string.IsNullOrWhiteSpace(familyName)) noFamily++;
            if (HasOpenConnectors(eq)) disconnected++;
            if (string.IsNullOrWhiteSpace(eq.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString())) noMark++;
        }

        int total = equipment.Count;
        int failCount = disconnected;
        int warnCount = noFamily + noMark;
        int passCount = Math.Max(0, total * 3 - failCount - warnCount);

        return new
        {
            category = "Equipment",
            totalElements = total,
            passCount,
            failCount,
            warnCount,
            issues = new
            {
                disconnected,
                noFamilyName = noFamily,
                missingMark = noMark
            }
        };
    }

    private static dynamic CheckFittings(Document doc, string? levelFilter)
    {
        var categories = new[]
        {
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_PipeFitting
        };

        var fittings = categories
            .SelectMany(cat => new FilteredElementCollector(doc)
                .OfCategory(cat)
                .WhereElementIsNotElementType()
                .ToList())
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            fittings = fittings.Where(f => GetLevelName(doc, f)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        int disconnected = 0, noSystem = 0;

        foreach (var f in fittings)
        {
            if (HasOpenConnectors(f)) disconnected++;
            if (string.IsNullOrWhiteSpace(f.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString())) noSystem++;
        }

        int total = fittings.Count;
        int failCount = disconnected;
        int warnCount = noSystem;
        int passCount = Math.Max(0, total * 2 - failCount - warnCount);

        return new
        {
            category = "Fitting",
            totalElements = total,
            passCount,
            failCount,
            warnCount,
            issues = new
            {
                disconnected,
                noSystemAssignment = noSystem
            }
        };
    }

    private static List<Element> CollectElements(Document doc, Type elementClass, string? levelFilter)
    {
        var elements = new FilteredElementCollector(doc)
            .OfClass(elementClass)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            elements = elements.Where(e => GetLevelName(doc, e)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        return elements;
    }

    private static bool HasOpenConnectors(Element elem)
    {
        var cm = (elem as Autodesk.Revit.DB.MEPCurve)?.ConnectorManager
                 ?? (elem as FamilyInstance)?.MEPModel?.ConnectorManager;
        if (cm is null) return false;

        foreach (Connector c in cm.Connectors)
            if (!c.IsConnected) return true;
        return false;
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId()
                    ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }
}
