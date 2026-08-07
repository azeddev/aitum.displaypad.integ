using System.Text.Json.Nodes;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Integrations.Aitum;

public sealed record AitumRule(string Id, string Name);

public sealed record AitumStateVariable(string Id, string Name, int Type, string? Value)
{
    public string TypeName => Type switch
    {
        0 => "Integer",
        1 => "Float",
        2 => "String",
        3 => "Boolean",
        _ => "Unknown",
    };
}

/// <summary>
/// Client for the Aitum Desktop public API (http://localhost:7777 by default).
///
///   GET /aitum/rules/          -> { "status": "OK", "data": { "Rule name": "ruleId" } }
///   GET /aitum/rules/:ruleId   -> triggers the rule
///   GET /aitum/state/          -> { "status": "OK", "data": [ { _id, name, type, value } ] }
///
/// The API is unauthenticated and local-only by design.
/// </summary>
public sealed class AitumClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private AitumSettings _settings = new();

    public bool Enabled => _settings.Enabled;

    public string? LastError { get; private set; }

    public string BaseUrl => $"http://{_settings.Host}:{_settings.Port}";

    public void ApplySettings(AitumSettings settings) => _settings = settings;

    public async Task<IReadOnlyList<AitumRule>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var data = await GetDataAsync("/aitum/rules/", cancellationToken).ConfigureAwait(false);
        if (data is not JsonObject map)
        {
            return Array.Empty<AitumRule>();
        }

        var rules = new List<AitumRule>();
        foreach (var (name, idNode) in map)
        {
            if (idNode?.GetValue<string>() is { } id)
            {
                rules.Add(new AitumRule(id, name));
            }
        }

        return rules.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> TriggerRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return false;
        }

        var data = await GetDataAsync($"/aitum/rules/{Uri.EscapeDataString(ruleId)}", cancellationToken).ConfigureAwait(false);
        return data is not null;
    }

    public async Task<IReadOnlyList<AitumStateVariable>> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var data = await GetDataAsync("/aitum/state/", cancellationToken).ConfigureAwait(false);
        if (data is not JsonArray array)
        {
            return Array.Empty<AitumStateVariable>();
        }

        var result = new List<AitumStateVariable>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var name = item["name"]?.GetValue<string>();
            if (name is null)
            {
                continue;
            }

            result.Add(new AitumStateVariable(
                item["_id"]?.GetValue<string>() ?? name,
                name,
                TryReadInt(item["type"]) ?? -1,
                item["value"]?.ToString()));
        }

        return result;
    }

    public async Task<string?> GetStateValueAsync(string name, CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        return state.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    /// <summary>Returns true when Aitum answers on the configured endpoint.</summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var data = await GetDataAsync("/aitum/rules/", cancellationToken).ConfigureAwait(false);
        return data is not null;
    }

    private async Task<JsonNode?> GetDataAsync(string path, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            LastError = "The Aitum integration is disabled.";
            return null;
        }

        try
        {
            using var response = await _http.GetAsync(BaseUrl + path, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"Aitum returned {(int)response.StatusCode} {response.ReasonPhrase}.";
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (JsonNode.Parse(body) is not JsonObject envelope)
            {
                LastError = "Aitum returned an unexpected payload.";
                return null;
            }

            var status = envelope["status"]?.GetValue<string>();
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                LastError = $"Aitum returned status '{status}'.";
                return null;
            }

            LastError = null;
            return envelope["data"];
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    private static int? TryReadInt(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception)
        {
            return int.TryParse(node.ToString(), out var value) ? value : null;
        }
    }

    public void Dispose() => _http.Dispose();
}
