using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RevitChatBot.Core.Models;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// HTTP client for the Ollama REST API.
/// Reference: https://github.com/ollama/ollama/blob/main/docs/api.md
/// </summary>
public class OllamaService : IOllamaService, IDisposable
{
    private readonly HttpClient _httpClient;
    private OllamaOptions _options;

    public OllamaService(OllamaOptions? options = null)
    {
        _options = options ?? new OllamaOptions();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.BaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<ChatMessage> ChatAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildChatPayload(messages, tools, stream: false);
        var response = await PostAsync("/api/chat", payload, cancellationToken);
        return ParseChatResponse(response);
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = BuildChatPayload(messages, tools: null, stream: true);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var node = JsonNode.Parse(line);
            var content = node?["message"]?["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(content))
                yield return content;

            if (node?["done"]?.GetValue<bool>() == true)
                yield break;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<OllamaModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(json);

        var models = new List<OllamaModel>();
        var modelsArray = node?["models"]?.AsArray();
        if (modelsArray is null) return models;

        foreach (var m in modelsArray)
        {
            models.Add(new OllamaModel
            {
                Name = m?["name"]?.GetValue<string>() ?? "",
                Size = m?["size"]?.GetValue<long>() ?? 0,
                ParameterSize = m?["details"]?["parameter_size"]?.GetValue<string>() ?? "",
                QuantizationLevel = m?["details"]?["quantization_level"]?.GetValue<string>() ?? ""
            });
        }

        return models;
    }

    public async Task<string> GenerateAsync(
        string prompt,
        string? formatJson = null,
        double? temperature = null,
        int? numCtx = null,
        CancellationToken cancellationToken = default)
    {
        var root = new JsonObject
        {
            ["model"] = _options.Model,
            ["prompt"] = prompt,
            ["stream"] = false,
            ["options"] = new JsonObject
            {
                ["temperature"] = temperature ?? 0.1,
                ["num_ctx"] = numCtx ?? 2048
            }
        };

        if (formatJson != null)
        {
            var formatNode = JsonNode.Parse(formatJson);
            if (formatNode != null)
                root["format"] = formatNode;
        }

        var content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/generate", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(json);
        return node?["response"]?.GetValue<string>() ?? "";
    }

    public void UpdateOptions(Action<OllamaOptions> configure)
    {
        configure(_options);
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public OllamaOptions GetCurrentOptions() => _options;

    private string BuildChatPayload(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools,
        bool stream)
    {
        var root = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = stream,
            ["options"] = new JsonObject
            {
                ["temperature"] = _options.Temperature
            }
        };

        if (_options.NumCtx.HasValue)
            root["options"]!.AsObject()["num_ctx"] = _options.NumCtx.Value;

        if (_options.KeepAlive is not null)
            root["keep_alive"] = _options.KeepAlive;

        if (_options.Think.HasValue)
            root["think"] = _options.Think.Value;

        root["messages"] = BuildMessagesArray(messages);

        if (tools is { Count: > 0 })
            root["tools"] = BuildToolsArray(tools);

        return root.ToJsonString();
    }

    private static JsonArray BuildMessagesArray(List<ChatMessage> messages)
    {
        var array = new JsonArray();

        foreach (var msg in messages)
        {
            var msgObj = new JsonObject
            {
                ["role"] = msg.Role.ToString().ToLowerInvariant(),
                ["content"] = msg.Content
            };

            if (msg.ToolCalls is { Count: > 0 })
            {
                var toolCallsArray = new JsonArray();
                foreach (var tc in msg.ToolCalls)
                {
                    var argsObj = new JsonObject();
                    foreach (var (key, val) in tc.Arguments)
                    {
                        argsObj[key] = val switch
                        {
                            string s => JsonValue.Create(s),
                            int i => JsonValue.Create(i),
                            long l => JsonValue.Create(l),
                            double d => JsonValue.Create(d),
                            bool b => JsonValue.Create(b),
                            _ => val is not null ? JsonValue.Create(val.ToString()!) : null
                        };
                    }

                    toolCallsArray.Add(new JsonObject
                    {
                        ["function"] = new JsonObject
                        {
                            ["name"] = tc.FunctionName,
                            ["arguments"] = argsObj
                        }
                    });
                }
                msgObj["tool_calls"] = toolCallsArray;
            }

            if (msg.Role == ChatRole.Tool && msg.ToolName is not null)
                msgObj["tool_name"] = msg.ToolName;

            array.Add(msgObj);
        }

        return array;
    }

    private static JsonArray BuildToolsArray(List<ToolDefinition> tools)
    {
        var array = new JsonArray();

        foreach (var tool in tools)
        {
            var properties = new JsonObject();
            foreach (var (name, param) in tool.Parameters)
            {
                var paramObj = new JsonObject
                {
                    ["type"] = param.Type,
                    ["description"] = param.Description
                };
                if (param.Enum is { Count: > 0 })
                {
                    var enumArray = new JsonArray();
                    foreach (var e in param.Enum) enumArray.Add(e);
                    paramObj["enum"] = enumArray;
                }
                properties[name] = paramObj;
            }

            var requiredArray = new JsonArray();
            foreach (var r in tool.Required) requiredArray.Add(r);

            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = requiredArray
                    }
                }
            });
        }

        return array;
    }

    private async Task<JsonNode> PostAsync(string endpoint, string payload, CancellationToken ct)
    {
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, content, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(json)!;
    }

    private static ChatMessage ParseChatResponse(JsonNode node)
    {
        var message = node["message"];
        var content = message?["content"]?.GetValue<string>() ?? string.Empty;
        var result = ChatMessage.FromAssistant(content);

        var toolCalls = message?["tool_calls"]?.AsArray();
        if (toolCalls is { Count: > 0 })
        {
            result.ToolCalls = new List<ToolCall>();
            foreach (var tc in toolCalls)
            {
                var fn = tc?["function"];
                if (fn is null) continue;

                var args = new Dictionary<string, object?>();
                var argsNode = fn["arguments"];
                if (argsNode is JsonObject argsObj)
                {
                    foreach (var (key, val) in argsObj)
                    {
                        args[key] = val switch
                        {
                            JsonValue jv when jv.TryGetValue<string>(out var s) => s,
                            JsonValue jv when jv.TryGetValue<int>(out var i) => i,
                            JsonValue jv when jv.TryGetValue<double>(out var d) => d,
                            JsonValue jv when jv.TryGetValue<bool>(out var b) => b,
                            _ => val?.ToString()
                        };
                    }
                }

                result.ToolCalls.Add(new ToolCall
                {
                    FunctionName = fn["name"]?.GetValue<string>() ?? string.Empty,
                    Arguments = args
                });
            }
        }

        return result;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
