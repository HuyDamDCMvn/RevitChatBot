using RevitChatBot.Core.Context;
using RevitChatBot.Core.Models;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Dynamically constructs system prompts based on query analysis.
/// Reduces prompt size by only including relevant sections for the detected intent.
/// Injects few-shot examples and query hints for better skill selection.
/// </summary>
public class AdaptivePromptBuilder
{
    private readonly PromptBuilder _basePromptBuilder;

    public AdaptivePromptBuilder(PromptBuilder? basePromptBuilder = null)
    {
        _basePromptBuilder = basePromptBuilder ?? new PromptBuilder();
    }

    /// <summary>
    /// Build messages with adaptive prompt based on query analysis.
    /// </summary>
    public List<ChatMessage> Build(
        List<ChatMessage> conversationHistory,
        ContextData? context,
        QueryAnalysis? analysis,
        IEnumerable<SkillDescriptor>? filteredSkills = null)
    {
        var messages = new List<ChatMessage>();

        var systemContent = BuildAdaptiveSystemPrompt(analysis);

        if (context is not null && context.Entries.Count > 0)
        {
            systemContent += "\n\n--- CURRENT CONTEXT ---\n";
            foreach (var entry in context.Entries)
                systemContent += $"\n[{entry.Key}]\n{entry.Value}\n";
        }

        if (analysis != null)
        {
            systemContent += $"\n\n--- QUERY ANALYSIS ---\n{analysis.GetPromptHint()}\n";

            var examples = FewShotIntentLibrary.GetRelevantExamples(analysis, 4);
            var fewShotBlock = FewShotIntentLibrary.FormatExamplesForPrompt(examples);
            if (!string.IsNullOrEmpty(fewShotBlock))
                systemContent += $"\n{fewShotBlock}\n";
        }

        messages.Add(ChatMessage.FromSystem(systemContent));
        messages.AddRange(conversationHistory);

        return messages;
    }

    public List<ToolDefinition> BuildToolDefinitions(IEnumerable<SkillDescriptor> skills)
    {
        return _basePromptBuilder.BuildToolDefinitions(skills);
    }

    private static string BuildAdaptiveSystemPrompt(QueryAnalysis? analysis)
    {
        var lang = analysis?.Language ?? "en";
        var intent = analysis?.Intent ?? "query";

        var sb = new System.Text.StringBuilder();

        sb.AppendLine(GetBaseRolePrompt(lang));
        sb.AppendLine(GetIntentSpecificGuidance(intent));

        if (intent is "calculate" or "create" or "modify")
            sb.AppendLine(GetDesignCriteriaSection());

        if (intent is "check" or "analyze")
            sb.AppendLine(GetRedFlagsSection());

        sb.AppendLine(GetResponseFormatSection(lang));

        return sb.ToString();
    }

    private static string GetBaseRolePrompt(string lang)
    {
        if (lang == "vi")
        {
            return """
                Bạn là trợ lý kỹ sư MEP (Cơ-Điện-Nước) chuyên nghiệp, tích hợp trong Autodesk Revit 2025.
                Luôn trả lời bằng tiếng Việt. Sử dụng thuật ngữ MEP chuẩn: ống gió, ống nước, thiết bị, hệ thống.
                
                QUAN TRỌNG:
                - Sử dụng tool/skill để thực hiện yêu cầu trong Revit.
                - Xác nhận trước khi thực hiện thao tác thay đổi model.
                - Trình bày kết quả dạng bảng markdown với Element ID.
                - Tham chiếu tiêu chuẩn TCVN, QCVN, ASHRAE, ISO khi tư vấn.
                """;
        }

        return """
            You are an expert MEP (Mechanical, Electrical, Plumbing) engineer assistant
            embedded inside Autodesk Revit 2025.
            
            RULES:
            - Use the available tools for Revit operations.
            - Confirm destructive actions before executing.
            - Format data tables in markdown with Element IDs.
            - Reference standards (TCVN, ASHRAE, SMACNA, NFPA, ISO 19650) when giving advice.
            """;
    }

    private static string GetIntentSpecificGuidance(string intent)
    {
        return intent switch
        {
            "query" => """
                ## QUERY MODE
                - Use query_elements for element counts and listings.
                - Use mep_system_overview for system summaries.
                - Use traverse_mep_system for network topology.
                - Group results by system or level. Include Element IDs.
                """,

            "check" => """
                ## QA/QC CHECK MODE
                Priority order: 1) Disconnected elements 2) Insulation 3) Fire dampers 
                4) Clearance 5) Velocity 6) Slope 7) Clashes
                - For "full QA" or "kiểm tra toàn bộ", run all checks sequentially.
                - Classify issues: Critical (🔴) / Major (🟠) / Minor (🟡)
                - Include remediation steps for each issue.
                """,

            "modify" => """
                ## MODIFY MODE — ALWAYS CONFIRM BEFORE EXECUTING
                - Explain what will be changed BEFORE executing.
                - Show affected element count and scope.
                - Use mode='analyze' first, then mode='execute' after confirmation.
                - For batch operations, show a preview of 3-5 elements first.
                """,

            "create" => """
                ## CREATE MODE — CONFIRM PARAMETERS BEFORE EXECUTING
                - Verify system type, level, and sizing before creating elements.
                - Reference design criteria for appropriate sizing.
                - Use execute_revit_code for complex creation tasks.
                """,

            "calculate" => """
                ## CALCULATION MODE
                - Show formulas and step-by-step calculation.
                - Provide both metric and imperial results.
                - Reference ASHRAE/SMACNA standards for sizing criteria.
                - Use execute_revit_code for complex calculations on model data.
                """,

            "explain" => """
                ## EXPLANATION MODE
                - Search knowledge base first for standards reference.
                - Provide clear, structured explanations.
                - Include relevant code examples or formulas when applicable.
                - Reference ISO, ASHRAE, TCVN standards as appropriate.
                """,

            "analyze" => """
                ## ANALYSIS MODE
                - Start with mep_system_overview for context.
                - Use multiple skills to build comprehensive analysis.
                - Present findings in structured format with severity levels.
                - Conclude with actionable recommendations.
                """,

            _ => """
                ## GENERAL MODE
                - Analyze what the user needs and choose the most appropriate skill.
                - Use ReAct reasoning: Think → Act → Observe → Answer.
                """
        };
    }

    private static string GetDesignCriteriaSection()
    {
        return """
            ## DESIGN CRITERIA
            Duct velocity: main 8-12 m/s, branch 4-6 m/s, exhaust 8-10 m/s
            Pipe velocity: CHW main 1.5-3.0 m/s, branch 1.0-1.5 m/s, FP 3.0-5.0 m/s
            Pipe slope: SAN DN50-75 ≥2%, DN100 ≥1%, DN150+ ≥0.5%
            Clearance: corridor 2.4m, office 2.6m, lobby 2.8-3.0m
            Duct sizing: A=Q/V, aspect ratio ≤4:1
            Pipe sizing: d=sqrt(4Q/πV), round to standard DN
            """;
    }

    private static string GetRedFlagsSection()
    {
        return """
            ## RED FLAGS
            - Duct velocity > 8 m/s in branch → noise
            - Pipe without insulation in CHW → condensation
            - Fire damper missing at fire-rated wall → CRITICAL
            - Pipe slope < min for DN → poor drainage
            - Clearance < 2.4m in corridor → obstruction
            - Disconnected elements → incomplete system
            """;
    }

    private static string GetResponseFormatSection(string lang)
    {
        if (lang == "vi")
        {
            return """
                ## ĐỊNH DẠNG KẾT QUẢ
                - Sử dụng bảng markdown cho danh sách (Element ID | Mô tả | Mức độ)
                - Nhóm theo hệ thống hoặc tầng
                - Đưa ra đề xuất hành động cụ thể
                - Bao gồm cả đơn vị metric và imperial
                """;
        }

        return """
            ## RESPONSE FORMAT
            - Use tables for listings (Element ID | Description | Severity)
            - Group results by system or level
            - Provide actionable recommendations
            - Include both metric and imperial units
            """;
    }
}
