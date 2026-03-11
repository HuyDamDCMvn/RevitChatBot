using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Orchestrates persistence of all self-learning modules.
/// Batches saves after N changes to avoid excessive I/O.
/// </summary>
public class SelfLearningPersistenceManager
{
    private readonly CodePatternLearning? _patternLearning;
    private readonly DynamicSkillRegistry? _dynamicSkills;
    private readonly AdaptiveFewShotLearning? _fewShotLearning;
    private readonly DynamicGlossary? _glossary;
    private readonly CodeGenLibrary? _codeGenLibrary;
    private readonly MemoryManager? _memory;
    private readonly PlanReplayStore? _planStore;
    private readonly InteractionRecorder? _interactionRecorder;
    private readonly ImprovementStore? _improvementStore;

    private int _changesSinceLastPersist;
    private const int PersistThreshold = 3;
    private readonly SemaphoreSlim _persistLock = new(1, 1);

    public SelfLearningPersistenceManager(
        CodePatternLearning? patternLearning = null,
        DynamicSkillRegistry? dynamicSkills = null,
        AdaptiveFewShotLearning? fewShotLearning = null,
        DynamicGlossary? glossary = null,
        CodeGenLibrary? codeGenLibrary = null,
        MemoryManager? memory = null,
        PlanReplayStore? planStore = null,
        InteractionRecorder? interactionRecorder = null,
        ImprovementStore? improvementStore = null)
    {
        _patternLearning = patternLearning;
        _dynamicSkills = dynamicSkills;
        _fewShotLearning = fewShotLearning;
        _glossary = glossary;
        _codeGenLibrary = codeGenLibrary;
        _memory = memory;
        _planStore = planStore;
        _interactionRecorder = interactionRecorder;
        _improvementStore = improvementStore;
    }

    /// <summary>
    /// Notify that a learning event occurred. Auto-persists after threshold.
    /// </summary>
    public void NotifyChange()
    {
        var count = Interlocked.Increment(ref _changesSinceLastPersist);
        if (count >= PersistThreshold)
            _ = PersistAllAsync();
    }

    /// <summary>
    /// Force persist all learning data to disk.
    /// </summary>
    public async Task PersistAllAsync(CancellationToken ct = default)
    {
        if (!await _persistLock.WaitAsync(0, ct)) return;
        try
        {
            Interlocked.Exchange(ref _changesSinceLastPersist, 0);

            var tasks = new List<Task>();

            if (_patternLearning != null) tasks.Add(_patternLearning.SaveAsync(ct));
            if (_dynamicSkills != null) tasks.Add(_dynamicSkills.SaveAsync(ct));
            if (_fewShotLearning != null) tasks.Add(_fewShotLearning.SaveAsync(ct));
            if (_glossary != null) tasks.Add(_glossary.SaveAsync(ct));
            if (_codeGenLibrary != null) tasks.Add(_codeGenLibrary.SaveAsync(ct));
            if (_planStore != null) tasks.Add(_planStore.SaveAsync(ct));
            if (_interactionRecorder != null) tasks.Add(_interactionRecorder.SaveAsync(ct));
            if (_improvementStore != null) tasks.Add(_improvementStore.SaveAsync(ct));

            await Task.WhenAll(tasks);
        }
        catch { /* non-critical background operation */ }
        finally
        {
            _persistLock.Release();
        }
    }

    /// <summary>
    /// Load all persisted learning data on startup.
    /// </summary>
    public async Task LoadAllAsync(CancellationToken ct = default)
    {
        var tasks = new List<Task>();

        if (_patternLearning != null) tasks.Add(_patternLearning.LoadAsync(ct));
        if (_dynamicSkills != null) tasks.Add(_dynamicSkills.LoadAndRegisterAsync(ct));
        if (_fewShotLearning != null) tasks.Add(_fewShotLearning.LoadAsync(ct));
        if (_glossary != null) tasks.Add(_glossary.LoadAsync(ct));
        if (_codeGenLibrary != null) tasks.Add(_codeGenLibrary.LoadAsync(ct));
        if (_planStore != null) tasks.Add(_planStore.LoadAsync(ct));
        if (_interactionRecorder != null) tasks.Add(_interactionRecorder.LoadAsync(ct));
        if (_improvementStore != null) tasks.Add(_improvementStore.LoadAsync(ct));

        await Task.WhenAll(tasks);
    }
}
