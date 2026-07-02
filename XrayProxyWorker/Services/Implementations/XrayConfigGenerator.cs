using System.Text.Json;
using System.Text.Json.Serialization;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;

namespace XrayProxyWorker.Services.Implementations
{
    public class XrayConfigGenerator : IXrayConfigGenerator
    {
        private readonly IProxyConfigValidator _validator;

        public XrayConfigGenerator(IProxyConfigValidator validator) => _validator = validator;

        public int GenerateMasterConfig(List<V2RayConfig> configs, string outputPath, int basePortPoolStart)
        {
            var inbounds = new List<object>
        {
            new { listen = "127.0.0.1", port = 10085, protocol = "dokodemo-door", tag = "api_inbound", settings = new { address = "127.0.0.1" } }
        };

            var outbounds = new List<object>();
            var rules = new List<object>();
            var validConfigs = _validator.FilterValidConfigs(configs);
            int successfullyGenerated = 0;

            for (int i = 0; i < validConfigs.Count; i++)
            {
                try
                {
                    var proxy = validConfigs[i];
                    string protocolLower = proxy.Protocol?.ToLower() == "ss" ? "shadowsocks" : proxy.Protocol?.ToLower() ?? "vless";

                    object outboundSettings = BuildOutboundSettings(proxy, protocolLower);
                    object? streamSettings = BuildStreamSettings(proxy);

                    if (streamSettings is Dictionary<string, object> dict && dict.TryGetValue("security", out var sec) && sec.ToString() == "reality")
                    {
                        proxy.Parameters.TryGetValue("pbk", out var publicKey);
                        if (string.IsNullOrWhiteSpace(publicKey)) continue;
                    }

                    int port = basePortPoolStart + successfullyGenerated;
                    string inboundTag = $"inbound_{port}";
                    string outboundTag = $"proxy_{port}";

                    inbounds.Add(new { listen = "127.0.0.1", port = port, protocol = "socks", tag = inboundTag, settings = new { auth = "noauth", udp = true } });
                    outbounds.Add(new { protocol = protocolLower, tag = outboundTag, settings = outboundSettings, streamSettings = streamSettings });
                    rules.Add(new { type = "field", inboundTag = new[] { inboundTag }, outboundTag = outboundTag });

                    successfullyGenerated++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SKIPPED NODE] Failed to parse node configuration layout matrix offset {i}: {ex.Message}");
                }
            }

            outbounds.Add(new { protocol = "freedom", tag = "direct" });

            var masterLayout = new
            {
                api = new { tag = "api", services = new[] { "HandlerService", "RoutingService" } },
                stats = new { },
                log = new { loglevel = "none" },
                inbounds = inbounds.ToArray(),
                outbounds = outbounds.ToArray(),
                routing = new { domainStrategy = "AsIs", rules = rules.ToArray() }
            };

            File.WriteAllText(outputPath, JsonSerializer.Serialize(masterLayout, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            }));

            return successfullyGenerated;
        }

        private object BuildOutboundSettings(V2RayConfig proxy, string protocol)
        {
            if (protocol == "vless" || protocol == "vmess")
            {
                return new
                {
                    vnext = new[] {
                    new {
                        address = proxy.Address,
                        port = proxy.Port,
                        users = new[] {
                            new { id = proxy.UserId, encryption = protocol == "vless" ? "none" : "auto", flow = proxy.Parameters.TryGetValue("flow", out var flow) ? flow : null }
                        }
                    }
                }
                };
            }

            return new
            {
                servers = new[] {
                new { address = proxy.Address, port = proxy.Port, password = proxy.UserId, method = proxy.Parameters.TryGetValue("method", out var method) ? method : (protocol == "shadowsocks" ? "aes-256-gcm" : null) }
            }
            };
        }

        private object? BuildStreamSettings(V2RayConfig proxy)
        {
            if (!proxy.Parameters.TryGetValue("security", out var security) || string.IsNullOrWhiteSpace(security) || security.ToLower() == "none") return null;

            proxy.Parameters.TryGetValue("type", out var network);
            if (string.IsNullOrWhiteSpace(network)) network = "tcp";

            var streamSettings = new Dictionary<string, object>
        {
            { "network", network.ToLower() },
            { "security", security.ToLower() }
        };

            string securityLower = security.ToLower();
            proxy.Parameters.TryGetValue("sni", out var sni);

            if (securityLower == "tls")
            {
                streamSettings["tlsSettings"] = new { serverName = sni };
            }
            else if (securityLower == "reality")
            {
                proxy.Parameters.TryGetValue("pbk", out var publicKey);
                proxy.Parameters.TryGetValue("sid", out var shortId);
                proxy.Parameters.TryGetValue("fp", out var fingerprint);

                streamSettings["realitySettings"] = new { serverName = sni, publicKey = publicKey, shortId = shortId, fingerprint = !string.IsNullOrWhiteSpace(fingerprint) ? fingerprint : "chrome" };
            }

            proxy.Parameters.TryGetValue("path", out var path);
            proxy.Parameters.TryGetValue("serviceName", out var serviceName);
            string targetPath = !string.IsNullOrWhiteSpace(path) ? path : serviceName;

            if (network.ToLower() == "ws")
            {
                streamSettings["wsSettings"] = new { path = targetPath, headers = !string.IsNullOrWhiteSpace(sni) ? new { Host = sni } : null };
            }
            else if (network.ToLower() == "grpc")
            {
                streamSettings["grpcSettings"] = new { serviceName = targetPath };
            }

            return streamSettings;
        }
    }
}
