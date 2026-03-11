using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Skill that performs web search via Ollama Cloud API.
/// Requires Ollama account + API key configured in OllamaOptions.
/// Useful for looking up current MEP standards, product specs, or code references online.
/// </summary>
[Skill("web_search",
    "Search the web for current MEP standards, product specifications, code references, " +
    "or technical information not available in the local knowledge base. " +
    "Requires Ollama Cloud API key.")]
[SkillParameter("query", "string", "The search query to look up on the web", isRequired: true)]
[SkillParameter("max_results", "integer", "Maximum results to return (1-10, default: 5)", isRequired: false)]
public class OllamaWebSearchSkill : ISkill
{
    private readonly HttpClient _httpClient;

    public OllamaWebSearchSkill(string cloudBaseUrl, string apiKey)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(cloudBaseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var query = parameters.GetValueOrDefault("query")?.ToString();
        if (string.IsNullOrWhiteSpace(query))
            return SkillResult.Fail("Parameter 'query' is required.");

        int maxResults = 5;
        if (parameters.GetValueOrDefault("max_results") is int mr) maxResults = mr;
        else if (parameters.GetValueOrDefault("max_results") is string mrs
                 && int.TryParse(mrs, out var mrp)) maxResults = mrp;
        maxResults = Math.Clamp(maxResults, 1, 10);

        try
        {
            var payload = JsonSerializer.Serialize(new { query, max_results = maxResults });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/search", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var node = JsonNode.Parse(json);
            var results = node?["results"]?.AsArray();

            if (results is null or { Count: 0 })
                return SkillResult.Ok("No web results found for the query.");

            var entries = new List<object>();
            foreach (var r in results)
            {
                entries.Add(new
                {
                    title = r?["title"]?.GetValue<string>() ?? "",
                    url = r?["url"]?.GetValue<string>() ?? "",
                    snippet = r?["snippet"]?.GetValue<string>() ?? r?["content"]?.GetValue<string>() ?? ""
                });
            }

            return SkillResult.Ok($"Found {entries.Count} web results.", entries);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return SkillResult.Fail(
                "Web search requires Ollama Cloud API key. Configure in Settings.",
                "Unauthorized: check cloudApiKey in settings.");
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Web search failed: {ex.Message}");
        }
    }
}
