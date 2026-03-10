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
| `/api/tags` | GET | List local models (`OllamaService.ListModelsAsync`, health check) |

### Current Model

- **qwen2.5:7b** - Default model configured in `OllamaOptions`

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
