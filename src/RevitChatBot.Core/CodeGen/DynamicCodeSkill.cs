using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// A skill that lets the LLM write and execute arbitrary C# code against the Revit API.
/// The LLM should generate a complete DynamicAction class with an Execute(Document) method.
/// Includes security validation and structured error feedback for LLM self-correction.
/// </summary>
[Skill("execute_revit_code",
    "Generate and execute C# code against the Revit API. " +
    "Use this when no existing skill can fulfill the request. " +
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
public class DynamicCodeSkill : ISkill
{
    private readonly DynamicCodeExecutor _executor;

    public DynamicCodeSkill(DynamicCodeExecutor executor)
    {
        _executor = executor;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (!parameters.TryGetValue("code", out var codeObj) || codeObj is not string code || string.IsNullOrWhiteSpace(code))
            return SkillResult.Fail("Parameter 'code' is required and must contain valid C# code.");

        var description = parameters.GetValueOrDefault("description")?.ToString() ?? "Dynamic code execution";

        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API invoker not available. Cannot execute code outside of Revit.");

        var result = await _executor.ExecuteAsync(code, context.RevitApiInvoker, cancellationToken);

        if (result.Success)
        {
            return SkillResult.Ok(
                result.Output ?? "Code executed successfully.",
                new
                {
                    description,
                    executionTime = result.ExecutionTime.TotalMilliseconds,
                    generatedCode = result.GeneratedCode
                });
        }

        var errorFeedback = BuildErrorFeedback(result);
        return SkillResult.Fail(errorFeedback, result.GeneratedCode);
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
