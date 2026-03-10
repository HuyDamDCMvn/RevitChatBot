namespace RevitChatBot.Core.Context;

public interface IContextProvider
{
    string Name { get; }
    int Priority { get; }
    Task<ContextData> GatherAsync(object? revitDocument = null);
}
