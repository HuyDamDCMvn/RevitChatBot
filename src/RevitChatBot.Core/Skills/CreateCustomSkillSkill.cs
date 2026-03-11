using System.Text.Json.Nodes;
using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.LLM;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Allows the user (or agent) to intentionally create a new reusable skill from
/// either provided code or a natural language description.
/// Compiles via Roslyn, validates, and registers into DynamicSkillRegistry.
/// </summary>
[Skill("create_custom_skill",
    "Create a new reusable skill from code or a natural language description. " +
    "The skill is compiled, validated, and registered for future use. " +
    "Use 'description' for LLM-assisted code generation, or 'code' to provide C# directly.")]
[SkillParameter("name", "string",
    "Snake_case name for the new skill (e.g. 'check_duct_aspect_ratio').",
    isRequired: true)]
[SkillParameter("skill_description", "string",
    "What the skill does, in natural language. Used for the skill catalog and LLM routing.",
    isRequired: true)]
[SkillParameter("code", "string",
    "C# code for the skill (DynamicAction.Execute pattern). If omitted, LLM generates it from skill_description.",
    isRequired: false)]
[SkillParameter("parameters_json", "string",
    "JSON array of parameter definitions: [{\"name\":\"x\",\"type\":\"string\",\"description\":\"...\"}].",
    isRequired: false)]
public class CreateCustomSkillSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var name = parameters.GetValueOrDefault("name")?.ToString()?.Trim() ?? "";
        var description = parameters.GetValueOrDefault("skill_description")?.ToString() ?? "";
        var code = parameters.GetValueOrDefault("code")?.ToString();
        var paramsJson = parameters.GetValueOrDefault("parameters_json")?.ToString();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
            return SkillResult.Fail("Both 'name' and 'skill_description' are required.");

        var registry = context.Extra.GetValueOrDefault("skill_registry") as SkillRegistry;
        if (registry?.GetSkill(name) is not null)
            return SkillResult.Fail($"Skill '{name}' already exists.");

        var dynamicRegistry = context.Extra.GetValueOrDefault("dynamic_skill_registry") as DynamicSkillRegistry;
        var codeExecutor = context.Extra.GetValueOrDefault("code_executor") as DynamicCodeExecutor;

        if (dynamicRegistry is null || codeExecutor is null)
            return SkillResult.Fail("DynamicSkillRegistry or DynamicCodeExecutor not available in context.");

        if (string.IsNullOrWhiteSpace(code))
        {
            var ollama = context.Extra.GetValueOrDefault("ollama_service") as IOllamaService;
            if (ollama is null)
                return SkillResult.Fail("No code provided and LLM not available for code generation.");

            code = await GenerateCode(ollama, name, description, cancellationToken);
            if (string.IsNullOrWhiteSpace(code))
                return SkillResult.Fail("LLM failed to generate valid code.");
        }

        var skillParams = ParseParams(paramsJson);

        try
        {
            dynamicRegistry.CreateSkill(name, description, code, skillParams);
            return SkillResult.Ok(
                $"Custom skill '{name}' created and registered successfully.\n" +
                $"Description: {description}\n" +
                $"Parameters: {skillParams.Count}\n" +
                $"The skill is now available for use.",
                new { name, description, parameterCount = skillParams.Count });
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Failed to create skill: {ex.Message}");
        }
    }

    private static async Task<string?> GenerateCode(
        IOllamaService ollama, string name, string description, CancellationToken ct)
    {
        var prompt = $$"""
            Generate a C# class for a Revit API skill.
            Skill name: {{name}}
            Description: {{description}}
            
            Use this exact pattern:
            ```csharp
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using Autodesk.Revit.DB;
            
            public static class DynamicAction
            {
                public static string Execute(Document doc)
                {
                    // Implementation here
                    return "result";
                }
            }
            ```
            
            Return ONLY the C# code, no explanation.
            Use FilteredElementCollector for queries.
            Use Transaction for any modifications.
            Always return a descriptive string result.
            """;

        try
        {
            var result = await ollama.GenerateAsync(prompt,
                temperature: 0.2, numCtx: 4096, cancellationToken: ct);

            var code = result.Trim();
            var startIdx = code.IndexOf("using ", StringComparison.Ordinal);
            if (startIdx > 0) code = code[startIdx..];
            var endIdx = code.LastIndexOf("```", StringComparison.Ordinal);
            if (endIdx > 0) code = code[..endIdx];
            return code.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static List<DynamicSkillParam> ParseParams(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var array = JsonNode.Parse(json)?.AsArray();
            if (array is null) return [];

            return array
                .Where(n => n is not null)
                .Select(n => new DynamicSkillParam
                {
                    Name = n!["name"]?.GetValue<string>() ?? "",
                    Type = n["type"]?.GetValue<string>() ?? "string",
                    Description = n["description"]?.GetValue<string>() ?? ""
                })
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList();
        }
        catch { return []; }
    }
}
