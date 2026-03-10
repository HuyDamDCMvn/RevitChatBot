# Revit MEP ChatBot

An AI-powered chatbot embedded in Autodesk Revit 2025 for MEP (Mechanical, Electrical, Plumbing) engineering tasks. Uses **Ollama** (`qwen2.5:7b`) for local LLM inference, **RAG** for standards lookup, a **ReAct agent** for multi-step reasoning, **Roslyn dynamic code generation** for unlimited Revit API operations, **cross-session memory** with conversation persistence and self-learning, and a **React** UI rendered via WebView2.

## Architecture Overview

```mermaid
graph TB
    subgraph Revit2025 [Revit 2025 Process]
        subgraph AddinLayer [RevitChatBot.Addin]
            RibbonBtn[Ribbon Button]
            WPF[WPF Window]
            WV2[WebView2 Control]
            Bridge[WebViewBridge]
            EEH[ExternalEventHandler]
        end
    end

    subgraph CoreLayer [RevitChatBot.Core - Reusable]
        AO[AgentOrchestrator<br/>ReAct Pattern]
        CSV2[ChatSessionV2]
        PB[PromptBuilder]
        OLS[OllamaService]
        SR[SkillRegistry]
        SE[SkillExecutor]
        CM[ContextManager]
        subgraph CodeGenSub [CodeGen - Roslyn Runtime]
            RCC[RoslynCodeCompiler]
            DCE[DynamicCodeExecutor]
            DCS[DynamicCodeSkill]
            CSV[CodeSecurityValidator]
            ACS["RevitApiCheatSheet<br/>+ CodeExamplesLibrary"]
        end
        subgraph MemorySub [Memory - Cross-Session Learning]
            MM[MemoryManager]
            CSTORE[ConversationStore]
            CSUM[ConversationSummarizer]
            LFS[LearnedFactsStore]
            UPS[UserPreferencesStore]
            SANA[SessionAnalytics]
            MCTX[MemoryContextProvider]
        end
    end

    subgraph KnowledgeLayer [RevitChatBot.Knowledge - RAG]
        ES[OllamaEmbeddingService]
        VS[InMemoryVectorStore]
        KM[KnowledgeManager]
        DL["DocumentLoaders<br/>(Text, JSON)"]
        KCP[KnowledgeContextProvider]
        KSS[KnowledgeSearchSkill]
    end

    subgraph MEPLayer [RevitChatBot.MEP - Skills & Context]
        direction LR
        subgraph SkillSubs [Skills by Domain]
            SQ["Query: Elements, Overview,<br/>SystemAnalysis, Connectivity,<br/>Space, MapRoomToMep"]
            SCk["Check: Velocity, Slope, Connection,<br/>Insulation, Clearance, DirectionalClearance,<br/>FireDamper, Audit, Compliance"]
            SH[HVAC: Load, DuctSizing]
            SPl[Plumbing: PipeSizing, Drainage]
            SEl[Electrical: LoadAnalysis]
            SC["Coordination: Clash, AdvClash,<br/>AvoidClash (Reroute Engine)"]
            SM["Modify: Create, ModifyParam,<br/>BatchModify, SplitDuctPipe"]
            SR2[Report: ExportReport]
            SCa[Calculation: MEPCalc]
        end
        subgraph CtxSubs [Context Providers]
            CP1[ProjectInfo]
            CP2[ActiveView]
            CP3[SelectedElements]
            CP4[MEPSystem]
            CP5[RoomSpace]
            CP6[SystemDetail]
            CP7[LevelSummary]
            CP8[ModelInventory]
        end
    end

    subgraph ServicesLayer [RevitChatBot.RevitServices]
        RES[RevitElementService]
        RDS[RevitDocumentService]
        RMS[RevitMEPService]
    end

    subgraph ReactUI [React UI - Vite + TypeScript + Tailwind]
        CW[ChatWindow]
        MB[MessageBubble]
        IB[InputBar]
        SP[SkillPanel]
        BridgeTS[bridge.ts]
    end

    subgraph OllamaServer [Ollama - localhost:11434]
        LLM["qwen2.5:7b<br/>/api/chat"]
        EMB["nomic-embed-text<br/>/api/embed"]
    end

    subgraph KnowledgeBase [Knowledge Base - docs/knowledge/]
        KB0[bim-standards/]
        KB1[mep-standards/]
        KB2[revit-api/]
        KB3[project-specs/]
    end

    RibbonBtn -->|click| WPF
    WPF --> WV2
    WV2 <-->|postMessage JSON| BridgeTS
    BridgeTS --> CW
    CW --> MB & IB & SP

    Bridge <-->|WebMessageReceived| WV2
    Bridge --> CSV2
    CSV2 --> AO
    AO --> PB
    AO --> OLS
    OLS <-->|HTTP REST| LLM
    AO --> SE
    SE --> SR
    SR --> SQ & SCk & SH & SPl & SEl & SC & SM & SR2 & SCa & KSS & DCS
    AO --> CM
    CM --> CP1 & CP2 & CP3 & CP4 & CP5 & CP6 & CP7 & CP8 & KCP

    KCP --> KM
    KSS --> KM
    KM --> ES & VS & DL
    ES <-->|HTTP REST| EMB
    DL -->|load| KB0 & KB1 & KB2 & KB3

    DCS --> CSV
    CSV -->|validate| DCE
    DCE --> RCC
    RCC -->|compile C#| DCE
    SQ & SCk & SH & SPl & SEl & SC & SM & SR2 & SCa & DCE -->|RevitApiInvoker| EEH
    EEH -->|"Main Thread"| RES & RDS & RMS
    ACS -->|injected into system prompt| AO
    AO --> MM
    MM --> CSTORE & CSUM & LFS & UPS & SANA
    MCTX -->|"learned_facts + preferences + summary"| CM
    MM --> MCTX
    CP1 & CP2 & CP3 & CP4 & CP5 & CP6 & CP7 & CP8 --> RES & RDS & RMS
```

## ReAct Agent Flow

```mermaid
sequenceDiagram
    participant U as User
    participant R as React UI
    participant B as WebViewBridge
    participant A as AgentOrchestrator
    participant CM as ContextManager
    participant KCP as KnowledgeContextProvider
    participant OL as OllamaService
    participant SE as SkillExecutor
    participant EH as ExternalEventHandler

    U->>R: Type message
    R->>B: postMessage(user_message)
    B->>B: KnowledgeContextProvider.SetQuery(text)
    B->>A: ExecuteAsync(text)

    A->>CM: GatherContextAsync()
    Note over CM: 8 MEP providers + RAG provider + ModelInventory
    CM->>KCP: GatherAsync()
    KCP->>KCP: SearchAsync(query) via KnowledgeManager
    CM-->>A: ContextData (project + MEP + knowledge)

    loop ReAct Loop (max 8 steps)
        A->>OL: ChatAsync(messages, tools)
        OL-->>A: Response

        alt Has tool_calls
            Note over A: THOUGHT: LLM reasoning
            A->>A: OnStepExecuted(Thought)

            loop For each tool_call
                alt Destructive action
                    A->>R: ConfirmationRequired
                    R-->>A: User confirms/cancels
                end

                Note over A: ACTION: Execute skill
                A->>SE: ExecuteAsync(skill, params)
                SE->>EH: RevitApiInvoker
                EH-->>SE: result
                SE-->>A: SkillResult

                Note over A: OBSERVATION: Review result
                A->>A: OnStepExecuted(Observation)
            end
        else No tool_calls
            Note over A: ANSWER: Final response
            break
        end
    end

    A-->>B: AgentPlan.FinalAnswer
    B-->>R: postMessage(assistant_message)
    R-->>U: Display response
```

## RAG Knowledge Pipeline

```mermaid
flowchart LR
    subgraph indexing [Indexing Phase - On Startup]
        D1[docs/knowledge/*.md]
        D2[docs/knowledge/*.txt]
        D3[docs/knowledge/*.json]
        D4[docs/knowledge/*.pdf]
        DL[DocumentLoaders<br/>TextLoader, JsonLoader, PdfLoader]
        CH[Chunks<br/>512-800 tokens with overlap]
        EMB[OllamaEmbeddingService<br/>nomic-embed-text]
        VEC["float[] embeddings"]
        VS[InMemoryVectorStore<br/>+ JSON persistence]
    end

    subgraph query [Query Phase - Per Message]
        UQ[User Query]
        QE[Query Embedding]
        COS[Cosine Similarity Search]
        TOP[Top-K Results]
        CTX["Inject into LLM Context<br/>[knowledge_base] ..."]
    end

    D1 & D2 & D3 & D4 --> DL
    DL --> CH
    CH --> EMB
    EMB --> VEC
    VEC --> VS

    UQ --> QE
    QE --> COS
    VS --> COS
    COS --> TOP
    TOP --> CTX
```

## Dynamic Code Generation Flow (Revit Code Interpreter)

```mermaid
flowchart TD
    A["LLM: No existing skill matches"] --> B["LLM generates C# code<br/>via execute_revit_code tool"]
    B --> C{CodeSecurityValidator}
    C -->|"Blocked API detected<br/>(File.Delete, Process, Network...)"| D[Reject + return error to LLM]
    C -->|"Namespace not whitelisted"| D
    C -->|Pass| E[RoslynCodeCompiler]
    E -->|"Parse + Compile<br/>with Revit API refs"| F{Compilation OK?}
    F -->|Errors| G["Return compile errors to LLM"]
    G -->|"Retry ≤ 3"| B
    G -->|"Max retries"| H[Return failure to user]
    F -->|Success| I[DynamicCodeExecutor]
    I --> J{Destructive action?}
    J -->|Yes| K[Request user confirmation]
    K -->|Cancelled| L[Return cancelled]
    K -->|Confirmed| M
    J -->|No| M["Execute via RevitApiInvoker<br/>(Revit main thread)"]
    M --> N[DynamicAction.Execute doc]
    N --> O{Runtime OK?}
    O -->|Exception| P[Return runtime error to LLM]
    O -->|Success| Q["Return result string<br/>(counts, details, etc.)"]

    style C fill:#ff9,stroke:#333
    style E fill:#9cf,stroke:#333
    style M fill:#9f9,stroke:#333
```

## Skill Execution Flow

```mermaid
flowchart TD
    A[LLM returns tool_calls] --> B{Parse tool_call}
    B --> C[SkillExecutor.ExecuteAsync]
    C --> D{Skill found in Registry?}
    D -->|No| E[Return SkillResult.Fail]
    D -->|Yes| F{Is destructive?}
    F -->|Yes| G[Request user confirmation]
    G -->|Cancelled| E
    G -->|Confirmed| H[ISkill.ExecuteAsync]
    F -->|No| H
    H --> I{Needs Revit API?}
    I -->|No| J[Process locally]
    I -->|Yes| K[context.RevitApiInvoker]
    K --> L[ExternalEventHandler.ExecuteAsync]
    L --> M[ExternalEvent.Raise]
    M --> N["Execute(UIApplication) on main thread"]
    N --> O[Transaction if needed]
    O --> P[Revit API calls]
    P --> Q[Return result via TaskCompletionSource]
    Q --> R[SkillResult.Ok / SkillResult.Fail]
    J --> R
    E --> S[Send result back to LLM]
    R --> S
    S --> T{LLM needs more tools?}
    T -->|Yes, step < 8| B
    T -->|No| U[Return final text to user]
```

## Context Injection Flow

```mermaid
flowchart TD
    subgraph providers [Context Providers - Priority Order]
        P0["MemoryContextProvider (P:5)<br/>learned_facts + preferences + summary"]
        P1["ProjectInfoProvider (P:10)"]
        P1b["ModelInventoryProvider (P:15)"]
        P2["ActiveViewProvider (P:20)"]
        P3["SelectedElementsProvider (P:30)"]
        P4["RoomSpaceProvider (P:35)"]
        P5["MEPSystemProvider (P:40)"]
        P6["SystemDetailProvider (P:45)"]
        P7["KnowledgeContextProvider (P:50)"]
        P8["LevelSummaryProvider (P:30)"]
    end

    subgraph gather [ContextManager.GatherContextAsync]
        G1[Sort by priority]
        G2[Call each provider]
        G3[Merge into ContextData]
    end

    subgraph inject [PromptBuilder.Build]
        I0["System prompt + ReAct + API CheatSheet + Code Examples"]
        I1["--- CURRENT MODEL CONTEXT ---"]
        Imem["[learned_facts] + [user_preferences] + [conversation_memory]"]
        I2["[project_info] ..."]
        I2b["[model_inventory] families, types, levels, params"]
        I3["[active_view] ..."]
        I4["[rooms_spaces] ..."]
        I5["[mep_systems] ..."]
        I6["[system_detail] ..."]
        I7["[knowledge_base] RAG results ..."]
        I8[Conversation history]
    end

    P0 & P1 & P1b & P2 & P3 & P4 & P5 & P6 & P7 & P8 --> G1
    G1 --> G2 --> G3
    G3 --> I1
    I0 --> I1
    I1 --> I2 --> I2b --> I3 --> I4 --> I5 --> I6 --> I7 --> I8
```

## Clash Avoidance Rerouting Pipeline

```mermaid
flowchart TD
    A["User: avoid_clash<br/>shift=pipe, stand=duct"] --> B["Collect Elements<br/>by category + level filter"]
    B --> C["BoundingBoxClashDetector<br/>AABB overlap + tolerance (mm→ft)"]
    C --> D{Clash pairs found?}
    D -->|No| Z1["No clashes → return summary"]
    D -->|Yes| E["ConnectedComponentAnalyzer<br/>BFS graph → ClashGroups"]
    E --> F["For each ClashGroup"]
    F --> G["DirectionClassifier<br/>XY dot product → Parallel / Perpendicular"]
    G --> H["DoglegGeometry<br/>6 waypoints (P0..P5)"]
    H --> I["MepSegmentFactory<br/>Create 5 segments matching original type"]
    I --> J["doc.Regenerate()"]
    J --> K["FittingConnector<br/>4 elbows at junction points"]
    K --> L{All connected?}
    L -->|Yes| M["Delete original element"]
    L -->|No| N["Rollback segments"]
    M --> O["Next element in group"]
    N --> O
    O --> F
    F -->|"All groups done"| P["Return RerouteResult<br/>success/fail counts + details"]
```

## Project Dependency Graph

```mermaid
graph BT
    Core["RevitChatBot.Core<br/>Agent, LLM, Skills, Context, CodeGen"]
    Services["RevitChatBot.RevitServices<br/>RevitAPI.dll wrappers"]
    MEP["RevitChatBot.MEP<br/>9 skill domains (25+ skills) + 8 context providers"]
    Knowledge["RevitChatBot.Knowledge<br/>RAG: Embeddings + VectorStore"]
    Addin["RevitChatBot.Addin<br/>WPF + WebView2 + wiring"]
    UI["revitchatbot-ui<br/>React + Vite + Tailwind"]

    Services --> Core
    MEP --> Core
    MEP --> Services
    Knowledge --> Core
    Addin --> Core
    Addin --> Services
    Addin --> MEP
    Addin --> Knowledge
    Addin -.->|"loads dist/index.html"| UI
```

## Solution Structure

```
RevitChatBot.slnx
├── src/
│   ├── RevitChatBot.Core/               # Reusable - no Revit dependency
│   │   ├── Agent/                        # AgentOrchestrator (ReAct), ChatSessionV2, AgentStep
│   │   ├── CodeGen/                      # RoslynCodeCompiler, DynamicCodeExecutor, DynamicCodeSkill, CodeSecurityValidator, RevitApiCheatSheet, CodeExamplesLibrary
│   │   ├── Memory/                       # MemoryManager, ConversationStore, ConversationSummarizer, LearnedFactsStore, UserPreferencesStore, SessionAnalytics, MemoryContextProvider
│   │   ├── LLM/                          # OllamaService, ChatSession, PromptBuilder
│   │   ├── Skills/                       # ISkill, SkillAttribute, SkillRegistry, SkillExecutor
│   │   ├── Context/                      # IContextProvider, ContextManager, ContextData
│   │   └── Models/                       # ChatMessage, BridgeMessage, ToolCall
│   │
│   ├── RevitChatBot.RevitServices/       # Revit API wrappers
│   │   ├── RevitElementService.cs
│   │   ├── RevitDocumentService.cs
│   │   └── RevitMEPService.cs
│   │
│   ├── RevitChatBot.Knowledge/           # RAG module
│   │   ├── Embeddings/                   # IEmbeddingService, OllamaEmbeddingService
│   │   ├── VectorStore/                  # IVectorStore, InMemoryVectorStore
│   │   ├── Documents/                    # IDocumentLoader, TextLoader, JsonLoader, PdfLoader
│   │   └── Search/                       # KnowledgeManager, KnowledgeContextProvider, KnowledgeSearchSkill
│   │
│   ├── RevitChatBot.MEP/                 # MEP domain skills & context
│   │   ├── Skills/
│   │   │   ├── Query/                    # QueryElements, SystemOverview, SystemAnalysis, ConnectivityAnalysis, SpaceAnalysis
│   │   │   ├── Check/                    # CheckVelocity, CheckSlope, CheckConnection, CheckInsulation, CheckClearance, CheckFireDamper, ModelAudit, ComplianceCheck
│   │   │   ├── HVAC/                     # HvacLoadCalculation, DuctSizing
│   │   │   ├── Plumbing/                 # PipeSizing, DrainageCalculation
│   │   │   ├── Electrical/               # ElectricalLoad
│   │   │   ├── Coordination/             # ClashDetection, AdvancedClashDetection, AvoidClash
│   │   │   │   └── Routing/             # MepRoutingEngine, DoglegGeometry, BoundingBoxClashDetector,
│   │   │   │                            # ConnectedComponentAnalyzer, DirectionClassifier,
│   │   │   │                            # MepSegmentFactory, FittingConnector
│   │   │   ├── Calculation/              # MEPCalculation
│   │   │   ├── Modify/                   # CreateElement, ModifyParameter, BatchModify, SplitDuctPipe
│   │   │   └── Report/                   # ExportReport
│   │   └── Context/                      # 8 providers (ProjectInfo, ActiveView, Selected, MEPSystem, RoomSpace, SystemDetail, LevelSummary, ModelInventory)
│   │
│   └── RevitChatBot.Addin/              # Revit 2025 Add-in entry point
│       ├── App.cs                        # IExternalApplication + Ribbon
│       ├── Commands/                     # ShowChatBotCommand
│       ├── Views/                        # WPF Window + WebView2
│       ├── Bridge/                       # WebViewBridge (React <-> C# + Knowledge + Agent)
│       └── Handlers/                     # ExternalEventHandler
│
├── ui/revitchatbot-ui/                   # React frontend
│   └── src/
│       ├── components/                   # ChatWindow, MessageBubble, InputBar, SkillPanel, SettingsPanel
│       ├── hooks/                        # useRevitBridge
│       ├── services/                     # bridge.ts
│       └── types/                        # TypeScript interfaces
│
├── docs/
│   ├── references/                       # Reference documentation links
│   └── knowledge/                        # RAG knowledge base (standards, specs)
│       ├── bim-standards/                # ISO 19650, 29481, 12006, 7817, 23386, DIN 276
│       │   ├── ISO-19650/                # 5 parts: concepts, delivery, operations, exchange, security
│       │   ├── ISO-29481/                # 3 parts: IDM methodology, interaction, data schema
│       │   ├── ISO-12006/                # Classification framework & object-oriented info
│       │   ├── ISO-other/                # ISO 7817, 12911, 23386, 23387 + unidentified
│       │   ├── DIN/                      # DIN 276 Building costs
│       │   └── bim-standards-knowledge.md # Comprehensive RAG summary
│       ├── mep-standards/                # ASHRAE, SMACNA, TCVN, DIN EN standards (64 PDFs)
│       │   ├── mep-routing-knowledge.md  # Clash detection, dogleg routing, MEP creation
│       │   └── mep-spatial-clearance-knowledge.md # Ray casting, room mapping, split/union
│       ├── revit-api/                    # Revit API notes
│       └── project-specs/                # Project-specific specs
│
├── Directory.Build.props                 # Revit API path config
└── .gitignore
```

## MEP Skills

| Domain | Skill | Description |
|---|---|---|
| **Query** | `query_elements` | Query ducts, pipes, equipment by category/system |
| **Query** | `mep_system_overview` | Comprehensive overview of all MEP systems |
| **Query** | `system_analysis` | Analyze MEP systems: group by classification, count, total length |
| **Query** | `connectivity_analysis` | BFS traversal of MEP network from a starting element |
| **Query** | `space_analysis` | Analyze MEP spaces: area, volume, design vs actual airflow |
| **Check** | `check_duct_velocity` | Check duct velocity violations (max m/s threshold) |
| **Check** | `check_pipe_slope` | Check pipe slope violations (min % threshold) |
| **Check** | `check_disconnected_mep` | Find disconnected MEP elements across all categories |
| **Check** | `check_insulation` | Check missing insulation coverage (ducts/pipes) |
| **Check** | `check_clearance` | Check elevation/clearance conflicts (min height m) |
| **Check** | `check_fire_dampers` | Check fire damper connection status |
| **Check** | `check_parameter_completeness` | Check parameter fill rate on elements |
| **Check** | `model_audit` | Model audit: warnings, element counts, top issues |
| **HVAC** | `hvac_load_calculation` | Cooling/heating load per space |
| **HVAC** | `duct_sizing_analysis` | Velocity-based duct sizing check |
| **Plumbing** | `pipe_sizing_analysis` | Pipe velocity and diameter analysis |
| **Plumbing** | `drainage_analysis` | Sanitary/storm slope and sizing |
| **Electrical** | `electrical_load_analysis` | Panel load distribution and circuits |
| **Coordination** | `clash_detection` | Basic bounding box clash detection |
| **Coordination** | `advanced_clash_detection` | Severity-classified, level-filtered clashes |
| **Coordination** | `avoid_clash` | MEP rerouting engine: detect clashes → group (BFS) → classify direction → dogleg reroute (5 segments + 4 elbows). Supports Pipe/Duct/CableTray/Conduit. Analyze or execute mode. |
| **Check** | `check_directional_clearance` | Ray casting (ReferenceIntersector) in 6 directions against walls/floors/ceilings/columns/beams. Linked model support. Multi-condition threshold checking. |
| **Query** | `map_room_to_mep` | Map Room/Space parameters to MEP elements via IsPointInRoom/Space API. Above-space detection. Report or execute mode. |
| **Modify** | `split_duct_pipe` | Split duct/pipe into equal segments at specified distance. Auto-creates union fittings. Sequential numbering. Bi-directional support. |
| **Calculation** | `mep_calculation` | Duct/pipe summary, airflow analysis |
| **Modify** | `modify_parameter` | Change element parameter values |
| **Modify** | `create_element` | Create new ducts or pipes |
| **Modify** | `batch_modify` | Batch modify parameters on multiple elements |
| **Report** | `export_report` | Generate project overview, schedules, reports |
| **Knowledge** | `search_knowledge_base` | RAG search through standards/specs |
| **CodeGen** | `execute_revit_code` | Generate & execute any C# via Roslyn (unlimited operations) |

## Prerequisites

- **Revit 2025** (with .NET 8 runtime)
- **Ollama** running locally with `qwen2.5:7b` + `nomic-embed-text` models
- **Node.js 18+** (for building the React UI)
- **.NET 8 SDK** or **Visual Studio 2022**

## Setup

### 1. Configure Revit API Path

Edit `Directory.Build.props` in the root directory:

```xml
<RevitApiPath>C:\Program Files\Autodesk\Revit 2025</RevitApiPath>
```

### 2. Build the React UI

```bash
cd ui/revitchatbot-ui
npm install
npm run build
```

### 3. Build the Solution

```bash
dotnet build RevitChatBot.slnx
```

### 4. Pull Ollama Models

```bash
ollama serve
ollama pull qwen2.5:7b
ollama pull nomic-embed-text
```

### 5. Add Knowledge Base (Optional)

Place standards documents (`.txt`, `.md`, `.json`, `.pdf`) in `docs/knowledge/`:

- `bim-standards/` - BIM Standards (ISO 19650, ISO 29481, ISO 12006, ISO 7817, ISO 23386/23387, DIN 276)
  - `ISO-19650/` - Information management (Parts 1-5): CDE, EIR, BEP, delivery & operations
  - `ISO-29481/` - Information Delivery Manual (IDM): methodology, interaction framework, data schema
  - `ISO-12006/` - Construction classification framework & object-oriented information
  - `ISO-other/` - Level of info need (7817), BIM implementation (12911), data templates (23386/23387)
  - `DIN/` - DIN 276 Building costs (KG 400 = MEP systems)
  - `bim-standards-knowledge.md` - Comprehensive summary for RAG indexing
- `mep-standards/` - ASHRAE, SMACNA, TCVN, DIN EN standards (64 PDFs)
- `revit-api/` - Revit API reference notes, 2025 API changes
- `project-specs/` - Project-specific specifications

The chatbot will auto-index all files (including PDFs via PdfDocumentLoader) and use them via RAG.

### 6. Install the Add-in

Copy from the build output to Revit's add-in folder:

```
%APPDATA%\Autodesk\Revit\Addins\2025\
├── RevitChatBot.addin
├── RevitChatBot.Addin.dll
├── RevitChatBot.Core.dll
├── RevitChatBot.MEP.dll
├── RevitChatBot.Knowledge.dll
├── RevitChatBot.RevitServices.dll
├── Microsoft.CodeAnalysis.*.dll     # Roslyn compiler
├── Microsoft.Web.WebView2.*.dll
├── UglyToad.PdfPig.*.dll            # PDF loader
├── knowledge/                    # Copy from docs/knowledge/
│   └── (standards files)
├── knowledge_index.json          # Auto-generated vector index
└── ui/
    └── index.html (+ assets/)
```

### 7. Launch Revit

Open Revit 2025. Find the **MEP ChatBot** button in the ribbon panel.

## Adding New Skills

1. Create a class in the appropriate `RevitChatBot.MEP/Skills/<Domain>/` folder
2. Implement `ISkill` and add `[Skill]` + `[SkillParameter]` attributes
3. Skills are auto-discovered via reflection at startup

```csharp
[Skill("my_skill", "Description for the LLM")]
[SkillParameter("param1", "string", "What this param does")]
public class MySkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        var result = await context.RevitApiInvoker!(doc => { ... });
        return SkillResult.Ok("Done", result);
    }
}
```

## Adding Knowledge Documents

Place files in `docs/knowledge/` with one of these formats:

**Text/Markdown** - Split into chunks by paragraph:
```
docs/knowledge/mep-standards/ashrae-duct-sizing.md
```

**PDF** - Extracted via PdfPig, auto-categorized by standard number:
```
docs/knowledge/mep-standards/DIN EN 16798-1 - Language English 3334892.pdf
```

**JSON** - Structured entries:
```json
[
  { "content": "ASHRAE recommends max duct velocity of 2000 FPM for main ducts...", "category": "HVAC" },
  { "content": "Minimum pipe slope for sanitary drainage: 1/8 inch per foot...", "category": "Plumbing" }
]
```

## Memory System (Cross-Session Learning)

The chatbot includes a comprehensive memory system that persists across sessions:

```mermaid
flowchart TB
    subgraph MemorySystem [MemoryManager — memory/ directory]
        CS[ConversationStore<br/>Per-project JSON files]
        SUM[ConversationSummarizer<br/>LLM-powered summary<br/>when history > 30 msgs]
        LF[LearnedFactsStore<br/>Extracts facts from corrections<br/>& user-provided info]
        UP[UserPreferencesStore<br/>Language, conventions,<br/>skill usage frequency]
        SA[SessionAnalytics<br/>Skill call stats,<br/>code gen success rate]
    end

    subgraph Triggers [When does memory activate?]
        T1["User sends message → detect language, record analytics"]
        T2["Assistant responds → extract learned facts via LLM"]
        T3["Skill executes → record timing, success/failure"]
        T4["Every 5 messages → auto-persist to disk"]
        T5["Window closes → final persist all memory"]
        T6["Window opens → restore previous conversation"]
    end

    subgraph Injection [How memory is used]
        MCP["MemoryContextProvider (Priority 5)"]
        MCP --> P1["[learned_facts] CHW pipes use DN150 minimum..."]
        MCP --> P2["[user_preferences] Language: Vietnamese..."]
        MCP --> P3["[conversation_memory] Summary of previous session..."]
    end

    T1 & T2 & T3 --> MemorySystem
    T4 & T5 --> CS
    T6 --> CS
    MemorySystem --> MCP
    MCP -->|"injected into every LLM call"| LLM["Ollama LLM"]
```

### Memory Files Structure

```
memory/
├── conversations/
│   ├── conv_my_project_name.json      # Per-project conversation snapshot
│   └── conv_another_project.json
├── learned_facts.json                  # Facts extracted from user corrections
├── preferences.json                    # User preferences (language, conventions)
└── analytics.json                      # Skill usage stats, code gen metrics
```

### Self-Learning Flow

1. **User corrects the bot**: "Không phải vậy, ống CHW trong dự án này dùng DN150 tối thiểu"
2. **LearnedFactsStore** detects correction markers ("không phải", "thực ra", etc.)
3. **LLM extracts facts**: `["CHW pipes in this project use DN150 minimum"]`
4. **Fact is persisted** to `learned_facts.json`
5. **Next session**: fact is injected into `[learned_facts]` context, LLM remembers

## References

- [Revit API 2025.3 Docs](https://www.revitapidocs.com/2025.3/)
- [Ollama GitHub + API Docs](https://github.com/ollama/ollama)
- [WebView2 Documentation](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
