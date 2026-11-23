using System.Net.Http.Headers;

namespace LocalDictation.Core;

internal static class LocalApiCredential
{
    public static string? Read(Uri endpoint)
    {
        var file = Environment.GetEnvironmentVariable("LOCAL_DICTATION_API_KEY_FILE");
        var binding = Environment.GetEnvironmentVariable("LOCAL_DICTATION_API_KEY_ENDPOINT");
        if (file is null && binding is null) return null;
        if (string.IsNullOrEmpty(file) || !Path.IsPathFullyQualified(file) ||
            !Uri.TryCreate(binding, UriKind.Absolute, out var origin) || !origin.IsLoopback ||
            origin.Scheme != "http" || origin.AbsolutePath != "/" || origin.Query.Length != 0 ||
            origin.Fragment.Length != 0 || origin.UserInfo.Length != 0)
            throw new InvalidOperationException("Configure the application key file and its loopback endpoint together.");
        if (!SameOrigin(endpoint, origin)) return null;
        if (new FileInfo(file).Length > 256) throw new InvalidDataException("Invalid application key file.");
        return Validate(File.ReadAllText(file).Trim());
    }

    internal static string Validate(string key)
    {
        if (key.Length is < 32 or > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
            throw new InvalidDataException("Invalid application key file.");
        return key;
    }

    static bool SameOrigin(Uri first, Uri second) =>
        first.Scheme == second.Scheme && first.Host == second.Host && first.Port == second.Port;

    internal sealed class Handler(Uri origin, string key, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri is not { IsAbsoluteUri: true } uri || !SameOrigin(uri, origin))
                throw new InvalidOperationException("The application key is bound to its configured loopback endpoint.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return base.SendAsync(request, token);
        }
    }
}
