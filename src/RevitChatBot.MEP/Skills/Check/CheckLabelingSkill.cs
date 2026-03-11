using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Checks that MEP elements have proper labels, marks, and tags.
/// Ensures equipment has marks, ducts/pipes have system names,
/// and key elements are tagged in views.
/// </summary>
[Skill("check_labeling",
    "Check that MEP elements have proper labels, marks, and tags. " +
    "Validates that equipment has unique marks, ducts/pipes have system names, " +
    "and key elements have comments/descriptions where required.")]
[SkillParameter("category", "string",
    "'equipment', 'duct', 'pipe', 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "equipment", "duct", "pipe", "all" })]
[SkillParameter("check_uniqueness", "string",
    "Check for duplicate marks within the same category. 'true' or 'false'. Default: 'true'.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
public class CheckLabelingSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var checkUniqueness = parameters.GetValueOrDefault("check_uniqueness")?.ToString() != "false";
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var sections = new List<object>();

            if (category is "equipment" or "all")
                sections.Add(CheckEquipmentLabels(document, levelFilter, checkUniqueness));

            if (category is "duct" or "all")
                sections.Add(CheckLinearLabels(document, BuiltInCategory.OST_DuctCurves,
                    "Duct", levelFilter));

            if (category is "pipe" or "all")
                sections.Add(CheckLinearLabels(document, BuiltInCategory.OST_PipeCurves,
                    "Pipe", levelFilter));

            int totalIssues = sections.Sum(s => ((dynamic)s).issueCount);

            return new
            {
                overallStatus = totalIssues > 0 ? "LABELING ISSUES" : "OK",
                totalIssues,
                sections
            };
        });

        return SkillResult.Ok("Labeling check completed.", result);
    }

    private static object CheckEquipmentLabels(Document doc, string? levelFilter, bool checkUnique)
    {
        var categories = new[]
        {
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_PlumbingFixtures
        };

        var equipment = categories
            .SelectMany(cat => new FilteredElementCollector(doc)
                .OfCategory(cat)
                .WhereElementIsNotElementType()
                .ToList())
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            equipment = equipment.Where(e => GetLevelName(doc, e)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        int missingMark = 0;
        int missingComments = 0;
        var marks = new Dictionary<string, List<long>>();
        var issues = new List<object>();

        foreach (var eq in equipment)
        {
            var mark = eq.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
            var comments = eq.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";

            if (string.IsNullOrWhiteSpace(mark))
            {
                missingMark++;
                issues.Add(new
                {
                    elementId = eq.Id.Value,
                    family = eq.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "N/A",
                    issue = "Missing Mark",
                    level = GetLevelName(doc, eq)
                });
            }
            else if (checkUnique)
            {
                if (!marks.ContainsKey(mark)) marks[mark] = [];
                marks[mark].Add(eq.Id.Value);
            }

            if (string.IsNullOrWhiteSpace(comments)) missingComments++;
        }

        int duplicateMarks = 0;
        if (checkUnique)
        {
            foreach (var (mark, ids) in marks.Where(kv => kv.Value.Count > 1))
            {
                duplicateMarks += ids.Count;
                issues.Add(new
                {
                    elementId = ids.First(),
                    family = "Multiple",
                    issue = $"Duplicate Mark '{mark}' ({ids.Count} elements)",
                    level = ""
                });
            }
        }

        return new
        {
            category = "Equipment",
            totalElements = equipment.Count,
            issueCount = missingMark + duplicateMarks,
            missingMark,
            missingComments,
            duplicateMarks,
            issues = issues.Take(30).ToList()
        };
    }

    private static object CheckLinearLabels(Document doc, BuiltInCategory bic,
        string label, string? levelFilter)
    {
        var elements = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            elements = elements.Where(e => GetLevelName(doc, e)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        int noSystem = 0, noSize = 0;

        foreach (var e in elements)
        {
            if (string.IsNullOrWhiteSpace(e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString()))
                noSystem++;
            if (string.IsNullOrWhiteSpace(e.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString()))
                noSize++;
        }

        return new
        {
            category = label,
            totalElements = elements.Count,
            issueCount = noSystem,
            missingSystemName = noSystem,
            missingSize = noSize
        };
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }
}
