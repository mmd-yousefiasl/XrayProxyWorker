using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Extensions
{
    public static class V2RayConfigExtensions
    {
        public static V2RayConfig? ToV2RayConfig(
            this string uriString)
        {
            if (string.IsNullOrWhiteSpace(uriString))
                return null;

            uriString = uriString.Trim();

            try
            {
                var parts = uriString.Split(
                    "://",
                    2,
                    StringSplitOptions.None);

                if (parts.Length != 2)
                    return null;

                string protocol =
                    parts[0].Trim().ToLowerInvariant();

                return protocol switch
                {
                    "vmess" =>
                        ParseVmess(parts[1], uriString),

                    "ss" =>
                        ParseShadowsocks(uriString),

                    "vless" =>
                        ParseStandard(uriString),

                    "trojan" =>
                        ParseStandard(uriString),

                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        // =========================================================
        // VLESS / Trojan
        // =========================================================

        private static V2RayConfig? ParseStandard(
            string uriString)
        {
            var config = new V2RayConfig
            {
                Url = uriString
            };

            var parts = uriString.Split(
                "://",
                2);

            config.Protocol =
                parts[0].ToLowerInvariant();

            string remaining = parts[1];

            // Fragment
            var fragmentSplit =
                remaining.Split('#', 2);

            string body =
                fragmentSplit[0];

            config.Name =
                fragmentSplit.Length > 1
                    ? Uri.UnescapeDataString(
                        fragmentSplit[1])
                    : "Unnamed";

            // Query
            var querySplit =
                body.Split('?', 2);

            string authority =
                querySplit[0];

            if (querySplit.Length > 1)
            {
                var queryParams =
                    QueryHelpers.ParseQuery(
                        querySplit[1]);

                foreach (var param in queryParams)
                {
                    config.Parameters[param.Key] =
                        param.Value.ToString();
                }
            }

            // user@host:port
            var atIndex =
                authority.LastIndexOf('@');

            if (atIndex <= 0)
                return null;

            config.UserId =
                Uri.UnescapeDataString(
                    authority[..atIndex]);

            string hostPort =
                authority[(atIndex + 1)..];

            if (!TryParseHostPort(
                    hostPort,
                    out string address,
                    out int port))
            {
                return null;
            }

            config.Address = address;
            config.Port = port;

            return config;
        }

        // =========================================================
        // VMess
        // =========================================================

        private static V2RayConfig? ParseVmess(
            string base64Part,
            string originalUri)
        {
            try
            {
                string normalized =
                    base64Part
                        .Trim()
                        .Replace(" ", "+")
                        .Replace("-", "+")
                        .Replace("_", "/");

                while (normalized.Length % 4 != 0)
                {
                    normalized += "=";
                }

                byte[] jsonBytes =
                    Convert.FromBase64String(normalized);

                string json =
                    Encoding.UTF8.GetString(jsonBytes);

                using var doc =
                    JsonDocument.Parse(json);

                var root = doc.RootElement;

                var config = new V2RayConfig
                {
                    Url = originalUri,
                    Protocol = "vmess"
                };

                config.Address =
                    root.TryGetProperty(
                        "add",
                        out var add)
                        ? add.GetString() ?? ""
                        : "";

                if (root.TryGetProperty(
                        "port",
                        out var port))
                {
                    config.Port =
                        port.ValueKind ==
                        JsonValueKind.Number
                            ? port.GetInt32()
                            : int.TryParse(
                                port.GetString(),
                                out int p)
                                ? p
                                : 443;
                }

                config.UserId =
                    root.TryGetProperty(
                        "id",
                        out var id)
                        ? id.GetString() ?? ""
                        : "";

                config.Name =
                    root.TryGetProperty(
                        "ps",
                        out var ps)
                        ? ps.GetString() ?? "VMess"
                        : "VMess";

                CopyJsonString(
                    root,
                    config,
                    "net",
                    "type");

                CopyJsonString(
                    root,
                    config,
                    "tls",
                    "security");

                CopyJsonString(
                    root,
                    config,
                    "sni");

                CopyJsonString(
                    root,
                    config,
                    "host");

                CopyJsonString(
                    root,
                    config,
                    "path");

                CopyJsonString(
                    root,
                    config,
                    "serviceName");

                CopyJsonString(
                    root,
                    config,
                    "alpn");

                // NOTE: In VMess JSON, "type" is the TCP header obfuscation
                // type ("none"/"http"), NOT the network. The network is
                // copied above into Parameters["type"] - so this must be
                // stored under a different key or it overwrites the network.

                CopyJsonString(
                    root,
                    config,
                    "type",
                    "headerType");

                CopyJsonString(
                    root,
                    config,
                    "scy",
                    "encryption");

                CopyJsonString(
                    root,
                    config,
                    "aid");

                return config;
            }
            catch
            {
                return null;
            }
        }

        // =========================================================
        // Shadowsocks
        // =========================================================

        private static V2RayConfig? ParseShadowsocks(
            string uriString)
        {
            try
            {
                var config = new V2RayConfig
                {
                    Url = uriString,
                    Protocol = "ss",
                    Name = "Shadowsocks"
                };

                string remaining =
                    uriString["ss://".Length..];

                // Fragment
                var fragmentSplit =
                    remaining.Split('#', 2);

                string body =
                    fragmentSplit[0];

                if (fragmentSplit.Length > 1)
                {
                    config.Name =
                        Uri.UnescapeDataString(
                            fragmentSplit[1]);
                }

                // -----------------------------------------------------
                // SIP002:
                //
                // ss://BASE64(method:password)@host:port
                // -----------------------------------------------------

                var atIndex =
                    body.LastIndexOf('@');

                if (atIndex > 0)
                {
                    string userInfo =
                        body[..atIndex];

                    string hostPort =
                        body[(atIndex + 1)..];

                    if (!TryParseHostPort(
                            hostPort,
                            out string address,
                            out int port))
                    {
                        return null;
                    }

                    config.Address = address;
                    config.Port = port;

                    string decoded =
                        DecodeBase64Flexible(
                            userInfo);

                    var credentials =
                        decoded.Split(
                            ':',
                            2);

                    if (credentials.Length != 2)
                    {
                        return null;
                    }

                    config.Parameters["method"] =
                        credentials[0];

                    config.UserId =
                        credentials[1];

                    return config;
                }

                // -----------------------------------------------------
                // Legacy:
                //
                // ss://BASE64(method:password@host:port)
                // -----------------------------------------------------

                string decodedLegacy =
                    DecodeBase64Flexible(body);

                if (string.IsNullOrWhiteSpace(
                        decodedLegacy))
                {
                    return null;
                }

                var methodPasswordHost =
                    decodedLegacy.Split(
                        '@',
                        2);

                if (methodPasswordHost.Length != 2)
                {
                    return null;
                }

                var methodPassword =
                    methodPasswordHost[0]
                        .Split(':', 2);

                if (methodPassword.Length != 2)
                {
                    return null;
                }

                config.Parameters["method"] =
                    methodPassword[0];

                config.UserId =
                    methodPassword[1];

                if (!TryParseHostPort(
                        methodPasswordHost[1],
                        out string legacyAddress,
                        out int legacyPort))
                {
                    return null;
                }

                config.Address =
                    legacyAddress;

                config.Port =
                    legacyPort;

                return config;
            }
            catch
            {
                return null;
            }
        }

        // =========================================================
        // Helpers
        // =========================================================

        private static bool TryParseHostPort(
            string hostPort,
            out string address,
            out int port)
        {
            address = string.Empty;
            port = 0;

            hostPort =
                hostPort.Trim();

            // IPv6: [::1]:443
            if (hostPort.StartsWith("["))
            {
                int closeBracket =
                    hostPort.IndexOf(']');

                if (closeBracket < 0)
                    return false;

                address =
                    hostPort[1..closeBracket];

                if (hostPort.Length <=
                    closeBracket + 2)
                {
                    port = 443;
                    return true;
                }

                if (hostPort[closeBracket + 1] != ':')
                    return false;

                return int.TryParse(
                    hostPort[(closeBracket + 2)..],
                    out port);
            }

            int lastColon =
                hostPort.LastIndexOf(':');

            if (lastColon <= 0)
            {
                address = hostPort;
                port = 443;
                return true;
            }

            address =
                hostPort[..lastColon];

            return int.TryParse(
                hostPort[(lastColon + 1)..],
                out port);
        }

        private static string DecodeBase64Flexible(
            string value)
        {
            value =
                Uri.UnescapeDataString(
                    value.Trim());

            value =
                value
                    .Replace("-", "+")
                    .Replace("_", "/");

            while (value.Length % 4 != 0)
            {
                value += "=";
            }

            byte[] bytes =
                Convert.FromBase64String(value);

            return Encoding.UTF8.GetString(bytes);
        }

        private static void CopyJsonString(
            JsonElement root,
            V2RayConfig config,
            string propertyName,
            string? parameterName = null)
        {
            if (!root.TryGetProperty(
                    propertyName,
                    out var element))
            {
                return;
            }

            // Fields like "aid" and "port" frequently appear as JSON
            // numbers in vmess links - stringify them instead of dropping.

            string? value = element.ValueKind switch
            {
                JsonValueKind.String =>
                    element.GetString(),

                JsonValueKind.Number =>
                    element.GetRawText(),

                _ => null
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                config.Parameters[
                    parameterName ?? propertyName] =
                    value;
            }
        }

        // =========================================================
        // Duplicate detection
        // =========================================================

        /// <summary>
        /// Canonical dedupe key for a proxy link: trims whitespace and
        /// drops the '#remark' fragment so the same proxy listed under
        /// different names collapses to one entry.
        /// </summary>
        public static string ToNormalizedProxyKey(
            this string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            url = url.Trim();

            int hashIndex = url.IndexOf('#');

            if (hashIndex >= 0)
            {
                url = url[..hashIndex];
            }

            return url.ToLowerInvariant();
        }

        // =========================================================
        // GitHub raw URL
        // =========================================================

        private static readonly Regex GitHubFileRegex =
            new(
                @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/blob/(?<branch>[^/]+)/(?<path>.+)$",
                RegexOptions.Compiled |
                RegexOptions.IgnoreCase);

        public static bool TryGetRawContentUrl(
            this string? url,
            out string rawUrl)
        {
            rawUrl = string.Empty;

            if (string.IsNullOrWhiteSpace(url))
                return false;

            url = url.Trim();

            if (Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out var uri) &&
                uri.Scheme is "http" or "https" &&
                uri.Host.Equals(
                    "raw.githubusercontent.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = uri.ToString();
                return true;
            }

            var match =
                GitHubFileRegex.Match(url);

            if (!match.Success)
                return false;

            rawUrl =
                $"https://raw.githubusercontent.com/" +
                $"{match.Groups["owner"].Value}/" +
                $"{match.Groups["repo"].Value}/" +
                $"{match.Groups["branch"].Value}/" +
                $"{match.Groups["path"].Value}";

            return true;
        }
    }
}