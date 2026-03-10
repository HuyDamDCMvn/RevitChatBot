namespace RevitChatBot.Core.Context;

public class ContextManager
{
    private readonly List<IContextProvider> _providers = new();
    private object? _revitDocument;

    public void SetRevitDocument(object? document)
    {
        _revitDocument = document;
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

        return result;
    }
}
