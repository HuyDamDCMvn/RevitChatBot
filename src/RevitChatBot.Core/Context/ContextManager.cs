namespace RevitChatBot.Core.Context;

public class ContextManager
{
    private readonly List<IContextProvider> _providers = new();
    private object? _revitDocument;
    private RevitContextCache? _contextCache;

    public RevitContextCache? ContextCache => _contextCache;

    public void SetRevitDocument(object? document)
    {
        _revitDocument = document;
    }

    public void SetContextCache(RevitContextCache cache)
    {
        _contextCache = cache;
    }

    public void Register(IContextProvider provider)
    {
        _providers.Add(provider);
    }

    public void RegisterRange(IEnumerable<IContextProvider> providers)
    {
        _providers.AddRange(providers);
    }

    public async Task<ContextData> GatherContextAsync()
    {
        var result = new ContextData();
        var sorted = _providers.OrderBy(p => p.Priority);

        foreach (var provider in sorted)
        {
            try
            {
                var data = await provider.GatherAsync(_revitDocument);
                result.Merge(data);
            }
            catch (Exception ex)
            {
                result.Add($"error_{provider.Name}", $"Failed to gather context: {ex.Message}");
            }
        }

        if (_contextCache != null)
        {
            result.Add("realtime_context",
                $"Last view change: {FormatAge(_contextCache.LastViewChangeUtc)}" +
                $"\nLast selection change: {FormatAge(_contextCache.LastSelectionChangeUtc)}" +
                $"\nLast document change: {FormatAge(_contextCache.LastDocumentChangeUtc)}" +
                $"\nAutomation mode: {_contextCache.AutomationMode}");
        }

        return result;
    }

    private static string FormatAge(DateTime utc)
    {
        if (utc == default) return "never";
        var age = DateTime.UtcNow - utc;
        if (age.TotalSeconds < 5) return "just now";
        if (age.TotalMinutes < 1) return $"{(int)age.TotalSeconds}s ago";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
        return $"{(int)age.TotalHours}h ago";
    }
}
