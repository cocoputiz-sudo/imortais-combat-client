using System;

namespace Imortais.LinuxClient.Services;

public static class ProductionEndpoint
{
    public const string BaseUrl = "https://imortais.macknacloud.com.br";

    // Only the former production origin. Preserve custom servers and credentials.
    public static string Migrate(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("cta-imortais.up.railway.app", StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort && uri.UserInfo.Length == 0
            && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0)
            return BaseUrl;
        return value;
    }
}
