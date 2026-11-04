using System.Text.Json;

namespace Notification.Tests.Support;

/// <summary>Reads response bodies the way the contract writes them: camelCase names, enums as camelCase strings.</summary>
public static class HttpJson
{
    /// <summary>The body text, deserialized as <typeparamref name="T"/>.</summary>
    public static T Deserialize<T>(string body)
    {
        T? value = JsonSerializer.Deserialize<T>(body, WireJson.Options);
        if (value is null)
        {
            throw new InvalidOperationException($"The response body was empty, expected {typeof(T).Name}.");
        }

        return value;
    }

    /// <summary>The response body, deserialized as <typeparamref name="T"/>.</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return Deserialize<T>(body);
    }

    /// <summary>The response body as a problem.</summary>
    public static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response)
    {
        return await ReadAsync<ProblemBody>(response);
    }
}
