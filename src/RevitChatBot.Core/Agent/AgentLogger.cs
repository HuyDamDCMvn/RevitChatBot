using System.Text.Json;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Structured logging for agent operations: tool traces, RAG provenance,
/// Ollama metrics, and warning deltas. Writes JSONL to a daily log file.
/// No PII is stored — only element IDs, tool names, scores, and durations.
/// </summary>
public class AgentLogger : IDisposable
{
    private readonly string _logDir;
    private StreamWriter? _writer;
    private string _currentDate = "";

    public AgentLogger(string logDir)
    {
        _logDir = logDir;
        Directory.CreateDirectory(logDir);
    }

    public void LogToolExecution(string toolName, Dictionary<string, object?>? args,
        bool success, double durationMs, WarningsDelta? warningsDelta = null)
    {
        Write(new
        {
            type = "tool_trace",
            ts = DateTime.UtcNow.ToString("o"),
            tool = toolName,
            args_hash = args != null ? ComputeArgsHash(args) : null,
            success,
            duration_ms = Math.Round(durationMs, 1),
            warnings_delta = warningsDelta?.Delta,
            warnings_new = warningsDelta?.NewWarnings?.Take(3).ToList()
        });
    }

    public void LogRagRetrieval(string query, int resultsCount, double topScore,
        List<string>? sourceIds = null)
    {
        Write(new
        {
            type = "retrieval_provenance",
            ts = DateTime.UtcNow.ToString("o"),
            query_hash = query.GetHashCode().ToString("x8"),
            results_count = resultsCount,
            top_score = Math.Round(topScore, 4),
            source_ids = sourceIds?.Take(5).ToList()
        });
    }

    public void LogLlmCall(string model, int promptTokens, int evalTokens,
        double totalDurationMs, bool hasToolCalls, string? activeEndpoint = null)
    {
        Write(new
        {
            type = "ollama_metrics",
            ts = DateTime.UtcNow.ToString("o"),
            model,
            prompt_eval_count = promptTokens,
            eval_count = evalTokens,
            total_duration_ms = Math.Round(totalDurationMs, 1),
            has_tool_calls = hasToolCalls,
            endpoint = activeEndpoint
        });
    }

    public void LogContextSnapshot(string? viewType, int? selectionCount,
        List<string>? categories, string automationMode)
    {
        Write(new
        {
            type = "revit_context_used",
            ts = DateTime.UtcNow.ToString("o"),
            view_type = viewType,
            selection_count = selectionCount,
            categories,
            automation_mode = automationMode
        });
    }

    public void LogActionPlanReview(string planSummary, int actionCount,
        bool approved, string? riskLevel = null)
    {
        Write(new
        {
            type = "action_plan_review",
            ts = DateTime.UtcNow.ToString("o"),
            summary_hash = planSummary.GetHashCode().ToString("x8"),
            action_count = actionCount,
            approved,
            risk_level = riskLevel
        });
    }

    private void Write(object entry)
    {
        try
        {
            EnsureWriter();
            var json = JsonSerializer.Serialize(entry,
                new JsonSerializerOptions { WriteIndented = false });
            _writer?.WriteLine(json);
            _writer?.Flush();
        }
        catch { }
    }

    private void EnsureWriter()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (today != _currentDate || _writer is null)
        {
            _writer?.Dispose();
            _currentDate = today;
            var path = Path.Combine(_logDir, $"agent_{today}.jsonl");
            _writer = new StreamWriter(path, append: true) { AutoFlush = false };
        }
    }

    private static string? ComputeArgsHash(Dictionary<string, object?> args)
    {
        var keys = string.Join(",", args.Keys.OrderBy(k => k));
        return keys.GetHashCode().ToString("x8");
    }

    public void Dispose()
    {
        _writer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
