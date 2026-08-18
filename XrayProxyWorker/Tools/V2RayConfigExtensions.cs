using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Tools
{
    public static class V2RayConfigExtensions
    {
        public static V2RayConfig? ToV2RayConfig(this string uriString)
        {
            if (string.IsNullOrWhiteSpace(uriString)) return null;

            try
            {
                var config = new V2RayConfig { Url = uriString };

                // 1. جدا کردن پروتکل
                var parts = uriString.Split("://", 2);
                if (parts.Length < 2) return null;

                config.Protocol = parts[0].ToLowerInvariant();
                var remaining = parts[1];

                // --- مدیریت اختصاصی پروتکل VMESS ---
                if (config.Protocol == "vmess")
                {
                    return ParseVmess(remaining, config);
                }

                // 2. جدا کردن فرگمنت (Name) قبل از هرگونه پردازش آدرس و پورت
                var fragmentSplit = remaining.Split('#', 2);
                config.Name = fragmentSplit.Length > 1
                    ? System.Net.WebUtility.UrlDecode(fragmentSplit[1])
                    : "Unnamed";

                var body = fragmentSplit[0];

                // 3. جدا کردن پارامترهای کوئری
                var querySplit = body.Split('?', 2);
                if (querySplit.Length > 1)
                {
                    var queryParams = QueryHelpers.ParseQuery(querySplit[1]);
                    foreach (var param in queryParams)
                        config.Parameters[param.Key] = param.Value.ToString();
                }

                // 4. استخراج لایه کاربری و هاست/پورت
                var userAndHost = querySplit[0].Split('@', 2);
                if (userAndHost.Length < 2) return null; // فاقد کاراکتر @

                config.UserId = userAndHost[0];

                // جدا کردن پورت از آی‌پدریس/دامنه
                var lastColonIndex = userAndHost[1].LastIndexOf(':');
                if (lastColonIndex >= 0)
                {
                    config.Address = userAndHost[1].Substring(0, lastColonIndex);
                    var portStr = userAndHost[1].Substring(lastColonIndex + 1);
                    config.Port = int.TryParse(portStr, out int p) ? p : 443;
                }
                else
                {
                    config.Address = userAndHost[1];
                    config.Port = 443; // پورت پیش‌فرض
                }

                return config;
            }
            catch
            {
                return null;
            }
        }

        private static V2RayConfig? ParseVmess(string base64Part, V2RayConfig config)
        {
            try
            {
                // پاکسازی ساختار بیس۶۴ برای جلوگیری از خطاهای پدینگ
                string normalizedBase64 = base64Part.Trim().Replace(" ", "+");
                int mod4 = normalizedBase64.Length % 4;
                if (mod4 > 0) normalizedBase64 += new string('=', 4 - mod4);

                var jsonBytes = Convert.FromBase64String(normalizedBase64);
                var jsonString = Encoding.UTF8.GetString(jsonBytes);

                // پارس کردن جی‌سون استاندارد نرم‌افزارهای v2ray
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                config.Address = root.GetProperty("add").GetString() ?? "";
                config.Port = root.GetProperty("port").ValueKind == JsonValueKind.Number
                    ? root.GetProperty("port").GetInt32()
                    : int.Parse(root.GetProperty("port").GetString() ?? "443");

                config.UserId = root.GetProperty("id").GetString() ?? "";
                config.Name = root.TryGetProperty("ps", out var psElement) ? psElement.GetString() ?? "VMess" : "VMess";

                // پر کردن پارامترها برای سازگاری با بقیه ساختار سیستم شما
                if (root.TryGetProperty("net", out var net)) config.Parameters["type"] = net.GetString() ?? "";
                if (root.TryGetProperty("tls", out var tls)) config.Parameters["security"] = tls.GetString() ?? "";
                if (root.TryGetProperty("sni", out var sni)) config.Parameters["sni"] = sni.GetString() ?? "";
                if (root.TryGetProperty("path", out var path)) config.Parameters["path"] = path.GetString() ?? "";

                return config;
            }
            catch
            {
                return null; // در صورت خرابی دیتای بیس۶۴
            }
        }

        private static readonly Regex GitHubFileRegex = new(
            @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/blob/(?<branch>[^/]+)/(?<path>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool TryGetRawContentUrl(
            this string? url,
            out string rawUrl)
        {
            rawUrl = string.Empty;

            if (string.IsNullOrWhiteSpace(url))
                return false;

            url = url.Trim();

            // Already a raw GitHub URL
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https" &&
                uri.Host.Equals(
                    "raw.githubusercontent.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = uri.ToString();
                return true;
            }

            // Normal GitHub file URL
            var match = GitHubFileRegex.Match(url);

            if (!match.Success)
                return false;

            var owner = match.Groups["owner"].Value;
            var repo = match.Groups["repo"].Value;
            var branch = match.Groups["branch"].Value;
            var path = match.Groups["path"].Value;

            rawUrl =
                $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";

            return true;
        }
    }
}