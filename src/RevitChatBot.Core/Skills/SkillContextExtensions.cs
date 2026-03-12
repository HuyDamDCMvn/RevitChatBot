using RevitChatBot.Core.Context;

namespace RevitChatBot.Core.Skills;

public static class SkillContextExtensions
{
    /// <summary>
    /// Returns ElementIds of the current Revit selection from the context cache,
    /// or null if no selection info is available.
    /// </summary>
    public static List<long>? GetCurrentSelectionIds(this SkillContext context)
    {
        if (context.Extra.GetValueOrDefault("contextCache") is RevitContextCache cache
            && cache.CurrentSelection is { Count: > 0 } snapshot)
        {
            return snapshot.ElementIds;
        }
        return null;
    }

    /// <summary>
    /// Returns the context cache instance, or null if not available.
    /// </summary>
    public static RevitContextCache? GetContextCache(this SkillContext context)
    {
        return context.Extra.GetValueOrDefault("contextCache") as RevitContextCache;
    }
}
