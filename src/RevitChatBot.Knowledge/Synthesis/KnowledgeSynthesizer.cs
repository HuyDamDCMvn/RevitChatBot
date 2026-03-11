using RevitChatBot.Core.Agent;
using RevitChatBot.Core.LLM;
using RevitChatBot.Knowledge.Search;

namespace RevitChatBot.Knowledge.Synthesis;

/// <summary>
/// Synthesizes new knowledge articles from accumulated interaction records.
/// Clusters interactions by topic, asks the LLM to summarize, then indexes
/// the result into the RAG vector store for future retrieval.
/// </summary>
public class KnowledgeSynthesizer
{
    private readonly IOllamaService _ollama;
    private readonly KnowledgeManager _knowledgeManager;
    private readonly string _synthDir;

    public KnowledgeSynthesizer(
        IOllamaService ollama,
        KnowledgeManager knowledgeManager,
        string synthesizedKnowledgeDir)
    {
        _ollama = ollama;
        _knowledgeManager = knowledgeManager;
        _synthDir = synthesizedKnowledgeDir;
        Directory.CreateDirectory(_synthDir);
    }

    /// <summary>
    /// Synthesize knowledge from recent interactions. Groups by topic,
    /// generates summary articles, and indexes them for RAG retrieval.
    /// </summary>
    public async Task<int> SynthesizeFromInteractions(
        List<InteractionRecord> recentInteractions,
        CancellationToken ct = default)
    {
        if (recentInteractions.Count < 3) return 0;

        var clusters = ClusterByTopic(recentInteractions);
        int articlesCreated = 0;

        foreach (var (topic, interactions) in clusters)
        {
            if (interactions.Count < 2) continue;

            try
            {
                var article = await GenerateArticle(topic, interactions, ct);
                if (string.IsNullOrWhiteSpace(article)) continue;

                var fileName = $"synth_{DateTime.UtcNow:yyyyMMdd}_{SanitizeTopic(topic)}.md";
                var filePath = Path.Combine(_synthDir, fileName);

                if (File.Exists(filePath))
                    fileName = $"synth_{DateTime.UtcNow:yyyyMMddHHmm}_{SanitizeTopic(topic)}.md";
                filePath = Path.Combine(_synthDir, fileName);

                await File.WriteAllTextAsync(filePath, article, ct);
                await _knowledgeManager.IndexFileAsync(filePath, ct);
                articlesCreated++;
            }
            catch { /* non-critical: skip failed topic */ }
        }

        return articlesCreated;
    }

    private async Task<string> GenerateArticle(
        string topic, List<InteractionRecord> interactions, CancellationToken ct)
    {
        var qaPairs = string.Join("\n---\n", interactions
            .Take(15)
            .Select(i => $"Q: {i.Query}\nSkills: {string.Join(", ", i.SkillsUsed)}\nA: {i.Answer}"));

        var prompt = $"""
            You are a technical writer specializing in MEP (Mechanical, Electrical, Plumbing) engineering.
            
            Below are Q&A interactions from a Revit MEP chatbot on the topic "{topic}".
            Summarize them into a concise knowledge article that captures:
            1. Key facts and numerical values discovered
            2. Common patterns and issues found
            3. Best practices and recommendations
            4. Relevant standards or design criteria
            5. Project-specific conventions (family names, system types, etc.)
            
            Write in the same language as the interactions.
            Use markdown formatting with headers, bullet points, and tables where helpful.
            
            Interactions:
            {qaPairs}
            """;

        return await _ollama.GenerateAsync(prompt, temperature: 0.3, numCtx: 4096, cancellationToken: ct);
    }

    private static Dictionary<string, List<InteractionRecord>> ClusterByTopic(
        List<InteractionRecord> interactions)
    {
        var clusters = new Dictionary<string, List<InteractionRecord>>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in interactions)
        {
            var key = record.Topic ?? record.Intent ?? "general";
            if (!clusters.TryGetValue(key, out var list))
            {
                list = [];
                clusters[key] = list;
            }
            list.Add(record);
        }

        if (clusters.TryGetValue("general", out var general) && general.Count >= 5)
        {
            var subGroups = general
                .GroupBy(r => string.Join(",", r.SkillsUsed.Take(2)))
                .Where(g => g.Count() >= 2);

            foreach (var sg in subGroups)
            {
                var subKey = $"general_{sg.Key.Replace(",", "_")}";
                clusters[subKey] = sg.ToList();
            }
        }

        return clusters;
    }

    private static string SanitizeTopic(string topic) =>
        string.Join("_", topic.Split(Path.GetInvalidFileNameChars(),
            StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
