using System.Text.Json;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Module 2: Allows runtime registration of new skills from successful codegen.
/// When user confirms saving a codegen result as a reusable skill, it gets a name,
/// description, and parameters — then is registered like any built-in skill.
/// Persists to JSON so skills survive restarts.
/// </summary>
public class DynamicSkillRegistry
{
    private readonly string _filePath;
    private readonly DynamicCodeExecutor _executor;
    private readonly SkillRegistry _hostRegistry;
    private List<DynamicSkillDefinition> _definitions = [];
    private bool _loaded;

    public DynamicSkillRegistry(string filePath, DynamicCodeExecutor executor, SkillRegistry hostRegistry)
    {
        _filePath = filePath;
        _executor = executor;
        _hostRegistry = hostRegistry;
    }

    public IReadOnlyList<DynamicSkillDefinition> Definitions => _definitions.AsReadOnly();

    public async Task LoadAndRegisterAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _definitions = JsonSerializer.Deserialize<List<DynamicSkillDefinition>>(json, JsonOpts) ?? [];
            }
        }
        catch { _definitions = []; }
        _loaded = true;

        foreach (var def in _definitions)
        {
            RegisterDefinition(def);
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_definitions, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Create a new dynamic skill from codegen code.
    /// Registers it immediately and persists for future sessions.
    /// </summary>
    public DynamicSkillDefinition CreateSkill(
        string name, string description, string code,
        List<DynamicSkillParam>? parameters = null)
    {
        name = SanitizeName(name);

        if (_hostRegistry.GetSkill(name) != null)
            throw new InvalidOperationException($"Skill '{name}' already exists.");

        var def = new DynamicSkillDefinition
        {
            Name = name,
            Description = description,
            Code = code,
            Parameters = parameters ?? [],
            CreatedAt = DateTime.UtcNow,
            UseCount = 0
        };

        _definitions.Add(def);
        RegisterDefinition(def);

        return def;
    }

    /// <summary>
    /// Remove a dynamic skill by name.
    /// </summary>
    public bool RemoveSkill(string name)
    {
        var idx = _definitions.FindIndex(d =>
            d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        _definitions.RemoveAt(idx);
        return true;
    }

    /// <summary>
    /// Get a summary of all dynamic skills for LLM context.
    /// </summary>
    public string GetDynamicSkillsSummary()
    {
        if (_definitions.Count == 0) return "";

        var lines = new List<string> { "[dynamic_skills]" };
        foreach (var d in _definitions.OrderByDescending(x => x.UseCount))
        {
            lines.Add($"  - {d.Name}: {d.Description} (used {d.UseCount}x)");
        }
        return string.Join("\n", lines);
    }

    public void RecordUsage(string skillName)
    {
        var def = _definitions.Find(d =>
            d.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));
        if (def != null)
        {
            def.UseCount++;
            def.LastUsed = DateTime.UtcNow;
        }
    }

    private void RegisterDefinition(DynamicSkillDefinition def)
    {
        try
        {
            var skill = new RuntimeDynamicSkill(def, _executor);
            var wrapper = new DynamicSkillWrapper(def, skill);
            _hostRegistry.RegisterDynamic(def.Name, wrapper, BuildDescriptor(def));
        }
        catch
        {
            // If registration fails, the skill is still in _definitions for next attempt
        }
    }

    private static SkillDescriptor BuildDescriptor(DynamicSkillDefinition def) => new()
    {
        Name = def.Name,
        Description = def.Description +
            " [Dynamic skill — created from codegen]",
        Parameters = def.Parameters.Select(p => new SkillParameterDescriptor
        {
            Name = p.Name,
            Type = p.Type,
            Description = p.Description,
            IsRequired = p.IsRequired,
            AllowedValues = p.AllowedValues
        }).ToList()
    };

    private static string SanitizeName(string name) =>
        string.Join("_", name.ToLowerInvariant().Split(
            [' ', '-', '.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries));

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

/// <summary>
/// A skill that wraps a DynamicSkillDefinition and executes its stored code.
/// </summary>
public class RuntimeDynamicSkill
{
    private readonly DynamicSkillDefinition _def;
    private readonly DynamicCodeExecutor _executor;

    public RuntimeDynamicSkill(DynamicSkillDefinition def, DynamicCodeExecutor executor)
    {
        _def = def;
        _executor = executor;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var code = _def.Code;

        foreach (var param in _def.Parameters)
        {
            if (parameters.TryGetValue(param.Name, out var val) && val != null)
            {
                code = code.Replace($"$${param.Name}$$", val.ToString());
                code = code.Replace($"/*PARAM:{param.Name}*/", val.ToString());
            }
        }

        var result = await _executor.ExecuteAsync(code, context.RevitApiInvoker, ct);

        if (result.Success)
            return SkillResult.Ok(result.Output ?? "Done.", new { dynamicSkill = _def.Name });

        return SkillResult.Fail($"Dynamic skill '{_def.Name}' failed: {result.Error}");
    }
}

/// <summary>
/// Wraps RuntimeDynamicSkill to implement ISkill interface.
/// </summary>
public class DynamicSkillWrapper : ISkill
{
    private readonly DynamicSkillDefinition _def;
    private readonly RuntimeDynamicSkill _inner;

    public DynamicSkillWrapper(DynamicSkillDefinition def, RuntimeDynamicSkill inner)
    {
        _def = def;
        _inner = inner;
    }

    public Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        return _inner.ExecuteAsync(context, parameters, cancellationToken);
    }
}

public class DynamicSkillDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Code { get; set; } = "";
    public List<DynamicSkillParam> Parameters { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsed { get; set; }
    public int UseCount { get; set; }
}

public class DynamicSkillParam
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
    public bool IsRequired { get; set; }
    public List<string>? AllowedValues { get; set; }
}
