using System.Text.Json;

namespace RevitChatBot.Core.Skills;

public class SkillResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
    public string? ErrorDetail { get; set; }

    public static SkillResult Ok(string message, object? data = null) =>
        new() { Success = true, Message = message, Data = data };

    public static SkillResult Fail(string message, string? detail = null) =>
        new() { Success = false, Message = message, ErrorDetail = detail };

    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            success = Success,
            message = Message,
            data = Data,
            error = ErrorDetail
        }, new JsonSerializerOptions { WriteIndented = false });
    }
}
