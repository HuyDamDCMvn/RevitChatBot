using RevitChatBot.Core.Skills;

namespace RevitChatBot.Knowledge.Search;

/// <summary>
/// Allows the user or agent to proactively index new documents into the
/// knowledge base (RAG). Supports file paths and directories.
/// </summary>
[Skill("index_knowledge",
    "Index documents into the knowledge base for RAG search. " +
    "Supports PDF, Markdown, JSON, and text files. " +
    "Use to add MEP standards, project specs, or Revit API docs.")]
[SkillParameter("path", "string",
    "File or directory path to index. All supported files will be processed.",
    isRequired: true)]
[SkillParameter("action", "string",
    "'index' to add documents, 'status' to check index count, 'clear' to reset.",
    isRequired: false,
    allowedValues: new[] { "index", "status", "clear" })]
public class IndexKnowledgeSkill : ISkill
{
    private readonly KnowledgeManager _manager;

    public IndexKnowledgeSkill(KnowledgeManager manager)
    {
        _manager = manager;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var path = parameters.GetValueOrDefault("path")?.ToString() ?? "";
        var action = parameters.GetValueOrDefault("action")?.ToString()?.ToLower() ?? "index";

        switch (action)
        {
            case "status":
                var count = await _manager.GetIndexedCountAsync(cancellationToken);
                return SkillResult.Ok(
                    $"Knowledge base contains {count} indexed chunks.",
                    new { indexedChunks = count });

            case "clear":
                await _manager.LoadIndexAsync(cancellationToken);
                return SkillResult.Ok("Knowledge base index reloaded from disk.");

            case "index":
            default:
                return await IndexPath(path, cancellationToken);
        }
    }

    private async Task<SkillResult> IndexPath(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return SkillResult.Fail("Path is required for indexing.");

        int beforeCount = await _manager.GetIndexedCountAsync(ct);

        try
        {
            if (Directory.Exists(path))
            {
                await _manager.IndexDirectoryAsync(path, ct);
            }
            else if (File.Exists(path))
            {
                await _manager.IndexFileAsync(path, ct);
            }
            else
            {
                return SkillResult.Fail($"Path not found: {path}");
            }

            int afterCount = await _manager.GetIndexedCountAsync(ct);
            int added = afterCount - beforeCount;

            return SkillResult.Ok(
                $"Indexed {added} new chunk(s) from '{Path.GetFileName(path)}'. " +
                $"Total: {afterCount} chunks in knowledge base.",
                new { path, chunksAdded = added, totalChunks = afterCount });
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Indexing failed: {ex.Message}");
        }
    }
}
