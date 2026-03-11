namespace RevitChatBot.Core.Context;

public class ContextManager
{
    private readonly List<IContextProvider> _providers = new();
    private object? _revitDocument;
    private RevitContextCache? _contextCache;
    private Func<Func<object, object?>, Task<object?>>? _revitApiInvoker;

    public RevitContextCache? ContextCache => _contextCache;

    public void SetRevitDocument(object? document)
    {
        _revitDocument = document;
    }

    public void SetContextCache(RevitContextCache cache)
    {
        _contextCache = cache;
    }

    /// <summary>
    /// Set the Revit API invoker so context providers run on the Revit main thread.
    /// Without this, providers that access Revit API will be skipped.
    /// </summary>
    public void SetRevitApiInvoker(Func<Func<object, object?>, Task<object?>>? invoker)
    {
        _revitApiInvoker = invoker;
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
        var sorted = _providers.OrderBy(p => p.Priority).ToList();

        var revitProviders = sorted.Where(p => p.NeedsRevitApi).ToList();
        var backgroundProviders = sorted.Where(p => !p.NeedsRevitApi).ToList();

        // Revit-dependent providers run on the Revit main thread via ExternalEvent
        if (_revitDocument != null && _revitApiInvoker != null && revitProviders.Count > 0)
        {
            try
            {
                var revitContext = await _revitApiInvoker(doc =>
                {
                    var data = new ContextData();
                    foreach (var provider in revitProviders)
                    {
                        try
                        {
                            var providerData = provider.GatherAsync(doc).GetAwaiter().GetResult();
                            data.Merge(providerData);
                        }
                        catch { }
                    }
                    return data;
                });

                if (revitContext is ContextData cd)
                    result.Merge(cd);
            }
            catch { }
        }

        // Non-Revit providers run on the current (background) thread - safe for async HTTP calls
        foreach (var provider in backgroundProviders)
        {
            try
            {
                var data = await provider.GatherAsync(_revitDocument);
                result.Merge(data);
            }
            catch { }
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
