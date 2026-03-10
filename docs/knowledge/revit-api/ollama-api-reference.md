# Ollama API Reference — ChatBot Self-Learning Document

Source: https://docs.ollama.com/api/generate

## Overview

Ollama provides a local REST API at `http://localhost:11434` for running LLM inference.
The ChatBot uses three main endpoints:
- `/api/chat` — conversational chat with tool/function calling
- `/api/generate` — single-turn generation with structured output
- `/api/embed` — vector embeddings for RAG and skill routing

## /api/generate — Structured Output Endpoint

### Purpose
Generate a response for a provided prompt. Unlike `/api/chat`, this endpoint:
- Supports the `format` parameter for guaranteed JSON schema output
- Supports `think` mode for chain-of-thought reasoning
- Does not maintain conversation history (single-turn)
- Best for: intent extraction, entity parsing, classification, structured analysis

### Request Schema

```json
{
  "model": "qwen2.5:7b",
  "prompt": "Analyze this MEP query...",
  "system": "You are an intent classifier...",
  "stream": false,
  "format": { "type": "object", "properties": { ... }, "required": [...] },
  "think": true,
  "options": {
    "temperature": 0.1,
    "num_ctx": 2048,
    "num_predict": 200,
    "top_k": 40,
    "top_p": 0.9,
    "seed": 42,
    "stop": ["\n\n"]
  },
  "keep_alive": "10m"
}
```

### Key Parameters

#### format (Structured Output)
Forces the model to output valid JSON matching a schema.
- Use `"json"` for free-form JSON
- Use a JSON Schema object for strict structure
- The model will ALWAYS output valid JSON matching the schema
- Essential for reliable intent extraction and entity parsing

Example schema for MEP intent extraction:
```json
{
  "type": "object",
  "properties": {
    "intent": {"type": "string", "enum": ["query","check","modify","calculate","create","delete","explain","analyze","report"]},
    "category": {"type": "string", "enum": ["duct","pipe","conduit","cable_tray","equipment","fitting","sprinkler","all"]},
    "system_type": {"type": "string"},
    "level": {"type": "string"},
    "element_ids": {"type": "array", "items": {"type": "integer"}},
    "needs_clarification": {"type": "boolean"},
    "clarification_question": {"type": "string"}
  },
  "required": ["intent", "needs_clarification"]
}
```

#### think (Chain-of-Thought)
When enabled, the model generates internal reasoning before the final response.
- `true` or `"medium"` — standard thinking
- `"high"` — more thorough reasoning (slower)
- `"low"` — brief reasoning (faster)
- Response includes both `thinking` and `response` fields
- Useful for complex multi-step planning (QA/QC workflows, codegen planning)

#### options.temperature
Controls randomness in generation:
- `0.0` — deterministic (best for classification, extraction)
- `0.1-0.3` — low randomness (best for structured tasks, tool selection)
- `0.5-0.7` — moderate (general conversation)
- `0.8-1.0` — high randomness (creative tasks)
- **Project default: 0.3** for tool selection accuracy

#### options.num_ctx
Context window size in tokens:
- `2048` — sufficient for intent extraction (fast)
- `4096` — default for simple queries
- `8192` — **project default** for full context (model inventory + few-shot + history)
- `16384+` — for very long contexts (requires more VRAM)
- When exceeded, older tokens are silently dropped from the beginning

#### options.num_predict
Maximum output tokens:
- `50` — for classification/intent only
- `200` — for structured extraction
- `1000` — for explanations
- `-1` or not set — unlimited (model decides when to stop)

### Response Schema

```json
{
  "model": "qwen2.5:7b",
  "created_at": "2025-10-17T23:14:07.414671Z",
  "response": "{\"intent\":\"check\",\"category\":\"pipe\",\"needs_clarification\":false}",
  "thinking": "The user wants to check insulation on chilled water pipes on Level 2...",
  "done": true,
  "done_reason": "stop",
  "total_duration": 174560334,
  "load_duration": 101397084,
  "prompt_eval_count": 11,
  "prompt_eval_duration": 13074791,
  "eval_count": 18,
  "eval_duration": 52479709
}
```

### Performance Metrics (from response)
- `prompt_eval_count` — input tokens consumed (monitor for context overflow)
- `eval_count` — output tokens generated
- `total_duration` — total time in nanoseconds (divide by 1e6 for ms)
- `eval_duration / eval_count` — tokens per second calculation

## /api/chat — Conversational Endpoint

### Purpose
Multi-turn conversation with tool/function calling support.
Used as the primary endpoint for the ReAct agent.

### Key Differences from /api/generate
| Feature | /api/chat | /api/generate |
|---|---|---|
| Messages history | Yes (array of messages) | No (single prompt) |
| Tool/function calling | Yes (`tools` parameter) | No |
| Structured output (`format`) | Not supported | Yes |
| System prompt | In messages array | Separate `system` parameter |
| `think` mode | Supported | Supported |

### Tool Calling Format
```json
{
  "model": "qwen2.5:7b",
  "messages": [
    {"role": "system", "content": "..."},
    {"role": "user", "content": "..."}
  ],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "check_insulation",
        "description": "Check missing insulation...",
        "parameters": {
          "type": "object",
          "properties": { ... },
          "required": [...]
        }
      }
    }
  ],
  "options": {"temperature": 0.3, "num_ctx": 8192}
}
```

## /api/embed — Embedding Endpoint

### Purpose
Generate vector embeddings for text. Used for RAG knowledge retrieval and semantic skill routing.

### Usage
```json
{
  "model": "nomic-embed-text",
  "input": ["text to embed", "another text"]
}
```

### Response
```json
{
  "embeddings": [[0.123, -0.456, ...], [0.789, -0.012, ...]]
}
```

## Optimization Guidelines for MEP ChatBot

### Token Budget Management
- System prompt (ReAct): ~4000 tokens
- API Cheat Sheet: ~3000 tokens
- Code Examples: ~2000 tokens
- Model context: ~500-1000 tokens
- Few-shot examples: ~300 tokens
- History: variable
- **Total budget: 8192 tokens** — prioritize system prompt > context > recent history

### Best Practices
1. Use `/api/generate` with `format` for pre-processing (intent, entities) — faster, structured
2. Use `/api/chat` with `tools` for the main agent loop — supports multi-turn + function calling
3. Set `temperature: 0.1` for extraction tasks, `0.3` for tool selection
4. Monitor `prompt_eval_count` to detect context overflow
5. Use `keep_alive: "10m"` to avoid model reload between queries
6. Use `think: true` for complex multi-step planning only (adds latency)

### Vietnamese-Specific Notes
- qwen2.5:7b has good Vietnamese support
- For Vietnamese intent extraction, include bilingual examples in the prompt
- Normalize Vietnamese terms before embedding (ống gió → duct) for better similarity scores
