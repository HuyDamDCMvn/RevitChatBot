using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Embedding provider delegate. Injected from Knowledge layer to avoid circular reference.
/// </summary>
public interface ISkillEmbeddingProvider
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
    Task<List<float[]>> GetEmbeddingsAsync(List<string> texts, CancellationToken ct = default);
}

/// <summary>
/// Pre-filters skills using embedding similarity before sending to LLM.
/// Reduces token usage by only presenting relevant skills as tool definitions.
/// Falls back to keyword matching if embedding service is unavailable.
/// </summary>
public class SemanticSkillRouter
{
    private readonly ISkillEmbeddingProvider? _embeddingProvider;
    private readonly Dictionary<string, float[]> _skillEmbeddings = new();
    private bool _initialized;

    private const int DefaultTopK = 10;
    private const double SimilarityThreshold = 0.3;

    public SemanticSkillRouter(ISkillEmbeddingProvider? embeddingProvider = null)
    {
        _embeddingProvider = embeddingProvider;
    }

    /// <summary>
    /// Pre-compute embeddings for all registered skills.
    /// Call once during initialization.
    /// </summary>
    public async Task InitializeAsync(IEnumerable<SkillDescriptor> skills, CancellationToken ct = default)
    {
        if (_embeddingProvider == null) return;

        try
        {
            var skillList = skills.ToList();
            var texts = skillList.Select(s => $"{s.Name}: {s.Description}").ToList();
            var embeddings = await _embeddingProvider.GetEmbeddingsAsync(texts, ct);

            for (int i = 0; i < skillList.Count; i++)
                _skillEmbeddings[skillList[i].Name] = embeddings[i];

            _initialized = true;
        }
        catch
        {
            _initialized = false;
        }
    }

    /// <summary>
    /// Route query to the most relevant skills. Returns filtered and ranked skills.
    /// </summary>
    public async Task<List<SkillDescriptor>> RouteAsync(
        string query,
        QueryAnalysis? analysis,
        IEnumerable<SkillDescriptor> allSkills,
        int topK = DefaultTopK,
        CancellationToken ct = default)
    {
        var skillList = allSkills.ToList();

        if (_initialized && _embeddingProvider != null)
        {
            try
            {
                return await SemanticRouteAsync(query, skillList, topK, ct);
            }
            catch
            {
                // Fall through to keyword routing
            }
        }

        return KeywordRoute(query, analysis, skillList, topK);
    }

    private async Task<List<SkillDescriptor>> SemanticRouteAsync(
        string query,
        List<SkillDescriptor> allSkills,
        int topK,
        CancellationToken ct)
    {
        var queryEmbedding = await _embeddingProvider!.GetEmbeddingAsync(query, ct);

        var scored = allSkills
            .Select(s =>
            {
                double similarity = 0;
                if (_skillEmbeddings.TryGetValue(s.Name, out var skillEmb))
                    similarity = CosineSimilarity(queryEmbedding, skillEmb);
                return (skill: s, similarity);
            })
            .Where(x => x.similarity >= SimilarityThreshold)
            .OrderByDescending(x => x.similarity)
            .Take(topK)
            .Select(x => x.skill)
            .ToList();

        if (scored.Count < 3)
        {
            var missing = allSkills
                .Where(s => !scored.Any(x => x.Name == s.Name))
                .Take(3 - scored.Count);
            scored.AddRange(missing);
        }

        return scored;
    }

    private static readonly HashSet<string> DataMappingKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "mapping", "map", "bảng", "table", "excel", "import", "match", "gán",
        "csv", "schedule", "dữ liệu", "data", "update from", "batch update"
    };

    private static readonly HashSet<string> SelectionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "selected", "đã chọn", "chọn", "selection", "đang chọn", "các đối tượng đã chọn",
        "highlighted", "current selection", "giống", "tương tự", "similar"
    };

    private static readonly HashSet<string> RevisionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "revision", "rev", "cloud", "issued", "phát hành", "bản sửa", "revision cloud"
    };

    private static readonly HashSet<string> SheetKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sheet", "viewport", "title block", "bản vẽ", "trang", "sheets"
    };

    private static readonly HashSet<string> ExportKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "export", "ifc", "nwc", "navisworks", "xuất", "pdf", "dwg", "image",
        "schedule", "bảng thống kê", "csv", "excel"
    };

    private static readonly HashSet<string> VelocityKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "velocity", "vận tốc", "tốc độ", "speed", "flow speed", "pipe velocity", "duct velocity"
    };

    private static readonly HashSet<string> CopyKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "copy to level", "copy lên tầng", "sao chép", "nhân bản", "duplicate to level",
        "copy between levels", "copy elements"
    };

    private static readonly HashSet<string> AggregateKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "thống kê", "report", "aggregate", "nhóm theo", "group by", "theo từng tầng",
        "theo system", "breakdown", "summary report", "tổng hợp", "count per"
    };

    private static readonly HashSet<string> HeadroomKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "headroom", "clearance", "khoảng trống", "trần", "ceiling", "floor above",
        "chiều cao thông thủy"
    };

    private static readonly HashSet<string> ChangeTypeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "change type", "đổi type", "swap type", "thay type", "convert type",
        "change family", "đổi size", "thay size"
    };

    private static readonly HashSet<string> MoveKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "di chuyển", "dịch chuyển", "move", "offset", "nâng lên", "hạ xuống",
        "lên cao", "xuống thấp", "shift", "dời"
    };

    private static readonly HashSet<string> MeasureKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "khoảng cách", "distance", "measure", "đo", "cách nhau", "spacing between",
        "xa bao nhiêu"
    };

    private static readonly HashSet<string> LoadFamilyKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "load family", "tải family", "import family", "thêm family", "nạp family", "load rfa",
        "family library"
    };

    private static readonly HashSet<string> MirrorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "mirror", "gương", "đối xứng", "lật", "flip across", "qua trục"
    };

    private static readonly HashSet<string> RotateKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "rotate", "xoay", "quay", "turn", "orientation", "hướng", "90 độ", "45 độ"
    };

    private static readonly HashSet<string> ModelHealthKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "health", "file size", "warning count", "performance", "sức khỏe", "nặng", "dung lượng",
        "model size", "MB", "warnings"
    };

    private static readonly HashSet<string> CoordinationReportKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "coordination report", "coordination meeting", "clash summary", "tổng hợp clash",
        "trước meeting", "báo cáo coordination"
    };

    private static readonly HashSet<string> ProgressKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "progress", "tiến độ", "phần trăm", "% complete", "completion", "hoàn thành",
        "xong chưa", "modeling progress"
    };

    private static readonly HashSet<string> ViewTemplateKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "view template", "template audit", "mẫu view", "nhất quán", "orphan view",
        "unused template"
    };

    private static readonly HashSet<string> SharedParamKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "shared parameter", "parameter setup", "parameter binding", "tham số chia sẻ",
        "parameter check", "BEP compliance"
    };

    private static readonly HashSet<string> WorksetKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "workset", "chuyển workset", "move to workset", "reassign workset", "gán workset",
        "tạo workset", "create workset"
    };

    private static readonly HashSet<string> PinKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "pin", "unpin", "ghim", "bỏ ghim", "lock element", "pinned"
    };

    private static readonly HashSet<string> PhaseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "phase", "giai đoạn", "existing", "new construction", "demolish", "phá dỡ"
    };

    private static readonly HashSet<string> CalloutKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "callout", "dependent view", "enlarged", "phóng to", "chi tiết", "detail view"
    };

    private static readonly HashSet<string> RevCloudKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "revision cloud", "đám mây", "cloud around", "đánh dấu thay đổi"
    };

    private static readonly HashSet<string> TextNoteKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "text note", "ghi chú", "viết chữ", "add text", "annotation text"
    };

    private static readonly HashSet<string> ScopeBoxKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "scope box", "vùng nhìn", "assign scope box", "gán scope box"
    };

    private static List<SkillDescriptor> KeywordRoute(
        string query,
        QueryAnalysis? analysis,
        List<SkillDescriptor> allSkills,
        int topK)
    {
        var intent = analysis?.Intent ?? "query";
        var category = analysis?.Category;
        var queryLower = query.ToLowerInvariant();

        var hasDataMappingIntent = DataMappingKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasSelectionIntent = SelectionKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasRevisionIntent = RevisionKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasSheetIntent = SheetKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasExportIntent = ExportKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasVelocityIntent = VelocityKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasCopyIntent = CopyKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasAggregateIntent = AggregateKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasHeadroomIntent = HeadroomKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasChangeTypeIntent = ChangeTypeKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasMoveIntent = MoveKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasMeasureIntent = MeasureKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasLoadFamilyIntent = LoadFamilyKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasMirrorIntent = MirrorKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasRotateIntent = RotateKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasModelHealthIntent = ModelHealthKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasCoordinationReportIntent = CoordinationReportKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasProgressIntent = ProgressKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasViewTemplateIntent = ViewTemplateKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasSharedParamIntent = SharedParamKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasWorksetIntent = WorksetKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasPinIntent = PinKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasPhaseIntent = PhaseKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasCalloutIntent = CalloutKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasRevCloudIntent = RevCloudKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasTextNoteIntent = TextNoteKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));
        var hasScopeBoxIntent = ScopeBoxKeywords.Any(k => queryLower.Contains(k.ToLowerInvariant()));

        var scored = allSkills.Select(s =>
        {
            double score = 0;
            var desc = s.Description.ToLowerInvariant();
            var name = s.Name.ToLowerInvariant();

            if (queryLower.Split(' ').Any(w => w.Length > 2 && (desc.Contains(w) || name.Contains(w))))
                score += 2.0;

            score += intent switch
            {
                "query" when name.Contains("query") || name.Contains("overview") || name.Contains("traverse")
                    || name.Contains("find") || name.Contains("list") || name.Contains("sum") => 1.5,
                "check" when name.Contains("check") || name.Contains("audit") || name.Contains("clash")
                    || name.Contains("mirrored") || name.Contains("self_test") => 1.5,
                "modify" when name.Contains("modify") || name.Contains("batch") || name.Contains("split")
                    || name.Contains("avoid") || name.Contains("map") || name.Contains("create")
                    || name.Contains("set_crop") || name.Contains("restore") || name.Contains("copy") => 1.5,
                "create" when name.Contains("create") || name.Contains("execute") || name.Contains("batch") => 1.5,
                "calculate" when name.Contains("execute") || name.Contains("calculate") || name.Contains("sizing")
                    || name.Contains("sum") || name.Contains("centroid") => 1.5,
                "explain" when name.Contains("search") || name.Contains("knowledge") => 1.5,
                "analyze" when name.Contains("analysis") || name.Contains("overview") || name.Contains("audit")
                    || name.Contains("report") || name.Contains("find") => 1.5,
                "export" when name.Contains("export") || name.Contains("ifc") || name.Contains("report") => 1.5,
                _ => 0
            };

            if (category != null)
            {
                if (desc.Contains(category) || s.Parameters.Any(p =>
                    p.AllowedValues?.Any(v => v == category) == true))
                    score += 1.0;
            }

            if (hasDataMappingIntent)
            {
                if (name is "map_data_table" or "batch_update_from_csv" or "map_parameters" or "ingest_schedule")
                    score += 2.5;
            }

            if (hasSelectionIntent)
            {
                if (s.Parameters.Any(p => p.Name == "source" &&
                    p.AllowedValues?.Any(v => v == "selected") == true))
                    score += 2.0;
                if (name is "selection_set" or "inspect_element" or "highlight_elements" or "select_similar")
                    score += 1.5;
            }

            if (hasRevisionIntent)
            {
                if (name.Contains("revision") || name.Contains("cloud"))
                    score += 2.5;
            }

            if (hasSheetIntent)
            {
                if (name.Contains("sheet") || name.Contains("viewport") || name == "copy_view_to_sheet")
                    score += 2.0;
            }

            if (hasExportIntent)
            {
                if (name.Contains("export") || name == "export_ifc" || name == "export_schedule_data")
                    score += 2.0;
            }

            if (hasVelocityIntent)
            {
                if (name == "check_mep_velocity")
                    score += 3.0;
            }

            if (hasCopyIntent)
            {
                if (name == "copy_elements_to_level")
                    score += 3.0;
            }

            if (hasAggregateIntent)
            {
                if (name == "aggregate_report")
                    score += 3.0;
            }

            if (hasHeadroomIntent)
            {
                if (name == "check_clearance")
                    score += 2.5;
            }

            if (hasChangeTypeIntent)
            {
                if (name == "change_element_type")
                    score += 3.0;
            }

            if (hasMoveIntent)
            {
                if (name == "move_elements")
                    score += 3.0;
            }

            if (hasMeasureIntent)
            {
                if (name == "measure_distance")
                    score += 3.0;
            }

            if (hasLoadFamilyIntent)
            {
                if (name == "load_family")
                    score += 3.0;
            }

            if (hasMirrorIntent)
            {
                if (name == "mirror_elements")
                    score += 3.0;
            }

            if (hasRotateIntent)
            {
                if (name == "rotate_elements")
                    score += 3.0;
            }

            if (hasModelHealthIntent)
            {
                if (name == "model_health_check")
                    score += 3.0;
            }

            if (hasCoordinationReportIntent)
            {
                if (name == "coordination_report")
                    score += 3.0;
            }

            if (hasProgressIntent)
            {
                if (name == "model_progress")
                    score += 3.0;
            }

            if (hasViewTemplateIntent)
            {
                if (name == "view_template_audit")
                    score += 3.0;
            }

            if (hasSharedParamIntent)
            {
                if (name == "check_shared_parameters")
                    score += 3.0;
            }

            if (hasWorksetIntent)
            {
                if (name is "workset_reassign" or "create_workset" or "worksharing_info")
                    score += 3.0;
            }

            if (hasPinIntent)
            {
                if (name == "pin_unpin_elements")
                    score += 3.0;
            }

            if (hasPhaseIntent)
            {
                if (name == "advanced_filter")
                    score += 2.5;
            }

            if (hasCalloutIntent)
            {
                if (name == "create_callout_view")
                    score += 3.0;
            }

            if (hasRevCloudIntent)
            {
                if (name == "create_revision_cloud")
                    score += 3.0;
            }

            if (hasTextNoteIntent)
            {
                if (name == "create_text_note")
                    score += 3.0;
            }

            if (hasScopeBoxIntent)
            {
                if (name == "manage_scope_box")
                    score += 3.0;
            }

            if (name == "execute_revit_code") score += 0.5;
            if (name == "search_knowledge_base" && intent == "explain") score += 1.0;

            return (skill: s, score);
        })
        .OrderByDescending(x => x.score)
        .Take(topK)
        .Select(x => x.skill)
        .ToList();

        if (!scored.Any(s => s.Name == "execute_revit_code") && allSkills.Any(s => s.Name == "execute_revit_code"))
            scored.Add(allSkills.First(s => s.Name == "execute_revit_code"));

        return scored;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dot / denom;
    }
}
