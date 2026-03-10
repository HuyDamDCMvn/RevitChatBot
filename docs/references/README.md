# Reference Documentation

## Revit API

| Resource | URL | Notes |
|---|---|---|
| Revit API 2025.3 Docs | https://www.revitapidocs.com/2025.3/ | Online API reference - namespace explorer, class docs, What's New |
| Revit API 2025 Docs | https://www.revitapidocs.com/2025/ | Base 2025 release API reference |
| Revit 2025 SDK | `C:\Program Files\Autodesk\Revit 2025\` | Local installation - RevitAPI.dll (v25.4.30.30), RevitAPIUI.dll |

### Key Revit API Namespaces for MEP

| Namespace | Usage |
|---|---|
| `Autodesk.Revit.DB` | Core database classes: Element, Document, Transaction, FilteredElementCollector |
| `Autodesk.Revit.DB.Mechanical` | Duct, MechanicalSystem, MechanicalSystemType, DuctType |
| `Autodesk.Revit.DB.Plumbing` | Pipe, PipingSystem, PipingSystemType, PipeType |
| `Autodesk.Revit.DB.Electrical` | ElectricalSystem, Wire |
| `Autodesk.Revit.UI` | UIApplication, IExternalCommand, IExternalApplication, ExternalEvent |

## Ollama LLM

| Resource | URL | Notes |
|---|---|---|
| Ollama GitHub | https://github.com/ollama/ollama | Main repo - installation, model library |
| Ollama API Docs | https://github.com/ollama/ollama/blob/main/docs/api.md | REST API reference - chat, tools, streaming, embeddings |
| Ollama Model Library | https://ollama.com/library | Browse and download models |

### API Endpoints Used

| Endpoint | Method | Usage in Project |
|---|---|---|
| `/api/chat` | POST | Main chat completion with tool calling (`OllamaService.ChatAsync`) |
| `/api/generate` | POST | Structured output for intent extraction (`OllamaService.GenerateAsync`, `QueryPreprocessor.AnalyzeDeepAsync`) |
| `/api/embed` | POST | Vector embeddings for RAG + skill routing (`OllamaEmbeddingService`, `SemanticSkillRouter`) |
| `/api/tags` | GET | List local models (`OllamaService.ListModelsAsync`, health check) |

### Ollama /api/generate — Full Reference (for ChatBot self-learning)

Source: https://docs.ollama.com/api/generate

#### Request Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `model` | string | (required) | Model name (e.g., `qwen2.5:7b`) |
| `prompt` | string | | Text for the model to generate a response from |
| `suffix` | string | | For fill-in-the-middle models, text after user prompt and before model response |
| `images` | string[] | | Base64-encoded images for multimodal models |
| `format` | string \| object | | `"json"` or JSON schema object for structured outputs |
| `system` | string | | System prompt for the generation |
| `stream` | boolean | true | When true, returns stream of partial responses |
| `think` | boolean \| string | | When true, returns separate thinking output. Can be `true`/`false` or `"high"`/`"medium"`/`"low"` |
| `raw` | boolean | | When true, raw response without prompt templating |
| `keep_alive` | string \| number | | Model keep-alive duration (e.g., `"5m"`, `0` to unload) |
| `options` | object | | Runtime options (see below) |
| `logprobs` | boolean | | Return log probabilities of output tokens |
| `top_logprobs` | integer | | Number of most likely tokens at each position |

#### Options (Runtime Generation Control)

| Option | Type | Description |
|---|---|---|
| `seed` | integer | Random seed for reproducible outputs |
| `temperature` | float | Controls randomness (higher = more random). **Project default: 0.3** |
| `top_k` | integer | Limits next token selection to K most likely |
| `top_p` | float | Cumulative probability threshold for nucleus sampling |
| `min_p` | float | Minimum probability threshold for token selection |
| `stop` | string \| string[] | Stop sequences that halt generation |
| `num_ctx` | integer | Context length size in tokens. **Project default: 8192** |
| `num_predict` | integer | Maximum tokens to generate |

#### Response Fields

| Field | Type | Description |
|---|---|---|
| `model` | string | Model name |
| `created_at` | string | ISO 8601 timestamp |
| `response` | string | Generated text response |
| `thinking` | string | Generated thinking output (when `think: true`) |
| `done` | boolean | Whether generation has finished |
| `done_reason` | string | Reason generation stopped (e.g., `"stop"`) |
| `total_duration` | integer | Total time in nanoseconds |
| `load_duration` | integer | Model loading time in nanoseconds |
| `prompt_eval_count` | integer | Number of input tokens |
| `prompt_eval_duration` | integer | Prompt evaluation time in nanoseconds |
| `eval_count` | integer | Output tokens generated |
| `eval_duration` | integer | Token generation time in nanoseconds |

#### Structured Output Example (used by QueryPreprocessor)

```bash
curl http://localhost:11434/api/generate -d '{
  "model": "qwen2.5:7b",
  "prompt": "Analyze this MEP query: kiểm tra bảo ôn ống lạnh tầng 2",
  "stream": false,
  "format": {
    "type": "object",
    "properties": {
      "intent": {"type": "string", "enum": ["query","check","modify","calculate","create","delete","explain","analyze","report"]},
      "category": {"type": "string"},
      "system_type": {"type": "string"},
      "level": {"type": "string"},
      "needs_clarification": {"type": "boolean"},
      "clarification_question": {"type": "string"}
    },
    "required": ["intent", "needs_clarification"]
  },
  "options": {
    "temperature": 0.1,
    "num_ctx": 2048
  }
}'
```

#### Thinking Mode Example (for complex ReAct reasoning)

```bash
curl http://localhost:11434/api/generate -d '{
  "model": "qwen2.5:7b",
  "prompt": "Plan the steps to check full QA/QC for MEP model...",
  "think": true,
  "stream": false
}'
# Response includes both "thinking" and "response" fields
```

#### Key Usage Patterns in Project

| Use Case | Configuration | Module |
|---|---|---|
| Intent extraction | `format: JSON schema`, `temperature: 0.1`, `num_ctx: 2048` | `QueryPreprocessor.AnalyzeDeepAsync` |
| Code generation planning | `think: true`, `temperature: 0.2` | Future: `AgentOrchestrator` codegen planning |
| Quick classification | `num_predict: 50`, `temperature: 0` | Future: `ConversationQueryRewriter` |
| Batch entity extraction | `format: JSON array schema`, `temperature: 0` | Future: `IntentDecomposer` |

### Current Model

- **qwen2.5:7b** — Default model for chat + generate (`OllamaOptions.Model`)
- **nomic-embed-text** — Embedding model for RAG + skill routing

## Roslyn (Dynamic Code Generation)

| Resource | URL | Notes |
|---|---|---|
| Roslyn GitHub | https://github.com/dotnet/roslyn | C# and VB compiler + code analysis APIs |
| Roslyn NuGet | https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp | `Microsoft.CodeAnalysis.CSharp` package used for runtime compilation |
| Scripting API | https://github.com/dotnet/roslyn/blob/main/docs/wiki/Scripting-API-Samples.md | Roslyn Scripting API reference |

### Usage in Project

| Component | Purpose |
|---|---|
| `RoslynCodeCompiler` | Runtime C# compilation with Revit API assembly references |
| `DynamicCodeExecutor` | Security validation + compilation + execution pipeline |
| `DynamicCodeSkill` | LLM-callable skill wrapping `execute_revit_code` tool |
| `CodeSecurityValidator` | Whitelist-based security check (blocks file/network/process APIs) |

## PdfPig (PDF Document Loader)

| Resource | URL | Notes |
|---|---|---|
| PdfPig GitHub | https://github.com/UglyToad/PdfPig | Apache 2.0 licensed PDF text extraction for .NET |
| PdfPig NuGet | https://www.nuget.org/packages/UglyToad.PdfPig | Used in `PdfDocumentLoader` for RAG indexing |

## Revit API Community References

| Resource | URL | Notes |
|---|---|---|
| RevitAPI EveryDay | https://github.com/chuongmep/RevitAPI_EveryDay | Revit API patterns reference (Filter, Parameter, Geometry, Intersect) |

## React (UI Framework)

| Resource | URL | Notes |
|---|---|---|
| React GitHub | https://github.com/facebook/react | Source code — compiler, packages, scripts. MIT licensed |
| React Docs | https://react.dev | Official documentation — Quick Start, Tutorial, API Reference |
| React 19 Release | https://react.dev/blog/2024/12/05/react-19 | Latest stable (v19.x) — Actions, use(), Server Components |
| React TypeScript Cheatsheet | https://react-typescript-cheatsheet.netlify.app | TS patterns for React — Props, Hooks, Context, HOC |

### Key React Concepts (project reference)

| Concept | Usage in revitchatbot-ui |
|---|---|
| Functional Components | `ChatWindow.tsx`, `MessageBubble.tsx`, `InputBar.tsx` |
| Hooks (`useState`, `useEffect`, `useRef`) | State management, auto-scroll, Revit bridge lifecycle |
| Custom Hooks | `useRevitBridge.ts` — WebView2 message communication |
| React Markdown | `react-markdown` for rendering LLM responses with tables/code |
| Tailwind CSS | Utility-first styling for all components |
| Vite | Build tool — `vite build` produces `dist/` loaded by WebView2 |

### Current Stack (ui/revitchatbot-ui)

| Package | Version | Purpose |
|---|---|---|
| react | ^18.3.1 | UI library |
| react-dom | ^18.3.1 | DOM rendering |
| react-markdown | ^9.0.1 | Markdown rendering for chat messages |
| typescript | ^5.6.3 | Type safety |
| vite | ^6.0.3 | Build tool + dev server |
| tailwindcss | ^3.4.16 | Utility CSS framework |

### Useful React Patterns for Future Development

| Pattern | When to Use | React Docs Link |
|---|---|---|
| `useMemo` / `useCallback` | Optimize re-renders for large chat histories | https://react.dev/reference/react/useMemo |
| `useReducer` | Complex state (chat + skills + settings) | https://react.dev/reference/react/useReducer |
| Context API | Global state (theme, user prefs, connection status) | https://react.dev/reference/react/useContext |
| Suspense + lazy | Code-split settings panel, skill browser | https://react.dev/reference/react/Suspense |
| Error Boundaries | Graceful error handling in chat | https://react.dev/reference/react/Component#catching-rendering-errors-with-an-error-boundary |
| React 19 `use()` | Read promises/context in render (future upgrade) | https://react.dev/reference/react/use |

## WebView2

| Resource | URL | Notes |
|---|---|---|
| WebView2 Docs | https://learn.microsoft.com/en-us/microsoft-edge/webview2/ | Microsoft Edge WebView2 documentation |
| WebView2 NuGet | https://www.nuget.org/packages/Microsoft.Web.WebView2 | WPF package used in Revit Add-in |
| React + WebView2 Communication | https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/communicate-btwn-web-native | postMessage pattern used in WebViewBridge |
