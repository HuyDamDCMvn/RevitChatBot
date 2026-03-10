using RevitChatBot.Core.Skills;

namespace RevitChatBot.Knowledge.Search;

/// <summary>
/// Skill that allows the LLM to explicitly search the knowledge base.
/// Useful when the LLM needs to look up standards, specs, or reference data.
/// </summary>
[Skill("search_knowledge_base",
    "Search the MEP standards and reference knowledge base for relevant information. " +
    "Use this when you need to look up codes, standards, specifications, or technical references.")]
[SkillParameter("query", "string", "The search query describing what information to find", isRequired: true)]
[SkillParameter("top_k", "integer", "Number of results to return (default: 5)", isRequired: false)]
public class KnowledgeSearchSkill : ISkill
{
    private readonly KnowledgeManager _knowledgeManager;

    public KnowledgeSearchSkill(KnowledgeManager knowledgeManager)
    {
        _knowledgeManager = knowledgeManager;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var query = parameters.GetValueOrDefault("query")?.ToString();
        if (string.IsNullOrWhiteSpace(query))
            return SkillResult.Fail("Parameter 'query' is required.");

        var topK = 5;
        if (parameters.GetValueOrDefault("top_k") is int k) topK = k;
        else if (parameters.GetValueOrDefault("top_k") is string ks && int.TryParse(ks, out var kp)) topK = kp;

        var results = await _knowledgeManager.SearchAsync(query, topK, cancellationToken);
        if (results.Count == 0)
            return SkillResult.Ok("No relevant documents found in the knowledge base.");

        var entries = results.Select(r => new
        {
            source = r.Entry.Metadata.GetValueOrDefault("source", "unknown"),
            category = r.Entry.Metadata.GetValueOrDefault("category", ""),
            relevance = Math.Round(r.Score, 3),
            content = r.Entry.Text
        }).ToList();

        return SkillResult.Ok($"Found {results.Count} relevant documents.", entries);
    }
}
