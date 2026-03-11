using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Skill that fetches and extracts content from a URL via Ollama Cloud API.
/// Useful for reading technical documentation, product data sheets, or standard references.
/// Requires Ollama account + API key.
/// </summary>
[Skill("web_fetch",
    "Fetch and read the content of a specific URL (technical documentation, product data sheets, " +
    "standard references). Returns the page content in readable format. " +
    "Requires Ollama Cloud API key.")]
[SkillParameter("url", "string", "The URL to fetch content from", isRequired: true)]
public class OllamaWebFetchSkill : ISkill
{
    private readonly HttpClient _httpClient;

    public OllamaWebFetchSkill(string cloudBaseUrl, string apiKey)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(cloudBaseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var url = parameters.GetValueOrDefault("url")?.ToString();
        if (string.IsNullOrWhiteSpace(url))
            return SkillResult.Fail("Parameter 'url' is required.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return SkillResult.Fail("Invalid URL. Must be a valid http or https URL.");

        try
        {
            var payload = JsonSerializer.Serialize(new { url });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/fetch", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var node = JsonNode.Parse(json);

            var pageContent = node?["content"]?.GetValue<string>();
            var title = node?["title"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(pageContent))
                return SkillResult.Ok("URL fetched but no readable content was extracted.");

            const int maxContentLength = 8000;
            if (pageContent.Length > maxContentLength)
                pageContent = pageContent[..maxContentLength] + "\n\n...(content truncated)";

            return SkillResult.Ok(
                $"Fetched content from: {title ?? url}",
                new { title, url, content = pageContent });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return SkillResult.Fail(
                "Web fetch requires Ollama Cloud API key. Configure in Settings.",
                "Unauthorized: check cloudApiKey in settings.");
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Web fetch failed: {ex.Message}");
        }
    }
}
