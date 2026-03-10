using System.Reflection;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Compiles C# code at runtime using Roslyn.
/// Resolves Revit API assemblies and .NET runtime references automatically.
/// </summary>
public class RoslynCodeCompiler
{
    private readonly List<MetadataReference> _references = [];
    private bool _initialized;

    public void AddReferenceFromFile(string dllPath)
    {
        if (File.Exists(dllPath))
            _references.Add(MetadataReference.CreateFromFile(dllPath));
    }

    public void AddReferenceFromType(Type type)
    {
        var location = type.Assembly.Location;
        if (!string.IsNullOrEmpty(location) && File.Exists(location))
            _references.Add(MetadataReference.CreateFromFile(location));
    }

    /// <summary>
    /// Initialize with .NET runtime references. Call once at startup.
    /// Revit API references should be added separately via AddReferenceFromFile.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDlls = new[]
        {
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Console.dll",
            "System.Text.RegularExpressions.dll",
            "System.Private.CoreLib.dll",
            "netstandard.dll",
            "mscorlib.dll"
        };

        foreach (var dll in runtimeDlls)
        {
            var path = Path.Combine(runtimeDir, dll);
            if (File.Exists(path))
                _references.Add(MetadataReference.CreateFromFile(path));
        }

        _initialized = true;
    }

    public CompilationResult Compile(string code, string? assemblyName = null)
    {
        if (!_initialized) Initialize();

        var sw = Stopwatch.StartNew();
        assemblyName ??= $"DynamicRevitAction_{Guid.NewGuid():N}";

        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [syntaxTree],
            references: _references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(false));

        using var ms = new MemoryStream();
        EmitResult emitResult = compilation.Emit(ms);
        sw.Stop();

        var result = new CompilationResult
        {
            GeneratedCode = code,
            CompileTime = sw.Elapsed
        };

        if (!emitResult.Success)
        {
            result.Success = false;
            foreach (var diag in emitResult.Diagnostics)
            {
                var msg = diag.ToString();
                if (diag.Severity == DiagnosticSeverity.Error)
                    result.Errors.Add(msg);
                else if (diag.Severity == DiagnosticSeverity.Warning)
                    result.Warnings.Add(msg);
            }
            return result;
        }

        ms.Seek(0, SeekOrigin.Begin);
        result.CompiledAssembly = Assembly.Load(ms.ToArray());
        result.Success = true;
        return result;
    }
}
