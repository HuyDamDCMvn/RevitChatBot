using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("skill_self_test",
    "Run a self-test on registered skills to verify configuration. " +
    "Lists all skills with parameter counts and validates metadata integrity.")]
[SkillParameter("skill_name", "string",
    "Specific skill name to test, or omit to test all.",
    isRequired: false)]
[SkillParameter("mode", "string",
    "Test mode: 'list' shows all skills, 'validate' checks configuration integrity.",
    isRequired: false,
    allowedValues: new[] { "list", "validate" })]
public class SkillSelfTestSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var mode = parameters.GetValueOrDefault("mode")?.ToString() ?? "list";
        var targetSkill = parameters.GetValueOrDefault("skill_name")?.ToString();

        if (context.Extra.GetValueOrDefault("skill_descriptors") is not IEnumerable<object> rawDescriptors)
            return SkillResult.Fail("Skill descriptors not available in context. Wire 'skill_descriptors' in WebViewBridge.");

        var descriptors = rawDescriptors
            .Cast<dynamic>()
            .Select(d => new SkillTestInfo
            {
                Name = (string)d.Name,
                Description = (string)d.Description,
                ParameterCount = ((IEnumerable<object>)d.Parameters).Count()
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(targetSkill))
            descriptors = descriptors.Where(d => d.Name.Contains(targetSkill, StringComparison.OrdinalIgnoreCase)).ToList();

        if (mode == "validate")
            return RunValidation(descriptors);

        return SkillResult.Ok($"Found {descriptors.Count} skills.", new
        {
            total = descriptors.Count,
            skills = descriptors.Select(d => new
            {
                name = d.Name,
                description = d.Description.Length > 80 ? d.Description[..80] + "..." : d.Description,
                parameters = d.ParameterCount
            }).OrderBy(s => s.name).ToList()
        });
    }

    private static SkillResult RunValidation(List<SkillTestInfo> descriptors)
    {
        var results = new List<object>();
        var passCount = 0;
        var failCount = 0;
        var issues = new List<string>();

        var nameSet = new HashSet<string>();
        foreach (var d in descriptors)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(d.Name))
                errors.Add("Name is empty");
            else if (!nameSet.Add(d.Name))
                errors.Add($"Duplicate name: {d.Name}");

            if (string.IsNullOrWhiteSpace(d.Description))
                errors.Add("Description is empty");
            else if (d.Description.Length < 20)
                errors.Add($"Description too short ({d.Description.Length} chars, min 20)");

            var passed = errors.Count == 0;
            if (passed) passCount++; else failCount++;

            results.Add(new
            {
                name = d.Name,
                status = passed ? "PASS" : "FAIL",
                errors = errors.Count > 0 ? errors : null,
                parameterCount = d.ParameterCount
            });

            issues.AddRange(errors.Select(e => $"[{d.Name}] {e}"));
        }

        var summary = $"Validation: {passCount} passed, {failCount} failed out of {descriptors.Count} skills.";
        if (issues.Count > 0)
            summary += "\nIssues:\n" + string.Join("\n", issues.Select(i => $"  - {i}"));

        return SkillResult.Ok(summary, new
        {
            total = descriptors.Count,
            passed = passCount,
            failed = failCount,
            results
        });
    }

    private class SkillTestInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int ParameterCount { get; set; }
    }
}
