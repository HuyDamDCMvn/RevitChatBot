namespace RevitChatBot.Core.Context;

public interface IContextProvider
{
    string Name { get; }
    int Priority { get; }

    /// <summary>
    /// If true, GatherAsync will be routed through ExternalEvent on the Revit main thread.
    /// If false, GatherAsync runs on the calling (background) thread.
    /// </summary>
    bool NeedsRevitApi => true;

    Task<ContextData> GatherAsync(object? revitDocument = null);
}
