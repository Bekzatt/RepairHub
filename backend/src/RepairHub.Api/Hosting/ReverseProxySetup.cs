using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace RepairHub.Api.Hosting;

/// <summary>
/// Behind the reverse proxy the API sees the proxy's address and plain HTTP. X-Forwarded-For/-Proto restore the
/// client IP (per-IP login rate limit) and the HTTPS scheme (HSTS), but ONLY when the request comes from an
/// explicitly trusted proxy — otherwise any client could spoof its IP or scheme with these headers.
/// <para>
/// Configuration: <c>ReverseProxy:TrustedProxies</c> = list of IP addresses or CIDR networks
/// (env: <c>ReverseProxy__TrustedProxies__0=172.30.99.10</c>). Empty = only loopback is trusted (ASP.NET default).
/// </para>
/// </summary>
public static class ReverseProxySetup
{
    public const string SectionName = "ReverseProxy:TrustedProxies";

    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var trusted = configuration.GetSection(SectionName).Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Exactly one proxy hop (Caddy). Values added by the client before it are ignored.
            options.ForwardLimit = 1;

            foreach (var entry in trusted.Select(e => e.Trim()).Where(e => e.Length > 0))
            {
                if (entry.Contains('/'))
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry));
                else
                    options.KnownProxies.Add(IPAddress.Parse(entry));
            }
        });
        return services;
    }
}
