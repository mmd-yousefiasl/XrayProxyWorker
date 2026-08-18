namespace XrayProxyWorker.Dtos
{
    namespace XrayProxyWorker.Dtos
    {
        public class V2RayConfig
        {
            public string Protocol { get; set; } = string.Empty;

            public string Address { get; set; } = string.Empty;

            public int Port { get; set; }

            public string UserId { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;

            public Dictionary<string, string> Parameters { get; set; }
                = new(StringComparer.OrdinalIgnoreCase);

            // Always preserve the original URI.
            public string Url { get; set; } = string.Empty;
        }
    }
}
