using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Preprocesses user queries before sending to LLM.
/// Uses MepGlossary for fast keyword-based extraction, with optional
/// /api/generate structured output fallback for complex queries.
/// </summary>
public class QueryPreprocessor
{
    private readonly HttpClient? _httpClient;
    private readonly string _model;

    public QueryPreprocessor(HttpClient? httpClient = null, string model = "qwen2.5:7b")
    {
        _httpClient = httpClient;
        _model = model;
    }

    /// <summary>
    /// Fast preprocessing using keyword-based extraction (no LLM call).
    /// </summary>
    public QueryAnalysis AnalyzeFast(string query)
    {
        return new QueryAnalysis
        {
            OriginalQuery = query,
            Language = MepGlossary.DetectLanguage(query),
            Intent = MepGlossary.DetectIntent(query),
            Category = MepGlossary.DetectCategory(query),
            Level = MepGlossary.ExtractLevel(query),
            SystemType = MepGlossary.DetectSystemType(query),
            ElementIds = MepGlossary.ExtractElementIds(query),
            NormalizedQuery = MepGlossary.NormalizeQuery(query),
            IsAmbiguous = CheckAmbiguity(query)
        };
    }

    /// <summary>
    /// Deep preprocessing using /api/generate with structured output.
    /// Only called for ambiguous or complex queries.
    /// </summary>
    public async Task<QueryAnalysis> AnalyzeDeepAsync(
        string query, CancellationToken ct = default)
    {
        var fast = AnalyzeFast(query);

        if (_httpClient == null || !fast.IsAmbiguous)
            return fast;

        try
        {
            var payload = new JsonObject
            {
                ["model"] = _model,
                ["prompt"] = $"""
                    Analyze this MEP engineering query and extract structured information.
                    Query: "{query}"
                    
                    Rules:
                    - intent: what the user wants to do
                    - category: MEP element type (duct/pipe/conduit/cable_tray/equipment/fitting/all)
                    - system_type: MEP system (ChilledWater/HotWater/Sanitary/SupplyAir/ReturnAir/ExhaustAir/FireProtection/null)
                    - level: building level name if mentioned (e.g., "Level 1")
                    - needs_clarification: true if the query is too vague to act on
                    - clarification_question: what to ask if needs_clarification is true
                    """,
                ["stream"] = false,
                ["format"] = JsonNode.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "intent": {"type": "string", "enum": ["query","check","modify","calculate","create","delete","explain","analyze","report"]},
                            "category": {"type": "string"},
                            "system_type": {"type": "string"},
                            "level": {"type": "string"},
                            "needs_clarification": {"type": "boolean"},
                            "clarification_question": {"type": "string"}
                        },
                        "required": ["intent", "needs_clarification"]
                    }
                    """),
                ["options"] = new JsonObject
                {
                    ["temperature"] = 0.1,
                    ["num_ctx"] = 2048
                }
            };

            var content = new System.Net.Http.StringContent(
                payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/generate", content, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var node = JsonNode.Parse(json);
            var responseText = node?["response"]?.GetValue<string>();

            if (!string.IsNullOrEmpty(responseText))
            {
                var parsed = JsonNode.Parse(responseText);
                if (parsed != null)
                {
                    fast.Intent = parsed["intent"]?.GetValue<string>() ?? fast.Intent;
                    var cat = parsed["category"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(cat) && cat != "null") fast.Category = cat;
                    var sys = parsed["system_type"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(sys) && sys != "null") fast.SystemType = sys;
                    var lvl = parsed["level"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(lvl) && lvl != "null") fast.Level = lvl;
                    fast.NeedsClarification = parsed["needs_clarification"]?.GetValue<bool>() ?? false;
                    fast.ClarificationQuestion = parsed["clarification_question"]?.GetValue<string>();
                    fast.IsAmbiguous = fast.NeedsClarification;
                    fast.UsedDeepAnalysis = true;
                }
            }
        }
        catch
        {
            // Fall back to fast analysis on error
        }

        return fast;
    }

    private static bool CheckAmbiguity(string query)
    {
        var lower = query.ToLowerInvariant().Trim();
        if (lower.Length < 8) return true;

        var category = MepGlossary.DetectCategory(query);
        var intent = MepGlossary.DetectIntent(query);

        bool vagueCategory = category == null &&
            (lower.Contains("ống") || lower.Contains("ong") || lower.Contains("pipe") || lower.Contains("element"));

        bool vagueAction = intent == "query" && !lower.Contains("đếm") && !lower.Contains("liệt kê")
            && !lower.Contains("count") && !lower.Contains("list") && !lower.Contains("show")
            && lower.Length < 20;

        return vagueCategory || vagueAction;
    }
}

public class QueryAnalysis
{
    public string OriginalQuery { get; set; } = "";
    public string Language { get; set; } = "en";
    public string Intent { get; set; } = "query";
    public string? Category { get; set; }
    public string? Level { get; set; }
    public string? SystemType { get; set; }
    public List<long> ElementIds { get; set; } = [];
    public string NormalizedQuery { get; set; } = "";
    public bool IsAmbiguous { get; set; }
    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
    public bool UsedDeepAnalysis { get; set; }

    public string GetPromptHint()
    {
        var parts = new List<string>();
        parts.Add($"[User intent: {Intent}]");
        if (Category != null) parts.Add($"[Category: {Category}]");
        if (SystemType != null) parts.Add($"[System: {SystemType}]");
        if (Level != null) parts.Add($"[Level: {Level}]");
        if (ElementIds.Count > 0) parts.Add($"[Element IDs: {string.Join(",", ElementIds)}]");
        return string.Join(" ", parts);
    }
}
