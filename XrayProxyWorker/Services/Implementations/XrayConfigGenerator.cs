using System.Text.Json;
using System.Text.Json.Serialization;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class XrayConfigGenerator : IXrayConfigGenerator
    {
        private readonly IProxyConfigValidator _validator;

        public XrayConfigGenerator(
            IProxyConfigValidator validator)
        {
            _validator = validator;
        }

        public List<GeneratedProxy> GenerateMasterConfig(
            List<V2RayConfig> configs,
            string outputPath,
            int basePortPoolStart)
        {
            var inbounds = new List<object>
            {
                new
                {
                    listen = "127.0.0.1",
                    port = 10085,
                    protocol = "dokodemo-door",
                    tag = "api_inbound",
                    settings = new
                    {
                        address = "127.0.0.1"
                    }
                }
            };

            var outbounds = new List<object>();
            var rules = new List<object>();
            var generated = new List<GeneratedProxy>();

            var validConfigs =
                _validator.FilterValidConfigs(configs);

            for (int i = 0; i < validConfigs.Count; i++)
            {
                var proxy = validConfigs[i];

                try
                {
                    string protocol =
                        NormalizeProtocol(proxy.Protocol);

                    int port =
                        basePortPoolStart + generated.Count;

                    string inboundTag =
                        $"inbound_{port}";

                    string outboundTag =
                        $"proxy_{port}";

                    object outboundSettings =
                        BuildOutboundSettings(
                            proxy,
                            protocol);

                    object? streamSettings =
                        BuildStreamSettings(proxy);

                    inbounds.Add(new
                    {
                        listen = "127.0.0.1",
                        port,
                        protocol = "socks",
                        tag = inboundTag,
                        settings = new
                        {
                            auth = "noauth",
                            udp = true
                        }
                    });

                    outbounds.Add(new
                    {
                        protocol,
                        tag = outboundTag,
                        settings = outboundSettings,
                        streamSettings
                    });

                    rules.Add(new
                    {
                        type = "field",
                        inboundTag = new[]
                        {
                            inboundTag
                        },
                        outboundTag
                    });

                    generated.Add(
                        new GeneratedProxy(
                            proxy,
                            port));
                }
                catch (Exception ex)
                {
                }
            }

            outbounds.Add(
                new
                {
                    protocol = "freedom",
                    tag = "direct"
                });

            var masterLayout = new
            {
                api = new
                {
                    tag = "api",
                    services = new[]
                    {
                        "HandlerService",
                        "RoutingService"
                    }
                },

                stats = new { },

                log = new
                {
                    loglevel = "warning"
                },

                inbounds = inbounds.ToArray(),

                outbounds = outbounds.ToArray(),

                routing = new
                {
                    domainStrategy = "AsIs",
                    rules = rules.ToArray()
                }
            };

            var json = JsonSerializer.Serialize(
                masterLayout,
                new JsonSerializerOptions
                {
                    DefaultIgnoreCondition =
                        JsonIgnoreCondition.WhenWritingNull,

                    WriteIndented = false
                });

            File.WriteAllText(
                outputPath,
                json);

            return generated;
        }

        private static string NormalizeProtocol(
            string protocol)
        {
            return protocol
                .Trim()
                .ToLowerInvariant() switch
            {
                "ss" => "shadowsocks",
                "vmess" => "vmess",
                "vless" => "vless",
                "trojan" => "trojan",
                _ => throw new NotSupportedException(
                    $"Unsupported protocol: {protocol}")
            };
        }

        private static object BuildOutboundSettings(
            V2RayConfig proxy,
            string protocol)
        {
            switch (protocol)
            {
                case "vless":
                case "vmess":
                    {
                        var user = new Dictionary<string, object?>
                        {
                            ["id"] = proxy.UserId,
                            ["encryption"] =
                                protocol == "vless"
                                    ? "none"
                                    : GetParameter(
                                        proxy,
                                        "encryption",
                                        "auto")
                        };

                        string? flow =
                            GetParameter(
                                proxy,
                                "flow");

                        if (!string.IsNullOrWhiteSpace(flow))
                            user["flow"] = flow;

                        string? level =
                            GetParameter(
                                proxy,
                                "level");

                        if (!string.IsNullOrWhiteSpace(level) &&
                            int.TryParse(level, out var levelValue))
                        {
                            user["level"] = levelValue;
                        }

                        return new
                        {
                            vnext = new[]
                            {
                            new
                            {
                                address = proxy.Address,
                                port = proxy.Port,
                                users = new[]
                                {
                                    user
                                }
                            }
                        }
                        };
                    }

                case "trojan":
                    {
                        return new
                        {
                            servers = new[]
                            {
                            new
                            {
                                address = proxy.Address,
                                port = proxy.Port,
                                password = proxy.UserId,
                                level = 0
                            }
                        }
                        };
                    }

                case "shadowsocks":
                    {
                        string method =
                            GetParameter(
                                proxy,
                                "method",
                                "aes-256-gcm")!;

                        return new
                        {
                            servers = new[]
                            {
                            new
                            {
                                address = proxy.Address,
                                port = proxy.Port,
                                method,
                                password = proxy.UserId
                            }
                        }
                        };
                    }

                default:
                    throw new NotSupportedException(
                        $"Unsupported outbound protocol: {protocol}");
            }
        }

        private object? BuildStreamSettings(V2RayConfig proxy)
        {
            string security =
                GetParameter(
                    proxy,
                    "security",
                    defaultValue: "none") ?? "none";

            string network =
                GetParameter(
                    proxy,
                    "type",
                    defaultValue: "tcp") ?? "tcp";

            security = security.Trim().ToLowerInvariant();
            network = network.Trim().ToLowerInvariant();

            // ---------------------------------------------------------
            // Trojan always uses TLS unless explicitly handled otherwise.
            // ---------------------------------------------------------

            if (proxy.Protocol.Equals(
                "trojan",
                StringComparison.OrdinalIgnoreCase))
            {
                security = "tls";
            }

            // ---------------------------------------------------------
            // Validate transport
            // ---------------------------------------------------------

            var allowedNetworks = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
    {
        "tcp",
        "ws",
        "grpc",
        "http",
        "httpupgrade",
        "splithttp",
        "xhttp",
        "quic"
    };

            if (!allowedNetworks.Contains(network))
            {
                throw new InvalidOperationException(
                    $"Unsupported transport type '{network}' for config: {proxy.Url}");
            }

            // ---------------------------------------------------------
            // Validate security
            // ---------------------------------------------------------

            var allowedSecurity = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
    {
        "none",
        "tls",
        "reality"
    };

            if (!allowedSecurity.Contains(security))
            {
                throw new InvalidOperationException(
                    $"Unsupported security '{security}' for config: {proxy.Url}");
            }

            // Simple TCP + no security needs no stream settings.
            if (security == "none" &&
                network == "tcp")
            {
                return null;
            }

            var streamSettings =
                new Dictionary<string, object?>
                {
                    ["network"] = network,
                    ["security"] = security
                };

            string? sni =
                GetParameter(
                    proxy,
                    "sni",
                    "serverName");

            string? fingerprint =
                GetParameter(
                    proxy,
                    "fp",
                    "fingerprint");

            string? alpn =
                GetParameter(
                    proxy,
                    "alpn");

            // ---------------------------------------------------------
            // TLS
            // ---------------------------------------------------------

            if (security == "tls")
            {
                var tlsSettings = new Dictionary<string, object?>();

                if (!string.IsNullOrWhiteSpace(sni))
                {
                    tlsSettings["serverName"] = sni;
                }

                if (!string.IsNullOrWhiteSpace(fingerprint))
                {
                    // This belongs in TLS settings for Xray's client fingerprint.
                    tlsSettings["fingerprint"] = fingerprint;
                }

                if (!string.IsNullOrWhiteSpace(alpn))
                {
                    tlsSettings["alpn"] =
                        alpn.Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries);
                }

                // IMPORTANT:
                // Do NOT add allowInsecure.
                // Xray 26.x removed this field.

                streamSettings["tlsSettings"] = tlsSettings;
            }

            // ---------------------------------------------------------
            // REALITY
            // ---------------------------------------------------------

            if (security == "reality")
            {
                string? publicKey =
                    GetParameter(
                        proxy,
                        "pbk",
                        "publicKey");

                string? shortId =
                    GetParameter(
                        proxy,
                        "sid",
                        "shortId");

                if (string.IsNullOrWhiteSpace(publicKey))
                {
                    throw new InvalidOperationException(
                        $"Reality config has no public key: {proxy.Url}");
                }

                var realitySettings =
                    new Dictionary<string, object?>
                    {
                        ["serverName"] = sni,
                        ["publicKey"] = publicKey,
                        ["shortId"] = shortId,
                        ["fingerprint"] =
                            string.IsNullOrWhiteSpace(fingerprint)
                                ? "chrome"
                                : fingerprint
                    };

                string? spiderX =
                    GetParameter(
                        proxy,
                        "spx",
                        "spiderX");

                if (!string.IsNullOrWhiteSpace(spiderX))
                {
                    realitySettings["spiderX"] =
                        spiderX;
                }

                streamSettings["realitySettings"] =
                    realitySettings;
            }

            // ---------------------------------------------------------
            // WebSocket
            // ---------------------------------------------------------

            if (network == "ws")
            {
                string? path =
                    GetParameter(
                        proxy,
                        "path");

                string? host =
                    GetParameter(
                        proxy,
                        "host");

                var headers =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(host))
                {
                    headers["Host"] = host;
                }
                else if (!string.IsNullOrWhiteSpace(sni))
                {
                    headers["Host"] = sni!;
                }

                streamSettings["wsSettings"] =
                    new
                    {
                        path = string.IsNullOrWhiteSpace(path)
                            ? "/"
                            : path,

                        headers
                    };
            }

            // ---------------------------------------------------------
            // gRPC
            // ---------------------------------------------------------

            if (network == "grpc")
            {
                string? serviceName =
                    GetParameter(
                        proxy,
                        "serviceName");

                streamSettings["grpcSettings"] =
                    new
                    {
                        serviceName =
                            serviceName ?? string.Empty
                    };
            }

            // ---------------------------------------------------------
            // HTTP
            // ---------------------------------------------------------

            if (network == "http")
            {
                string? host =
                    GetParameter(
                        proxy,
                        "host");

                string? path =
                    GetParameter(
                        proxy,
                        "path");

                streamSettings["httpSettings"] =
                    new
                    {
                        host =
                            string.IsNullOrWhiteSpace(host)
                                ? Array.Empty<string>()
                                : new[] { host },

                        path =
                            string.IsNullOrWhiteSpace(path)
                                ? "/"
                                : path
                    };
            }

            return streamSettings;
        }

        private static string? GetParameter(
            V2RayConfig proxy,
            string key,
            string? fallbackKey = null,
            string? defaultValue = null)
        {
            if (proxy.Parameters.TryGetValue(
                    key,
                    out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (!string.IsNullOrWhiteSpace(fallbackKey) &&
                proxy.Parameters.TryGetValue(
                    fallbackKey,
                    out var fallback) &&
                !string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            return defaultValue;
        }
    }
}