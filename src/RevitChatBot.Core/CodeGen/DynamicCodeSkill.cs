using RevitChatBot.Core.Learning;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// A skill that lets the LLM write and execute arbitrary C# code against the Revit API.
/// Integrates with CodeGenLibrary (reuse), DynamicSkillRegistry (promote), and
/// CodePatternLearning (learn) for self-evolving capabilities.
/// </summary>
[Skill("execute_revit_code",
    "Generate and execute C# code against the Revit API. " +
    "Use this when no existing skill can fulfill the request. " +
    "Check [saved_codegen_library] first — if a similar task was done before, reuse that code. " +
    "Write a complete C# class named 'DynamicAction' with a static 'Execute(Document doc)' " +
    "method that returns a string result. " +
    "Available namespaces: System, System.Linq, System.Collections.Generic, " +
    "Autodesk.Revit.DB, Autodesk.Revit.DB.Mechanical, Autodesk.Revit.DB.Plumbing, " +
    "Autodesk.Revit.DB.Electrical, Autodesk.Revit.DB.Structure. " +
    "Use FilteredElementCollector to query elements. " +
    "Wrap modifications in a Transaction. " +
    "IMPORTANT: Return a descriptive result string, not just 'Done'. " +
    "If the previous attempt failed, analyze the error message and fix the code.")]
[SkillParameter("code", "string",
    "Complete C# code with a 'DynamicAction' class containing a static 'Execute(Document doc)' method. " +
    "Must return a string.", isRequired: true)]
[SkillParameter("description", "string",
    "Brief description of what the code does (for user confirmation)", isRequired: true)]
[SkillParameter("is_destructive", "boolean",
    "Whether the code modifies the Revit model (create/modify/delete elements)", isRequired: false)]
[SkillParameter("save_as_skill", "string",
    "If set, save this code as a reusable skill with this name (e.g., 'count_ducts_by_level'). " +
    "Only use after successful execution when the user confirms saving.", isRequired: false)]
public class DynamicCodeSkill : ISkill
{
    private readonly DynamicCodeExecutor _executor;
    private readonly CodeGenLibrary? _library;
    private readonly DynamicSkillRegistry? _dynamicRegistry;
    private readonly CodePatternLearning? _patternLearning;
    private readonly LearningModuleHub? _hub;
    private readonly CodeAutoFixer? _autoFixer;

    public DynamicCodeSkill(
        DynamicCodeExecutor executor,
        CodeGenLibrary? library = null,
        DynamicSkillRegistry? dynamicRegistry = null,
        CodePatternLearning? patternLearning = null,
        LearningModuleHub? hub = null,
        CodeAutoFixer? autoFixer = null)
    {
        _executor = executor;
        _library = library;
        _dynamicRegistry = dynamicRegistry;
        _patternLearning = patternLearning;
        _hub = hub;
        _autoFixer = autoFixer;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (!parameters.TryGetValue("code", out var codeObj) || codeObj is not string code || string.IsNullOrWhiteSpace(code))
            return SkillResult.Fail("Parameter 'code' is required and must contain valid C# code.");

        var description = parameters.GetValueOrDefault("description")?.ToString() ?? "Dynamic code execution";
        var saveAsSkill = parameters.GetValueOrDefault("save_as_skill")?.ToString();

        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API invoker not available. Cannot execute code outside of Revit.");

        var cachedEntry = _library?.FindMatch(description);
        if (cachedEntry != null && cachedEntry.Code == code)
        {
            cachedEntry.UseCount++;
            cachedEntry.LastUsed = DateTime.UtcNow;
        }

        var result = await _executor.ExecuteAsync(code, context.RevitApiInvoker, cancellationToken);

        if (result.Success)
        {
            var finalCode = result.GeneratedCode ?? code;
            _library?.RecordSuccess(description, finalCode,
                result.Output ?? "", result.ExecutionTime.TotalMilliseconds);
            _patternLearning?.RecordSuccess(finalCode,
                description, result.ExecutionTime.TotalMilliseconds);

            var analysis = CodeAutoFixer.AnalyzeCode(finalCode);
            PublishCodeGenEvent(true, description, finalCode, null, analysis);

            if (!string.IsNullOrWhiteSpace(saveAsSkill) && _dynamicRegistry != null)
            {
                try
                {
                    var def = _dynamicRegistry.CreateSkill(saveAsSkill, description, finalCode);
                    _ = _dynamicRegistry.SaveAsync(CancellationToken.None);

                    _hub?.Publish(new LearningEvent("DynamicCodeSkill",
                        LearningEventTypes.SkillRegistered,
                        new { SkillName = def.Name, Description = description }));

                    return SkillResult.Ok(
                        $"{result.Output}\n\n--- Saved as reusable skill: '{def.Name}' ---\n" +
                        $"You can now call this skill directly by name in future requests.",
                        new { description, executionTime = result.ExecutionTime.TotalMilliseconds,
                              savedAsSkill = def.Name });
                }
                catch (Exception ex)
                {
                    return SkillResult.Ok(
                        $"{result.Output}\n\n(Could not save as skill: {ex.Message})",
                        new { description, executionTime = result.ExecutionTime.TotalMilliseconds });
                }
            }

            _ = Task.Run(() => _library?.SaveAsync(CancellationToken.None));
            _ = Task.Run(() => _patternLearning?.SaveAsync(CancellationToken.None));

            var savedNote = _library != null
                ? "\n\n(Code saved to library for future reuse.)" : "";

            return SkillResult.Ok(
                (result.Output ?? "Code executed successfully.") + savedNote,
                new { description, executionTime = result.ExecutionTime.TotalMilliseconds,
                      generatedCode = result.GeneratedCode });
        }

        _library?.RecordFailure(description, result.GeneratedCode ?? code, result.Error ?? "");
        _patternLearning?.RecordFailure(result.GeneratedCode ?? code, result.Error ?? "");
        _ = Task.Run(() => _patternLearning?.SaveAsync(CancellationToken.None));

        PublishCodeGenEvent(false, description, result.GeneratedCode ?? code, result.Error, null);

        var errorFeedback = BuildErrorFeedback(result);
        return SkillResult.Fail(errorFeedback, result.GeneratedCode);
    }

    private void PublishCodeGenEvent(bool success, string description, string code,
        string? error, CodeAnalysisResult? analysis)
    {
        _hub?.Publish(new LearningEvent(
            "DynamicCodeSkill",
            success ? LearningEventTypes.CodeGenSuccess : LearningEventTypes.CodeGenFailure,
            new CodeGenEventData
            {
                Query = description,
                Code = code,
                Error = error,
                ApiPatternsUsed = analysis?.ApiPatterns ?? [],
                CategoriesQueried = analysis?.CategoriesQueried ?? []
            }));
    }

    /// <summary>
    /// Builds a structured error message that helps the LLM understand what went wrong
    /// and how to fix it. Includes the error, the relevant fix hint, and instructions.
    /// </summary>
    private static string BuildErrorFeedback(CodeExecutionResult result)
    {
        var error = result.Error ?? "Unknown error";
        var feedback = $"CODE EXECUTION FAILED:\n{error}\n";

        var hints = MatchErrorToHints(error);
        if (hints.Count > 0)
        {
            feedback += "\nSUGGESTED FIXES:\n";
            foreach (var hint in hints)
                feedback += $"  → {hint}\n";
        }

        feedback += "\nPlease fix the code and try again with execute_revit_code. " +
                    "Analyze the error carefully and apply the suggested fixes.";

        return feedback;
    }

    private static List<string> MatchErrorToHints(string error)
    {
        var hints = new List<string>();

        if (error.Contains("PipeSystemType") && error.Contains("Storm"))
            hints.Add("PipeSystemType.Storm doesn't exist in Revit 2025. Use PipeSystemType.OtherPipe instead.");

        if (error.Contains("ParameterType") && error.Contains("obsolete"))
            hints.Add("ParameterType is deprecated. Use ForgeTypeId / SpecTypeId instead.");

        if (error.Contains("UnitType") && error.Contains("obsolete"))
            hints.Add("UnitType is deprecated. Use UnitTypeId instead (e.g., UnitTypeId.Millimeters).");

        if (error.Contains("ElementSet") && error.Contains("Size"))
            hints.Add("ElementSet.Size is deprecated. Use .Count property or LINQ .Count() instead.");

        if (error.Contains("not found") || error.Contains("could not be found"))
        {
            hints.Add("Check 'using' statements. Required: System, System.Linq, System.Collections.Generic, Autodesk.Revit.DB");
            if (error.Contains("Duct") || error.Contains("Mechanical"))
                hints.Add("Add: using Autodesk.Revit.DB.Mechanical;");
            if (error.Contains("Pipe") || error.Contains("Plumbing") || error.Contains("PipingSystem"))
                hints.Add("Add: using Autodesk.Revit.DB.Plumbing;");
            if (error.Contains("Electrical"))
                hints.Add("Add: using Autodesk.Revit.DB.Electrical;");
            if (error.Contains("Structure") || error.Contains("StructuralType"))
                hints.Add("Add: using Autodesk.Revit.DB.Structure;");
        }

        if (error.Contains("Transaction") || error.Contains("not permitted"))
            hints.Add("Model modifications require Transaction. Use: using var tx = new Transaction(doc, \"name\"); tx.Start(); ... tx.Commit();");

        if (error.Contains("IsActive") || error.Contains("symbol is not active"))
            hints.Add("Before placing FamilyInstance: if (!symbol.IsActive) symbol.Activate(); (inside Transaction).");

        if (error.Contains("Cannot implicitly convert") && error.Contains("XYZ"))
            hints.Add("Create XYZ with: new XYZ(x, y, z). Values must be in feet (meters / 0.3048).");

        if (error.Contains("LinearDimension") && error.Contains("Dimension"))
            hints.Add("In Revit 2025, linear dimensions return as LinearDimension (child of Dimension). Use 'is Dimension' pattern.");

        if (error.Contains("Security validation failed"))
            hints.Add("Only Revit API namespaces are allowed. No file I/O, network, process, or unsafe code.");

        if (error.Contains("No active document"))
            hints.Add("This code can only run when a Revit document is open.");

        if (error.Contains("NullReferenceException"))
            hints.Add("Add null checks. Common: element?.Parameter, doc.GetElement(id) can return null, MEPSystem can be null for unconnected elements.");

        if (error.Contains("InvalidOperationException") && error.Contains("Sequence contains no elements"))
            hints.Add("Use .FirstOrDefault() instead of .First() and check for null. The collection might be empty.");

        if (hints.Count == 0)
            hints.Add("Review the error message, check API method signatures, and ensure all types are correctly referenced.");

        return hints;
    }
}
