namespace RevitChatBot.Core.CodeGen;

public class CodeExecutionResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public string? GeneratedCode { get; set; }

    public static CodeExecutionResult Ok(string output, string? code = null) =>
        new() { Success = true, Output = output, GeneratedCode = code };

    public static CodeExecutionResult Fail(string error, string? code = null) =>
        new() { Success = false, Error = error, GeneratedCode = code };
}
