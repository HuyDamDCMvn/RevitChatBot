namespace RevitChatBot.Core.LLM;

/// <summary>
/// Curated few-shot examples mapping user queries to expected skill calls.
/// Injected into context (3-5 relevant examples) to guide skill selection.
/// </summary>
public static class FewShotIntentLibrary
{
    private static readonly List<FewShotExample> AllExamples =
    [
        // === Vietnamese - Query ===
        new("đếm ống gió theo tầng", "query", "duct", "query_elements", "category=\"duct\", group_by=\"level\""),
        new("liệt kê tất cả ống nước", "query", "pipe", "query_elements", "category=\"pipe\""),
        new("bao nhiêu thiết bị cơ", "query", "equipment", "query_elements", "category=\"mechanical_equipment\""),
        new("tổng quan hệ thống MEP", "query", null, "mep_system_overview", ""),
        new("cho xem hệ thống ống", "query", "pipe", "system_analysis", ""),
        new("phòng nào có ống đi qua", "query", "pipe", "map_room_to_mep", "mode=\"report\""),
        new("duyệt hệ thống từ ống này", "query", null, "traverse_mep_system", "element_id=..., include_path=\"yes\""),
        new("routing preference của pipe type", "query", "pipe", "query_routing_preferences", "category=\"pipe\""),

        // === Vietnamese - Check ===
        new("kiểm tra bảo ôn", "check", null, "check_insulation", ""),
        new("kiểm tra bảo ôn ống lạnh", "check", "pipe", "check_insulation", "system_filter=\"ChilledWater\""),
        new("ống nào chưa kết nối", "check", null, "check_disconnected_mep", ""),
        new("kiểm tra vận tốc gió", "check", "duct", "check_duct_velocity", "max_velocity_ms=8"),
        new("kiểm tra độ dốc ống thoát nước", "check", "pipe", "check_pipe_slope", "min_slope_pct=1"),
        new("kiểm tra van chống cháy", "check", null, "check_fire_dampers", ""),
        new("kiểm tra va chạm ống gió và ống nước", "check", null, "clash_detection", "category_a=\"duct\", category_b=\"pipe\""),
        new("va chạm ở tầng 2", "check", null, "clash_detection", "level_name=\"Level 2\""),
        new("clearance ống gió so với tường", "check", "duct", "check_directional_clearance", "category=\"duct\", directions=\"all\""),
        new("audit model", "check", null, "model_audit", ""),

        // === Vietnamese - Modify ===
        new("chia ống thành đoạn 2m", "modify", null, "split_duct_pipe", "split_distance_mm=2000"),
        new("set comment cho tất cả duct", "modify", "duct", "batch_modify", "category=\"duct\", parameter=\"Comments\", value=\"...\""),
        new("tránh va chạm tự động", "modify", null, "avoid_clash", "mode=\"analyze\""),
        new("ghi số phòng vào ống", "modify", null, "map_room_to_mep", "mode=\"execute\""),

        // === Vietnamese - Calculate ===
        new("tính kích thước ống gió 500 L/s", "calculate", "duct", "execute_revit_code", "description=\"Duct sizing for 500 L/s\""),
        new("tính kích thước ống nước DN100", "calculate", "pipe", "execute_revit_code", "description=\"Pipe sizing verification DN100\""),

        // === Vietnamese - Explain ===
        new("giải thích ISO 19650", "explain", null, "(direct LLM answer)", "No skill needed - use RAG knowledge"),
        new("tiêu chuẩn ASHRAE cho vận tốc ống gió", "explain", "duct", "search_knowledge_base", "query=\"ASHRAE duct velocity\""),

        // === English - Query ===
        new("count all pipes by system", "query", "pipe", "query_elements", "category=\"pipe\", group_by=\"system\""),
        new("list ducts on Level 3", "query", "duct", "query_elements", "category=\"duct\", level_name=\"Level 3\""),
        new("show MEP system summary", "query", null, "mep_system_overview", ""),
        new("trace CHW system from element 12345", "query", "pipe", "traverse_mep_system", "element_id=12345, domain_filter=\"piping\""),

        // === English - Check ===
        new("check duct velocity violations", "check", "duct", "check_duct_velocity", ""),
        new("find disconnected elements", "check", null, "check_disconnected_mep", ""),
        new("check insulation on chilled water pipes", "check", "pipe", "check_insulation", "system_filter=\"ChilledWater\""),
        new("check clashes between ducts and pipes", "check", null, "clash_detection", "category_a=\"duct\", category_b=\"pipe\""),
        new("full QA/QC check", "check", null, "(multi-step)", "Run full QA workflow: overview → disconnect → insulation → fire damper → velocity → slope → clash"),
        new("run model audit", "check", null, "model_audit", ""),

        // === English - Modify ===
        new("split all ducts into 3m segments", "modify", "duct", "split_duct_pipe", "category=\"duct\", split_distance_mm=3000"),
        new("reroute clashing pipes", "modify", "pipe", "avoid_clash", "shift_category=\"pipe\", mode=\"analyze\""),
        new("set room numbers on all MEP elements", "modify", null, "map_room_to_mep", "mode=\"execute\""),

        // === English - Calculate ===
        new("calculate duct size for 1000 CFM", "calculate", "duct", "execute_revit_code", "description=\"Duct sizing for 1000 CFM\""),
    ];

    /// <summary>
    /// Get the most relevant few-shot examples for a given query analysis.
    /// Returns 3-5 examples sorted by relevance.
    /// </summary>
    public static List<FewShotExample> GetRelevantExamples(QueryAnalysis analysis, int maxExamples = 5)
    {
        var scored = AllExamples.Select(ex =>
        {
            double score = 0;

            if (ex.Intent == analysis.Intent) score += 3.0;

            if (analysis.Category != null && ex.Category == analysis.Category) score += 2.0;
            if (analysis.Category == null && ex.Category == null) score += 0.5;

            if (analysis.Language == "vi" &&
                !ex.Query.Any(c => c >= 'A' && c <= 'z' && char.IsAscii(c) && ex.Query.All(ch => ch < 128)))
                score += 0.5;
            if (analysis.Language == "en" && ex.Query.All(c => c < 128))
                score += 0.5;

            return (example: ex, score);
        })
        .OrderByDescending(x => x.score)
        .Take(maxExamples)
        .Select(x => x.example)
        .ToList();

        return scored;
    }

    /// <summary>
    /// Format examples as a prompt string for injection into LLM context.
    /// </summary>
    public static string FormatExamplesForPrompt(List<FewShotExample> examples)
    {
        if (examples.Count == 0) return "";

        var lines = new List<string> { "--- FEW-SHOT EXAMPLES (follow these patterns) ---" };
        foreach (var ex in examples)
        {
            lines.Add($"  User: \"{ex.Query}\"");
            lines.Add($"  → Skill: {ex.ExpectedSkill}({ex.ExpectedParams})");
        }
        return string.Join("\n", lines);
    }
}

public record FewShotExample(
    string Query,
    string Intent,
    string? Category,
    string ExpectedSkill,
    string ExpectedParams);
