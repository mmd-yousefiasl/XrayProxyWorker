using XrayProxyWorker.Dtos;

namespace XrayProxyWorker.Tools
{
    public static class V2RayConfigExtensions
    {
        public static V2RayConfig? ToV2RayConfig(this string uriString)
        {
            try
            {
                var config = new V2RayConfig();
                config.Url = uriString;

                // 1. Split protocol
                var parts = uriString.Split("://");
                if (parts.Length < 2) return null; // Not a valid URI format

                config.Protocol = parts[0];
                var remaining = parts[1];

                // 2. Separate the fragment (Name)
                var fragmentSplit = remaining.Split('#');
                config.Name = fragmentSplit.Length > 1 ? fragmentSplit[1] : "Unnamed";
                var body = fragmentSplit[0];

                // 3. Separate Query Parameters
                var querySplit = body.Split('?');
                if (querySplit.Length > 1)
                {
                    var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(querySplit[1]);
                    foreach (var param in queryParams)
                        config.Parameters[param.Key] = param.Value.ToString();
                }

                // 4. Safely extract UUID, Address, and Port
                var userAndHost = querySplit[0].Split('@');
                if (userAndHost.Length < 2) return null; // Malformed: missing '@'

                config.UserId = userAndHost[0];

                var hostAndPort = userAndHost[1].Split(':');
                config.Address = hostAndPort[0];
                config.Port = hostAndPort.Length > 1 && int.TryParse(hostAndPort[1], out int p) ? p : 443;

                return config;
            }
            catch
            {
                // Return null if anything goes wrong so the LINQ filter can remove it
                return null;
            }
        }
    }
}
