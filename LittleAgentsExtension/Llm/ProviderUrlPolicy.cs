using System;

namespace LittleAgentsExtension.Llm;

internal static class ProviderUrlPolicy
{
    public static bool TryCreateSupportedUri(string baseUrl, out Uri? uri)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out uri)
            && uri.Host.Length > 0
            && (uri.Scheme == Uri.UriSchemeHttps
                || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
    }
}
