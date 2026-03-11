using System.Diagnostics;
using System.Reflection;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Executes dynamically compiled C# code within a Revit context.
/// Expects the generated class to have a static Execute(Document doc) method
/// that returns a string result.
/// </summary>
public class DynamicCodeExecutor
{
    private readonly RoslynCodeCompiler _compiler;
    private readonly TimeSpan _executionTimeout;

    public const string EntryClassName = "DynamicAction";
    public const string EntryMethodName = "Execute";

    public DynamicCodeExecutor(RoslynCodeCompiler compiler, TimeSpan? timeout = null)
    {
        _compiler = compiler;
        _executionTimeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Validates, compiles, and executes the code.
    /// The revitApiInvoker is used to marshal execution onto Revit's main thread.
    /// </summary>
    public async Task<CodeExecutionResult> ExecuteAsync(
        string code,
        Func<Func<object, object?>, Task<object?>> revitApiInvoker,
        CancellationToken ct = default)
    {
        var security = CodeSecurityValidator.Validate(code);
        if (!security.IsValid)
        {
            return CodeExecutionResult.Fail(
                "Security validation failed:\n" +
                string.Join("\n", security.Violations.Select(v => $"  - {v}")),
                code);
        }

        var wrappedCode = WrapIfNeeded(code);
        var compilationResult = _compiler.Compile(wrappedCode);
        if (!compilationResult.Success)
        {
            return CodeExecutionResult.Fail(
                "Compilation failed:\n" +
                string.Join("\n", compilationResult.Errors.Select(e => $"  - {e}")),
                wrappedCode);
        }

        try
        {
            var assembly = compilationResult.CompiledAssembly!;
            var entryType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == EntryClassName)
                ?? throw new InvalidOperationException(
                    $"Class '{EntryClassName}' not found in compiled code.");

            var method = entryType.GetMethod(EntryMethodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"Static method '{EntryMethodName}' not found in '{EntryClassName}'.");

            var sw = Stopwatch.StartNew();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_executionTimeout);

            var result = await revitApiInvoker(doc =>
            {
                var parameters = method.GetParameters();
                return parameters.Length switch
                {
                    0 => method.Invoke(null, null),
                    1 => method.Invoke(null, [doc]),
                    _ => throw new InvalidOperationException(
                        $"Execute method must take 0 or 1 (Document) parameter, found {parameters.Length}")
                };
            });

            sw.Stop();

            var output = result?.ToString() ?? "Code executed successfully (no output).";
            return new CodeExecutionResult
            {
                Success = true,
                Output = output,
                GeneratedCode = wrappedCode,
                ExecutionTime = sw.Elapsed
            };
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException ?? tie;
            return CodeExecutionResult.Fail(
                $"Runtime error: {inner.GetType().Name}: {inner.Message}",
                wrappedCode);
        }
        catch (Exception ex)
        {
            return CodeExecutionResult.Fail(
                $"Execution error: {ex.Message}",
                wrappedCode);
        }
    }

    /// <summary>
    /// Wraps bare code that doesn't contain a class definition into the expected structure.
    /// </summary>
    private static string WrapIfNeeded(string code)
    {
        if (code.Contains($"class {EntryClassName}"))
            return code;

        if (code.Contains("public static") && code.Contains(EntryMethodName))
            return code;

        return $$"""
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using Autodesk.Revit.DB;
            using Autodesk.Revit.DB.Mechanical;
            using Autodesk.Revit.DB.Plumbing;
            using Autodesk.Revit.DB.Electrical;
            using Autodesk.Revit.DB.Structure;
            using RevitChatBot.RevitServices;

            public static class DynamicAction
            {
                public static string Execute(Document doc)
                {
                    {{code}}
                }
            }
            """;
    }

    /// <summary>
    /// Returns the C# code template the LLM should follow when generating code.
    /// </summary>
    public static string GetCodeTemplate()
    {
        return """
            // Template for dynamic Revit code.
            // The Execute method receives the active Revit Document.
            // Return a string describing the result.
            
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using Autodesk.Revit.DB;
            using Autodesk.Revit.DB.Mechanical;
            using Autodesk.Revit.DB.Plumbing;
            using Autodesk.Revit.DB.Electrical;
            using Autodesk.Revit.DB.Structure;
            using RevitChatBot.RevitServices;
            
            public static class DynamicAction
            {
                public static string Execute(Document doc)
                {
                    // Use FluentCollector for optimized queries:
                    //   new FluentCollector(doc).OfCategory(...).OnLevel(...).ToList()
                    // Use ElementExtensions for clean parameter access:
                    //   element.GetSystemName(), element.GetSize(), element.GetLevelName(doc)
                    // Use FilteredElementCollector for standard queries.
                    // Use Transaction for modifications.
                    // Return a descriptive result string.
                    
                    return "Done";
                }
            }
            """;
    }
}
