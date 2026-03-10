using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.Context;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;
using RevitChatBot.Core.Models;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Implements a ReAct (Reasoning + Acting) agent pattern for multi-step task execution.
/// The agent iterates through Thought -> Action -> Observation cycles until it reaches an answer.
/// Supports confirmation flow for destructive actions (create/modify/delete).
/// Integrates with MemoryManager for cross-session learning and context persistence.
/// </summary>
public class AgentOrchestrator
{
    private readonly IOllamaService _ollama;
    private readonly SkillRegistry _skillRegistry;
    private readonly SkillExecutor _skillExecutor;
    private readonly ContextManager _contextManager;
    private readonly PromptBuilder _promptBuilder;
    private readonly MemoryManager? _memory;
    private readonly List<ChatMessage> _history = [];

    private const int MaxReActSteps = 8;

    private static readonly HashSet<string> DestructiveSkills =
    [
        "create_element",
        "modify_parameter",
        "delete_element",
        "execute_revit_code",
        "batch_modify",
        "avoid_clash",
        "split_duct_pipe",
        "map_room_to_mep"
    ];

    public IReadOnlyList<ChatMessage> History => _history.AsReadOnly();
    public List<ChatMessage> MutableHistory => _history;

    public event Action<AgentStep>? OnStepExecuted;
    public event Action<string>? OnThinking;
    public event Func<string, Task<bool>>? OnConfirmationRequired;

    public AgentOrchestrator(
        IOllamaService ollama,
        SkillRegistry skillRegistry,
        SkillExecutor skillExecutor,
        ContextManager contextManager,
        PromptBuilder? promptBuilder = null,
        MemoryManager? memory = null)
    {
        _ollama = ollama;
        _skillRegistry = skillRegistry;
        _skillExecutor = skillExecutor;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder ?? new PromptBuilder();
        _memory = memory;
    }

    public async Task<AgentPlan> ExecuteAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var plan = new AgentPlan { Goal = userMessage };
        var userMsg = ChatMessage.FromUser(userMessage);
        _history.Add(userMsg);
        _memory?.OnUserMessage(userMessage);

        var context = await _contextManager.GatherContextAsync();
        var toolDefs = _promptBuilder.BuildToolDefinitions(_skillRegistry.GetAllDescriptors());

        var systemPrompt = BuildReActSystemPrompt();
        var messages = new List<ChatMessage> { ChatMessage.FromSystem(systemPrompt) };

        if (context.Entries.Count > 0)
        {
            var contextContent = "--- CURRENT MODEL CONTEXT ---\n";
            foreach (var entry in context.Entries)
                contextContent += $"\n[{entry.Key}]\n{entry.Value}\n";
            messages.Add(ChatMessage.FromSystem(contextContent));
        }

        messages.AddRange(_history);

        for (int step = 0; step < MaxReActSteps; step++)
        {
            var response = await _ollama.ChatAsync(messages, toolDefs, cancellationToken);

            if (response.ToolCalls is not { Count: > 0 })
            {
                plan.Steps.Add(new AgentStep
                {
                    Type = AgentStepType.Answer,
                    Content = response.Content
                });
                plan.FinalAnswer = response.Content;
                plan.IsCompleted = true;

                _history.Add(response);
                OnStepExecuted?.Invoke(plan.Steps[^1]);

                if (_memory != null)
                    _ = _memory.OnAssistantResponseAsync(userMsg, response, cancellationToken);

                break;
            }

            var thoughtStep = new AgentStep
            {
                Type = AgentStepType.Thought,
                Content = response.Content
            };
            plan.Steps.Add(thoughtStep);
            OnStepExecuted?.Invoke(thoughtStep);

            if (!string.IsNullOrWhiteSpace(response.Content))
                OnThinking?.Invoke(response.Content);

            messages.Add(response);
            _history.Add(response);

            foreach (var toolCall in response.ToolCalls)
            {
                if (DestructiveSkills.Contains(toolCall.FunctionName))
                {
                    var confirmed = await RequestConfirmation(toolCall, cancellationToken);
                    if (!confirmed)
                    {
                        var cancelMsg = ChatMessage.FromTool(toolCall.FunctionName,
                            "{\"success\":false,\"message\":\"User cancelled the operation.\"}");
                        messages.Add(cancelMsg);
                        _history.Add(cancelMsg);

                        plan.Steps.Add(new AgentStep
                        {
                            Type = AgentStepType.Observation,
                            Content = "User cancelled the destructive action.",
                            SkillName = toolCall.FunctionName
                        });
                        continue;
                    }
                }

                var actionStep = new AgentStep
                {
                    Type = AgentStepType.Action,
                    SkillName = toolCall.FunctionName,
                    Parameters = toolCall.Arguments,
                    Content = $"Executing: {toolCall.FunctionName}"
                };
                plan.Steps.Add(actionStep);
                OnStepExecuted?.Invoke(actionStep);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await _skillExecutor.ExecuteAsync(
                    toolCall.FunctionName, toolCall.Arguments, cancellationToken);
                sw.Stop();

                _memory?.OnSkillExecuted(toolCall.FunctionName, result.Success, sw.Elapsed.TotalMilliseconds);

                if (toolCall.FunctionName == "execute_revit_code")
                    _memory?.OnCodeGenAttempt(result.Success, result.Success, result.Success ? null : "compile_error");

                var toolMessage = ChatMessage.FromTool(toolCall.FunctionName, result.ToJson());
                messages.Add(toolMessage);
                _history.Add(toolMessage);

                var observationStep = new AgentStep
                {
                    Type = AgentStepType.Observation,
                    SkillName = toolCall.FunctionName,
                    Content = result.Message
                };
                plan.Steps.Add(observationStep);
                OnStepExecuted?.Invoke(observationStep);
            }
        }

        if (!plan.IsCompleted)
        {
            var finalResponse = await _ollama.ChatAsync(messages, cancellationToken: cancellationToken);
            plan.FinalAnswer = finalResponse.Content;
            plan.IsCompleted = true;
            _history.Add(finalResponse);

            if (_memory != null)
                _ = _memory.OnAssistantResponseAsync(userMsg, finalResponse, cancellationToken);
        }

        return plan;
    }

    private async Task<bool> RequestConfirmation(ToolCall toolCall, CancellationToken ct)
    {
        if (OnConfirmationRequired is null)
            return true;

        var description = $"Action: {toolCall.FunctionName}\n" +
                         string.Join("\n", toolCall.Arguments.Select(a => $"  {a.Key}: {a.Value}"));

        return await OnConfirmationRequired.Invoke(description);
    }

    private static string BuildReActSystemPrompt()
    {
        var basePrompt = """
            You are an expert MEP (Mechanical, Electrical, Plumbing) engineer working with Autodesk Revit 2025.
            You use the ReAct (Reasoning + Acting) approach for multi-step tasks.

            ## YOUR ROLES
            - **Digital Coordinator**: QA/QC, clash detection, model audit, compliance checking, report generation
            - **MEP Modeler**: sizing, routing, system setup, parameter management, schedule generation

            ## LANGUAGE RULES
            - Detect the language the user writes in and ALWAYS reply in that same language.
            - If the user writes in Vietnamese, reply entirely in Vietnamese.
            - If the user writes in English, reply entirely in English.
            - Use technical MEP terminology: ống gió, ống nước, thiết bị, hệ thống, va chạm (VI) / ducts, pipes, equipment, systems, clashes (EN).

            ## REASONING APPROACH (ReAct)
            1. THINK: Analyze what the user needs. Check [model_inventory] context for families, types, levels.
            2. ACT: Call the appropriate tool(s).
            3. OBSERVE: Review results.
            4. Repeat if more information is needed.
            5. ANSWER: Provide a clear response with actionable recommendations.

            ## ENGINEERING JUDGMENT — RED FLAGS
            - Duct velocity > 8 m/s in branch → noise issue
            - Pipe without insulation in CHW system → condensation risk
            - Fire damper missing at fire-rated wall → CRITICAL safety
            - Element without system assignment → incomplete model
            - Pipe slope < minimum for its size → poor drainage
            - Clearance < 2.4m in corridor → obstructed pathway
            - Disconnected elements → discontinuous system
            - Oversized duct width > 1500mm → consider splitting

            ## QA/QC WORKFLOW (when user asks for full check)
            1. Query model overview (element counts, systems)
            2. Check disconnected elements
            3. Check insulation coverage (CHW, SA must have insulation)
            4. Check fire dampers (both ends connected)
            5. Check clearance height (≥2.4m corridor)
            6. Check duct velocity (main ≤12 m/s, branch ≤6 m/s)
            7. Check pipe slope (SAN DN100 ≥1%)
            8. Check clashes (ducts vs pipes)
            9. Suggest avoid_clash rerouting for identified clashes
            10. Generate summary report (Critical / Major / Minor)

            ## CLASH AVOIDANCE REROUTING
            - Use 'avoid_clash' skill with mode='analyze' first to show the user what will change.
            - Only use mode='execute' after user confirmation.
            - The reroute uses a dogleg pattern (5 segments + 4 elbows) to bypass obstacles.
            - Direction classification: parallel elements → offset Up/Down; perpendicular → Left/Right.
            - Typical tolerances: 50mm standard, 25mm tight, 100mm generous.
            - Typical offsets: 150mm pipe, 200mm duct, 100mm conduit/cable tray.
            - Always specify level_name for large models to limit scope.

            ## DIRECTIONAL CLEARANCE (Ray Casting)
            - Use 'check_directional_clearance' for precise distance to walls/floors/ceilings/columns.
            - Requires a 3D view. Supports linked models (check_links=true).
            - 6 directions: top/bottom/left/right/front/back. Use 'all' for comprehensive check.
            - Typical thresholds: corridor 2400mm top, 50mm side, sprinkler 25-305mm from ceiling.

            ## ROOM/SPACE MAPPING
            - Use 'map_room_to_mep' to identify which room/space each MEP element belongs to.
            - mode='report' for analysis, mode='execute' to write values to MEP parameters.
            - Uses IsPointInRoom/IsPointInSpace API (most accurate method).
            - above_offset_mm for ceiling-mounted elements (e.g., 1000mm for above-ceiling duct).

            ## SPLIT DUCT/PIPE/CONDUIT/CABLE TRAY
            - Use 'split_duct_pipe' to divide ducts, pipes, conduits, or cable trays into equal segments.
            - Duct/Pipe: uses BreakCurve + union fittings.
            - Conduit/CableTray: uses CopyElement workaround (BreakCurve not supported).
            - Numbers each segment sequentially in the chosen parameter.
            - Confirm before executing — this modifies the model significantly.

            ## MEP SYSTEM GRAPH TRAVERSAL
            - Use 'traverse_mep_system' to explore full system topology from any starting element.
            - Returns: element count, max depth, open ends, total length, category breakdown.
            - include_path=yes for detailed traversal path with connector info.
            - domain_filter: hvac/piping/electrical/all to limit traversal scope.
            - Handles MEPCurve, FamilyInstance, and FabricationPart connectors.
            - Skips logical connectors (ConnectorType.Logical) for accurate physical graph.
            - Typical use: "show me all elements connected to this duct", "trace the piping network".

            ## ROUTING PREFERENCES
            - Use 'query_routing_preferences' to inspect preferred fittings for pipe/duct types.
            - Shows: junction type (Tee vs Tap), elbow families, transition families, segment rules.
            - Helpful before creating fittings — ensures the right family is used.
            - Helpful for QA — verify that routing preferences are configured correctly.

            ## CONNECTOR ANALYSIS (for dynamic code / advanced queries)
            - Connector access: MEPCurve.ConnectorManager, FamilyInstance.MEPModel?.ConnectorManager, FabricationPart.ConnectorManager
            - Properties: Origin, CoordinateSystem.BasisZ (direction), Domain, Shape, Flow, PressureDrop
            - For Tee fitting: main connectors (colinear, dot≈-1) first, branch last.
            - For Cross fitting: main pair first, then branch pair.
            - Area by shape: Round=π×r², Rect=W×H, Oval=π×W×H/4

            ## DESIGN CRITERIA QUICK REFERENCE
            Duct velocity: main 8-12 m/s, branch 4-6 m/s, exhaust 8-10 m/s
            Pipe velocity: CHW main 1.5-3.0 m/s, branch 1.0-1.5 m/s, FP 3.0-5.0 m/s
            Pipe slope: SAN DN50-75 ≥2%, DN100 ≥1%, DN150+ ≥0.5%, CON 1-2%
            Clearance: corridor 2.4m, office 2.6m, lobby 2.8-3.0m, basement 2.1m
            Insulation required: CHW, SA, HW, CON. NOT required: EA, RA (in ceiling), SAN, STM
            Duct sizing: A=Q/V, standard sizes: 100,125,150,200...2000mm, aspect ratio ≤4:1
            Pipe sizing: d=sqrt(4Q/πV), standard DN: 15,20,25,32,40,50,65,80,100...500mm

            ## SIZING FORMULAS
            Duct: A(m²) = Q(m³/s) / V(m/s), rect: W=sqrt(A×AR), H=A/W
            Pipe: d(m) = sqrt(4×Q/(π×V)), round to standard DN
            Heat load: Q(kW) = m(kg/s) × Cp × ΔT. Water Cp=4.186, Air Cp=1.005
            Equivalent diameter: De = 1.3×(W×H)^0.625/(W+H)^0.25
            Standard duct sizes (mm): 100,125,150,200,250,300,350,400,450,500,550,600,700,800,900,1000,1200,1400,1600,1800,2000
            Standard pipe DN (mm): 15,20,25,32,40,50,65,80,100,125,150,200,250,300,350,400,450,500

            ## UNIT CONVERSIONS
            1 m³/s = 1000 L/s = 2119 CFM. 1 RT = 3.517 kW = 12000 BTU/h
            Revit internal = FEET. 1 ft = 0.3048 m. 1 sq ft = 0.092903 m²

            ## STANDARDS REFERENCE
            MEP: TCVN 5687 (HVAC), QCVN 06 (fire protection), ASHRAE 62.1/90.1, SMACNA, NFPA 13/72/90A
            BIM: ISO 19650 (info management), ISO 29481 (IDM), ISO 12006 (classification), ISO 7817 (LOI need)
            BIM Data: ISO 23386/23387 (data templates), ISO 12911 (BIM implementation spec), ISO 16739 (IFC)
            Cost: DIN 276 (building costs — KG 400 = MEP systems: 410 plumbing, 420 heating, 430 HVAC, 440 electrical)

            ## BIM INFORMATION MANAGEMENT (ISO 19650)
            - CDE states: WIP → Shared → Published → Archived
            - Information requirements: OIR → AIR → PIR → EIR
            - Key docs: BIM Execution Plan (BEP), MIDP, TIDP, Responsibility Matrix
            - Exchange review (7C): CDE compliance, Conformance, Continuity, Communication, Consistency, Completeness
            - UK file naming: Project-Originator-Volume-Level-Type-Role-Number
            - Open data formats: IFC (ISO 16739), BCF, gbXML, COBie
            - Level of Information Need (ISO 7817): specifies geometry/property detail per project phase

            ## DYNAMIC CODE GENERATION
            When no existing tool/skill can fulfill the request, use 'execute_revit_code':
            - Class 'DynamicAction' with static 'Execute(Document doc)' returning string
            - Available: System, System.Linq, Autodesk.Revit.DB (and sub-namespaces)
            - Use FilteredElementCollector to query. Wrap modifications in a Transaction.
            - ALWAYS return descriptive result string with counts, names, details.
            - If compilation fails, analyze error feedback and fix (up to 3 retries).
            - CHECK [model_inventory] for exact Family/Type/Level names.
            - REFERENCE the API Cheat Sheet below for method signatures.

            ## RESPONSE FORMAT
            - Use tables for listing issues (Element ID | Issue | Severity)
            - Group results by system or level
            - Provide actionable recommendations, not just problem descriptions
            - Include both metric and imperial units when presenting measurements
            - For destructive actions, explain what you will do first
            """;

        return basePrompt + "\n\n" +
               RevitApiCheatSheet.GetCheatSheet() + "\n\n" +
               RevitApiCheatSheet.GetCommonErrorFixes() + "\n\n" +
               CodeExamplesLibrary.GetExamples();
    }

    public void ClearHistory() => _history.Clear();
}
