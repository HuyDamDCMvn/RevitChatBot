# Changelog

All notable changes to RevitChatBot will be documented in this file.

## [1.1.0] - 2026-03-12

### Added

- **Scope parameter** (`active_view` / `entire_model`) for 28 skills — query, check, coordination, and report skills now support scoping to active view or entire model for faster, targeted analysis.
- **IOllamaService interface** — abstraction layer for LLM service, enabling cloud routing and testing.
- **OllamaCloudRouter** — intelligent routing between local Ollama and cloud LLM providers.
- **Enhanced SemanticSkillRouter** — improved embedding-based skill matching with keyword fallback and success-rate boosting (365+ lines of improvements).
- **Expanded MepGlossary** — 186+ new bilingual MEP term mappings for better Vietnamese/English query understanding.
- **FewShotIntentLibrary expansion** — 100+ new curated examples for more accurate intent resolution.
- **InteractionRecorder enhancements** — richer interaction logging for self-training pipeline.
- **Cursor development rules** — `.cursor/rules/revit-code-quality.mdc` for consistent AI-assisted development.

### Improved

- **WebViewBridge** — major refactoring (648 lines) for more robust React ↔ C# communication, better error handling, and streamlined initialization.
- **RevitEventHandler** — new `ExecuteWithUIAsync` method for UIDocument operations (selection, view switching).
- **AgentOrchestrator** — improved plan execution, better error recovery, and cross-learning event publishing.
- **LearningCortex** — enhanced cross-module meta-analysis and failure pattern detection.
- **CrossSkillCorrelator** — improved skill correlation discovery and persistence.
- **FailureRecoveryLearner** — better recovery advice generation from failure patterns.
- **CompositeSkillEngine** — smarter composite skill discovery and chain detection.
- **DynamicCodeSkill** — better code generation context and auto-fix integration.
- **AdvancedFilterSkill** — more flexible element filtering with scope support.
- **BatchUpdateFromCsvSkill** — enhanced CSV import with better error handling.
- **CheckClearanceSkill, CheckSlopeSkill, CheckVelocitySkill** — scope-aware with improved reporting.
- **ClashDetectionSkill** — scope parameter and performance improvements.
- **UI Settings Panel** — model auto-detection and backend/frontend settings sync.

### Fixed

- **avoid_clash** now supports same-category clashes and selected elements correctly.
- **Auto-detect LLM models** — settings panel syncs available models between Ollama backend and UI.
- **Release packaging** now includes RAG knowledge docs (md, json, txt) for out-of-the-box knowledge base.
- **Deploy target** copies knowledge docs during Release build to Revit Addins folder.

## [1.0.0] - 2026-03-11

### Added

- **ReAct Agent** — Multi-step reasoning engine (Thought → Action → Observation loop, max 8 steps) with structured action plan review and user confirmation for destructive operations.
- **40+ MEP Skills** across 9 domains: Query, Check, HVAC, Plumbing, Electrical, Coordination, Modify, Calculation, and Report.
- **Clash Avoidance Rerouting Engine** — Automatic MEP rerouting with dogleg geometry (5 segments + 4 elbows), BFS-based clash grouping, and directional classification. Supports Pipe, Duct, Conduit, and Cable Tray.
- **MEP System Graph Traversal** — Full BFS topology traversal via connectors (MEPCurve, FamilyInstance, FabricationPart) with domain filtering and detailed path output.
- **Directional Clearance Check** — Ray casting (ReferenceIntersector) in 6 directions with linked model support and multi-condition thresholds.
- **Room/Space to MEP Mapping** — Accurate mapping using IsPointInRoom/IsPointInSpace API with above-offset detection for ceiling-mounted elements.
- **Split Duct/Pipe/Conduit/Cable Tray** — Equal segment splitting with sequential numbering; BreakCurve for Duct/Pipe, CopyElement workaround for Conduit/Cable Tray.
- **Routing Preferences Query** — Inspect preferred fittings (elbows, tees, crosses, transitions) and junction types for any pipe/duct type.
- **Dynamic Code Generation** — Roslyn-powered C# runtime compilation with Revit API references, security validation, and auto-retry on compilation errors.
- **Self-Evolving Skill System** — Successful code generation is cached (CodeGenLibrary), promotable to named skills (DynamicSkillRegistry), with error pattern tracking (CodePatternLearning).
- **RAG Knowledge Pipeline** — Document indexing (Text, JSON, PDF via PdfPig) with Ollama embeddings (nomic-embed-text), in-memory vector store, and cosine similarity search.
- **Cross-Session Memory** — Conversation persistence, learned facts extraction, user preference tracking, session analytics, and conversation summarization.
- **Smart Query Understanding** — Bilingual (Vietnamese/English) intent/entity extraction with MepGlossary (120+ MEP terms), QueryPreprocessor (fast + deep analysis), ClarificationFlow, and FewShotIntentLibrary.
- **Advanced LLM Intelligence** — ConversationQueryRewriter, ContextWindowOptimizer, SmartHistoryPruner, MultiIntentDecomposer, AdaptiveFewShotLearning, DynamicGlossary, SkillSuccessFeedback, PromptCache, ResponseQualityValidator, and StreamingIntentDetector.
- **Autonomous Self-Training Pipeline** — PlanReplayStore (experience replay), InteractionRecorder, SelfEvaluator (LLM self-critique), ImprovementStore, CompositeSkillEngine, KnowledgeSynthesizer, SkillGapAnalyzer, and SkillDiscoveryAgent. Background scheduler runs every 30 minutes.
- **3D Visualization Engine** — DirectContext3D highlighting with semantic severity colors (critical/warning/ok/info), clash visualization, and visual workflow learning.
- **Learning Cortex** — Cross-module meta-analysis: CrossSkillCorrelator, FailureRecoveryLearner, ContextUsageTracker, VisualFeedbackLearner, and VisualWorkflowComposer.
- **Automation Modes** — Three modes: SuggestOnly (analysis without execution), PlanAndApprove (review before destructive actions), AutoExecute (full automation).
- **React UI** — Modern chat interface built with Vite + TypeScript + Tailwind CSS, rendered via WebView2. Features: Markdown rendering, Mermaid diagrams, settings panel, health check, model info, skill execution progress.
- **Revit 2025 Integration** — Nice3point Revit Toolkit/Extensions for modern ExternalApplication/Command patterns, AsyncEventHandler for thread-safe API calls.
- **Install/Uninstall Scripts** — PowerShell scripts for one-click deployment to Revit 2025 Addins folder.
- **Knowledge Base** — Pre-loaded BIM standards (ISO 19650, ISO 29481, ISO 12006, DIN 276) and MEP standards reference.

### Technical Details

- **6 C# projects**: Core (no Revit dependency), RevitServices, MEP, Knowledge, Visualization, Addin
- **229 C# source files**, 40+ skills, 8 context providers
- **React frontend**: TypeScript, Tailwind CSS, Mermaid diagram support
- **LLM**: Ollama with qwen2.5:7b (local inference, no cloud dependency)
- **Embeddings**: nomic-embed-text for RAG and semantic skill routing
- **Target**: Revit 2025 (.NET 8)
