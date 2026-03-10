namespace RevitChatBot.Core.LLM;

/// <summary>
/// Handles ambiguous user queries by generating clarification questions.
/// Integrates with QueryPreprocessor to detect when clarification is needed.
/// </summary>
public class ClarificationFlow
{
    private static readonly Dictionary<string, List<ClarificationTemplate>> Templates = new()
    {
        ["missing_category"] = [
            new("vi",
                "Bạn muốn thao tác với loại phần tử MEP nào?",
                ["Ống gió (Duct)", "Ống nước (Pipe)", "Ống luồn (Conduit)", "Máng cáp (Cable Tray)", "Thiết bị (Equipment)", "Tất cả"]),
            new("en",
                "Which MEP element type do you want to work with?",
                ["Duct", "Pipe", "Conduit", "Cable Tray", "Equipment", "All"])
        ],

        ["missing_level"] = [
            new("vi",
                "Bạn muốn thực hiện trên tầng nào? (hoặc 'Tất cả' cho toàn bộ model)",
                []),
            new("en",
                "Which level should this apply to? (or 'All' for entire model)",
                [])
        ],

        ["missing_system"] = [
            new("vi",
                "Hệ thống nào? Ví dụ: cấp lạnh (CHW), thoát nước (SAN), cấp gió (SA), hồi gió (RA), PCCC...",
                ["Cấp lạnh (CHW)", "Cấp nóng (HW)", "Thoát nước (SAN)", "Cấp gió (SA)", "Hồi gió (RA)", "PCCC"]),
            new("en",
                "Which system? E.g., Chilled Water (CHW), Sanitary (SAN), Supply Air (SA)...",
                ["Chilled Water", "Hot Water", "Sanitary", "Supply Air", "Return Air", "Fire Protection"])
        ],

        ["vague_query"] = [
            new("vi",
                "Yêu cầu chưa rõ ràng. Bạn có thể mô tả chi tiết hơn không? Ví dụ:\n" +
                "- 'Kiểm tra vận tốc ống gió tầng 2'\n" +
                "- 'Đếm ống nước theo hệ thống'\n" +
                "- 'Tìm va chạm ống gió và ống nước'",
                []),
            new("en",
                "Could you be more specific? For example:\n" +
                "- 'Check duct velocity on Level 2'\n" +
                "- 'Count pipes by system'\n" +
                "- 'Find clashes between ducts and pipes'",
                [])
        ],

        ["confirm_destructive"] = [
            new("vi",
                "Thao tác này sẽ thay đổi model. Bạn có muốn xem trước (analyze) hay thực hiện luôn (execute)?",
                ["Xem trước (Analyze)", "Thực hiện (Execute)"]),
            new("en",
                "This action will modify the model. Would you like to preview (analyze) or execute directly?",
                ["Preview (Analyze)", "Execute"])
        ]
    };

    /// <summary>
    /// Check if clarification is needed based on query analysis.
    /// Returns a clarification message, or null if the query is clear enough.
    /// </summary>
    public static ClarificationResult? CheckNeedsClarification(QueryAnalysis analysis)
    {
        if (analysis.NeedsClarification && !string.IsNullOrEmpty(analysis.ClarificationQuestion))
        {
            return new ClarificationResult
            {
                NeedsClarification = true,
                Question = analysis.ClarificationQuestion,
                Reason = "deep_analysis"
            };
        }

        var lang = analysis.Language;

        if (analysis.IsAmbiguous && analysis.Category == null &&
            analysis.Intent is "query" or "check" or "modify")
        {
            var template = GetTemplate("missing_category", lang);
            if (template != null)
            {
                return new ClarificationResult
                {
                    NeedsClarification = true,
                    Question = template.Question,
                    Options = template.Options,
                    Reason = "missing_category"
                };
            }
        }

        if (analysis.Intent is "modify" or "create" or "delete" &&
            analysis.Category == null && analysis.ElementIds.Count == 0)
        {
            var template = GetTemplate("confirm_destructive", lang);
            if (template != null)
            {
                return new ClarificationResult
                {
                    NeedsClarification = true,
                    Question = template.Question,
                    Options = template.Options,
                    Reason = "confirm_destructive"
                };
            }
        }

        if (analysis.OriginalQuery.Trim().Length < 10 && analysis.Intent == "query")
        {
            var template = GetTemplate("vague_query", lang);
            if (template != null)
            {
                return new ClarificationResult
                {
                    NeedsClarification = true,
                    Question = template.Question,
                    Reason = "vague_query"
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Enrich the user's original query with clarification response.
    /// </summary>
    public static string EnrichQuery(string originalQuery, string clarificationResponse, string reason)
    {
        return reason switch
        {
            "missing_category" => $"{originalQuery} [loại: {clarificationResponse}]",
            "missing_level" => $"{originalQuery} [tầng: {clarificationResponse}]",
            "missing_system" => $"{originalQuery} [hệ thống: {clarificationResponse}]",
            _ => $"{originalQuery} ({clarificationResponse})"
        };
    }

    private static ClarificationTemplate? GetTemplate(string key, string lang)
    {
        if (!Templates.TryGetValue(key, out var templates)) return null;
        return templates.FirstOrDefault(t => t.Language == lang)
            ?? templates.FirstOrDefault();
    }
}

public class ClarificationResult
{
    public bool NeedsClarification { get; set; }
    public string Question { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public string Reason { get; set; } = "";
}

public record ClarificationTemplate(
    string Language,
    string Question,
    List<string> Options);
