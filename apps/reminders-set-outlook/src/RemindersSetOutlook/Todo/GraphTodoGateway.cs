// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Extensions.Logging;
using RemindersSetOutlook.Auth;

namespace RemindersSetOutlook.Todo;

/// <summary>
/// Microsoft To Do over the Microsoft Graph v1.0 REST API.
/// </summary>
internal sealed partial class GraphTodoGateway : ITodoGateway
{
    /// <summary>
    /// The Graph base address the <see cref="HttpClient"/> is configured with.
    /// </summary>
    public static readonly Uri BaseAddress = new("https://graph.microsoft.com/v1.0/");

    private const int MaxAttempts = 4;

    private readonly HttpClient http;
    private readonly AuthSession auth;
    private readonly ILogger<GraphTodoGateway> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphTodoGateway"/> class.
    /// </summary>
    /// <param name="http">The HTTP client, with <see cref="BaseAddress"/> set.</param>
    /// <param name="auth">The auth session.</param>
    /// <param name="logger">The logger.</param>
    public GraphTodoGateway(HttpClient http, AuthSession auth, ILogger<GraphTodoGateway> logger)
    {
        this.http = http;
        this.auth = auth;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> GetOrCreateListAsync(string displayName, CancellationToken cancellationToken)
    {
        string? url = "me/todo/lists?$select=id,displayName";
        while (url is not null)
        {
            using var page = await this.SendForJsonAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
            foreach (var list in page.RootElement.GetProperty("value").EnumerateArray())
            {
                if (string.Equals(list.GetProperty("displayName").GetString(), displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return list.GetProperty("id").GetString()!;
                }
            }

            url = page.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        this.LogCreatingList(displayName);
        var body = new JsonObject { ["displayName"] = displayName };
        using var created = await this.SendForJsonAsync(HttpMethod.Post, "me/todo/lists", body, cancellationToken).ConfigureAwait(false);
        return created.RootElement.GetProperty("id").GetString()!;
    }

    /// <inheritdoc/>
    public async Task<string> CreateTaskAsync(string listId, TodoTaskDraft draft, CancellationToken cancellationToken)
    {
        var url = $"me/todo/lists/{Uri.EscapeDataString(listId)}/tasks";
        using var created = await this.SendForJsonAsync(HttpMethod.Post, url, TodoTaskPayload.Build(draft), cancellationToken).ConfigureAwait(false);
        return created.RootElement.GetProperty("id").GetString()!;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateTaskAsync(string listId, string taskId, TodoTaskDraft draft, CancellationToken cancellationToken)
    {
        var url = $"me/todo/lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(taskId)}";
        using var response = await this.SendAsync(HttpMethod.Patch, url, TodoTaskPayload.Build(draft), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = content;
        try
        {
            using var error = JsonDocument.Parse(content);
            if (error.RootElement.TryGetProperty("error", out var detail))
            {
                message = $"{detail.GetProperty("code").GetString()}: {detail.GetProperty("message").GetString()}";
            }
        }
        catch (JsonException)
        {
            // Not a Graph error body; report it as it came.
        }

        throw new HttpRequestException(
            $"Microsoft Graph returned {(int)response.StatusCode} {response.ReasonPhrase}: {message}",
            null,
            response.StatusCode);
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        // Retry-After is either a number of seconds or an HTTP date.
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var value = values.FirstOrDefault() ?? string.Empty;
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until))
            {
                var wait = until - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private async Task<JsonDocument> SendForJsonAsync(HttpMethod method, string url, JsonNode? body, CancellationToken cancellationToken)
    {
        using var response = await this.SendAsync(method, url, body, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, JsonNode? body, CancellationToken cancellationToken)
    {
        var credential = await this.auth.GetCredentialAsync(cancellationToken).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(AuthSession.Scopes), cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            var response = await this.http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var transient = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
            if (!transient || attempt == MaxAttempts)
            {
                return response;
            }

            var delay = RetryDelay(response, attempt);
            this.LogRetrying((int)response.StatusCode, delay.TotalSeconds, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(LogLevel.Information, "Creating To Do list {DisplayName}")]
    private partial void LogCreatingList(string displayName);

    [LoggerMessage(LogLevel.Warning, "Microsoft Graph returned {StatusCode}; retrying in {Seconds:F0}s (attempt {Attempt})")]
    private partial void LogRetrying(int statusCode, double seconds, int attempt);
}
