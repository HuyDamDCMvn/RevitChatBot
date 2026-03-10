using System.Text;
using RevitChatBot.Core.Context;

namespace RevitChatBot.Knowledge.Search;

/// <summary>
/// Injects relevant knowledge base content into the LLM context via RAG.
/// Searches the vector store for chunks relevant to the user's latest message.
/// </summary>
public class KnowledgeContextProvider : IContextProvider
{
    private readonly KnowledgeManager _knowledgeManager;
    private string _lastQuery = string.Empty;

    public string Name => "knowledge_base";
    public int Priority => 50;

    public KnowledgeContextProvider(KnowledgeManager knowledgeManager)
    {
        _knowledgeManager = knowledgeManager;
    }

    public void SetQuery(string query) => _lastQuery = query;

    public async Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();
        if (string.IsNullOrWhiteSpace(_lastQuery)) return data;

        try
        {
            var results = await _knowledgeManager.SearchAsync(_lastQuery, topK: 3);
            if (results.Count == 0) return data;

            var sb = new StringBuilder();
            sb.AppendLine("Relevant reference materials:");
            foreach (var result in results)
            {
                var source = result.Entry.Metadata.GetValueOrDefault("source", "unknown");
                var category = result.Entry.Metadata.GetValueOrDefault("category", "");
                sb.AppendLine($"--- [{source}] {(category != "" ? $"({category}) " : "")}(relevance: {result.Score:F2}) ---");
                sb.AppendLine(result.Entry.Text);
                sb.AppendLine();
            }

            data.Add("knowledge_base", sb.ToString());
        }
        catch
        {
            // RAG is non-critical; don't fail the whole context gathering
        }

        return data;
    }
}
