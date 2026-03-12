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
        new("kiểm tra vận tốc gió", "check", "duct", "check_mep_velocity", "category=\"duct\", maxVelocity=8"),
        new("kiểm tra vận tốc pipe cấp lạnh", "check", "pipe", "check_mep_velocity", "category=\"pipe\", system_name=\"Chilled Water\""),
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
        new("check duct velocity violations", "check", "duct", "check_mep_velocity", "category=\"duct\""),
        new("check pipe velocity", "check", "pipe", "check_mep_velocity", "category=\"pipe\""),
        new("copy fittings from Level 1 to Level 2", "modify", null, "copy_elements_to_level", "source_level=\"Level 1\", target_level=\"Level 2\", category=\"fittings\""),
        new("export duct schedule to csv", "export", "duct", "export_schedule_data", "schedule_name=\"Duct Schedule\", format=\"csv\""),
        new("report fitting count per level", "report", null, "aggregate_report", "category=\"fittings\", group_by=\"Level\", aggregate=\"count\""),
        new("thống kê tổng chiều dài pipe theo system", "report", "pipe", "aggregate_report", "category=\"pipes\", group_by=\"System Name\", aggregate=\"sum\", value_parameter=\"Length\""),
        new("thống kê element theo category", "report", null, "aggregate_report", "category=\"all_mep\", group_by=\"Category\""),
        new("count all MEP elements by category", "report", null, "aggregate_report", "category=\"all_mep\", group_by=\"Category\""),
        new("đếm tổng element trong model", "report", null, "aggregate_report", "category=\"all_mep\", group_by=\"Category\""),
        new("bao nhiêu element theo từng category", "report", null, "aggregate_report", "category=\"all_mep\", group_by=\"Category\""),
        new("check headroom from ceiling to duct", "check", "duct", "check_clearance", "reference=\"ceiling\", minHeight=0.3"),
        new("check clash duct vs beam", "check", null, "clash_detection", "category_a=\"duct\", category_b=\"beam\""),
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

        // === Vietnamese - Selection-aware ===
        new("gán Mark cho các duct đã chọn", "modify", "duct", "batch_modify", "source=\"selected\", parameterName=\"Mark\", value=\"...\""),
        new("kiểm tra thông tin các đối tượng đang chọn", "query", null, "inspect_element", "source=\"selected\""),
        new("export dữ liệu các đối tượng đang chọn ra CSV", "modify", null, "export_to_csv", "source=\"selected\""),
        new("tô màu đỏ các đối tượng đã chọn", "modify", null, "override_element_color", "source=\"selected\", color=\"red\""),
        new("lưu selection hiện tại với tên 'ống tầng 1'", "modify", null, "selection_set", "action=\"save\", name=\"ống tầng 1\""),
        new("map source param vào target param cho các đối tượng đang chọn", "modify", null, "map_parameters", "source=\"selected\", source_parameter=\"...\", target_parameter=\"...\""),
        new("highlight các đối tượng đang chọn", "modify", null, "highlight_elements", "source=\"selected\", severity=\"info\""),

        // === English - Selection-aware ===
        new("set Mark on selected elements", "modify", null, "batch_modify", "source=\"selected\", parameterName=\"Mark\", value=\"...\""),
        new("inspect the currently selected elements", "query", null, "inspect_element", "source=\"selected\""),
        new("export selected elements to CSV", "modify", null, "export_to_csv", "source=\"selected\""),
        new("color selected elements red", "modify", null, "override_element_color", "source=\"selected\", color=\"red\""),
        new("save current selection as 'my set'", "modify", null, "selection_set", "action=\"save\", name=\"my set\""),

        // === Vietnamese - Data table mapping ===
        new("map bảng dữ liệu này vào duct theo Size", "modify", "duct", "map_data_table",
            "data_source=\"inline\", match_rules=[{table_column:\"Size\",element_parameter:\"Size\",operator:\"equals\"}], mappings=[...]"),
        new("import dữ liệu từ CSV vào pipe, match theo Mark", "modify", "pipe", "batch_update_from_csv",
            "match_column=\"Mark\", match_parameter=\"Mark\", category=\"pipes\""),
        new("gán giá trị từ bảng schedule vào elements", "modify", null, "map_data_table",
            "data_source=\"schedule:Equipment Schedule\", match_rules=[...], mappings=[...]"),

        // === English - Data table mapping ===
        new("map this table to ducts on Level 1 matching by Size", "modify", "duct", "map_data_table",
            "data_source=\"inline\", category=\"ducts\", level_filter=\"Level 1\", match_rules=[{table_column:\"Size\",element_parameter:\"Size\"}]"),
        new("update pipes from CSV matching by Mark instead of ElementId", "modify", "pipe", "batch_update_from_csv",
            "match_column=\"Mark\", match_parameter=\"Mark\", category=\"pipes\""),
        new("import schedule data into MEP elements", "modify", null, "map_data_table",
            "data_source=\"schedule:...\", match_rules=[...], mappings=[...]"),

        // === Load / Mirror / Rotate ===
        new("load family FCU-600 vào project", "create", null, "load_family", "family_name=\"FCU-600\""),
        new("load family from path", "create", null, "load_family", "family_name=\"...\", family_path=\"C:\\...\\family.rfa\""),
        new("mirror AHU qua trục A", "modify", null, "mirror_elements", "element_ids=\"...\", axis=\"A\", copy=\"true\""),
        new("mirror elements across grid", "modify", null, "mirror_elements", "element_ids=\"...\", axis=\"grid_name\""),
        new("xoay quạt 90 độ", "modify", null, "rotate_elements", "element_ids=\"...\", angle_degrees=90"),
        new("rotate equipment 45 degrees", "modify", null, "rotate_elements", "element_ids=\"...\", angle_degrees=45"),

        // === Model Health / Coordination ===
        new("model health check", "check", null, "model_health_check", ""),
        new("file size model bao nhiêu", "query", null, "model_health_check", ""),
        new("bao nhiêu warning trong model", "check", null, "model_health_check", "include_details=\"true\""),
        new("coordination report cho meeting", "analyze", null, "coordination_report", ""),
        new("tổng hợp clash theo tầng", "analyze", null, "coordination_report", ""),
        new("clash summary for coordination meeting", "analyze", null, "coordination_report", "scope=\"entire_model\""),

        // === Progress / Templates / Shared Params ===
        new("tiến độ model bao nhiêu phần trăm", "query", null, "model_progress", "discipline=\"all\""),
        new("mechanical modeling xong chưa", "query", null, "model_progress", "discipline=\"mechanical\""),
        new("model progress for Level 2", "query", null, "model_progress", "level=\"Level 2\""),
        new("kiểm tra view template", "check", null, "view_template_audit", "action=\"full\""),
        new("view nào chưa có template", "check", null, "view_template_audit", "action=\"orphan\""),
        new("audit view templates", "check", null, "view_template_audit", "action=\"full\""),
        new("kiểm tra shared parameter", "check", null, "check_shared_parameters", "action=\"audit\""),
        new("shared parameter setup đúng chưa", "check", null, "check_shared_parameters", "action=\"audit\""),
        new("validate shared parameters against BEP", "check", null, "check_shared_parameters",
            "action=\"validate\", expected_parameters=\"COBie.Type.Name,Status\""),

        // === Workset / Pin ===
        new("chuyển duct tầng 3 sang workset HVAC", "modify", "duct", "workset_reassign",
            "target_workset=\"HVAC\", category=\"ducts\", level=\"Level 3\""),
        new("move pipes to Plumbing workset", "modify", "pipe", "workset_reassign",
            "target_workset=\"Plumbing\", category=\"pipes\""),
        new("pin hết linked model", "modify", null, "pin_unpin_elements", "action=\"pin\", category=\"links\""),
        new("unpin AHU để dời", "modify", null, "pin_unpin_elements", "action=\"unpin\", element_ids=\"...\""),
        new("tạo workset mới tên Plumbing", "create", null, "create_workset",
            "action=\"create\", workset_name=\"Plumbing\""),
        new("liệt kê tất cả workset", "query", null, "create_workset", "action=\"list\""),

        // === Phase ===
        new("tìm pipe giai đoạn existing", "query", "pipe", "advanced_filter",
            "category=\"pipes\", phase=\"Existing\""),
        new("đếm duct phase New Construction", "query", "duct", "advanced_filter",
            "category=\"ducts\", phase=\"New Construction\""),

        // === Callout / Dependent / Text / Cloud / Scope Box ===
        new("tạo callout quanh phòng máy", "create", null, "create_callout_view",
            "mode=\"callout\", around_element_id=\"...\""),
        new("tạo dependent view cho plan tầng 3", "create", null, "create_callout_view",
            "mode=\"dependent\""),
        new("ghi text note cảnh báo clash", "create", null, "create_text_note",
            "text=\"CHÚ Ý: clash area\", near_element_id=\"...\""),
        new("ghi revision cloud quanh khu vực sửa", "create", null, "create_revision_cloud",
            "around_element_ids=\"...\", comments=\"Changed routing\""),
        new("gán scope box Zone A cho view plan tầng 2", "modify", null, "manage_scope_box",
            "action=\"assign\", scope_box_name=\"Zone A\", view_name_pattern=\"Level 2\""),
        new("liệt kê scope box", "query", null, "manage_scope_box", "action=\"list\""),

        // === Lazy-user / vague / overview prompts ===
        new("tóm tắt model cho tôi", "query", null, "mep_system_overview", "discipline=\"all\""),
        new("cho tôi xem tổng quan model", "query", null, "mep_system_overview", "discipline=\"all\""),
        new("model nặng không?", "check", null, "model_health_check", ""),
        new("có bao nhiêu view trong project", "check", null, "model_health_check", ""),
        new("model có link gì không", "check", null, "link_model_analysis", ""),
        new("tổng hợp element theo level", "report", null, "aggregate_report", "category=\"all_mep\", group_by=\"Level\""),
        new("thống kê theo type", "report", null, "aggregate_report", "category=\"all_mep\", group_by=\"Type\""),
        new("thống kê theo system name", "report", null, "aggregate_report", "category=\"all_mep\", group_by=\"System Name\""),
        new("check model", "check", null, "model_audit", ""),
        new("có vấn đề gì không", "check", null, "model_audit", ""),
        new("element này là gì", "query", null, "inspect_element", "source=\"selected\""),
        new("tìm cái bị sai", "check", null, "model_audit", ""),
        new("check clash", "check", null, "clash_detection", "category_a=\"all_mep\""),
        new("check velocity", "check", null, "check_mep_velocity", "category=\"all\""),
        new("tag hết element chưa có tag", "modify", null, "place_tags", "category=\"all_mep\""),
        new("export tất cả schedule ra excel", "export", null, "export_schedule_data", "action=\"export\", schedule_name=\"all\", format=\"csv\""),
        new("so sánh element giữa tầng 1 và tầng 2", "report", null, "(multi-step)",
            "Run: aggregate_report(all_mep, level=Level 1) + aggregate_report(all_mep, level=Level 2)"),
        new("workset đang ai own gì", "query", null, "workset_reassign", "action=\"audit\""),
        new("có element nào nằm sai workset không", "query", null, "workset_reassign", "action=\"audit\""),
        new("pipe tầng 2 có vấn đề gì không", "check", "pipe", "(multi-step)",
            "Run: check_pipe_slope(level=Level 2) + check_mep_velocity(category=pipe) + check_disconnected_mep(level=Level 2)"),
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
