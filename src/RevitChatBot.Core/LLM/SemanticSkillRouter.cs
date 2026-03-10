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

    private static List<SkillDescriptor> KeywordRoute(
        string query,
        QueryAnalysis? analysis,
        List<SkillDescriptor> allSkills,
        int topK)
    {
        var intent = analysis?.Intent ?? "query";
        var category = analysis?.Category;

        var scored = allSkills.Select(s =>
        {
            double score = 0;
            var desc = s.Description.ToLowerInvariant();
            var name = s.Name.ToLowerInvariant();
            var queryLower = query.ToLowerInvariant();

            if (queryLower.Split(' ').Any(w => w.Length > 2 && (desc.Contains(w) || name.Contains(w))))
                score += 2.0;

            score += intent switch
            {
                "query" when name.Contains("query") || name.Contains("overview") || name.Contains("traverse") => 1.5,
                "check" when name.Contains("check") || name.Contains("audit") || name.Contains("clash") => 1.5,
                "modify" when name.Contains("modify") || name.Contains("batch") || name.Contains("split") || name.Contains("avoid") || name.Contains("map") => 1.5,
                "create" when name.Contains("create") || name.Contains("execute") => 1.5,
                "calculate" when name.Contains("execute") || name.Contains("calculate") || name.Contains("sizing") => 1.5,
                "explain" when name.Contains("search") || name.Contains("knowledge") => 1.5,
                "analyze" when name.Contains("analysis") || name.Contains("overview") || name.Contains("audit") => 1.5,
                _ => 0
            };

            if (category != null)
            {
                if (desc.Contains(category) || s.Parameters.Any(p =>
                    p.AllowedValues?.Any(v => v == category) == true))
                    score += 1.0;
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
