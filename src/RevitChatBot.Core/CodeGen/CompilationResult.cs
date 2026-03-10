using System.Reflection;

namespace RevitChatBot.Core.CodeGen;

public class CompilationResult
{
    public bool Success { get; set; }
    public Assembly? CompiledAssembly { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string? GeneratedCode { get; set; }
    public TimeSpan CompileTime { get; set; }
}
