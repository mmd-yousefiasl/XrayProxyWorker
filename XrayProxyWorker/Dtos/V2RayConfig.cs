namespace XrayProxyWorker.Dtos
{
    public class V2RayConfig
    {
        public string Protocol { get; set; } = string.Empty; // e.g., "vless"
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public string UserId { get; set; } = string.Empty; // UUID
        public string Name { get; set; } = string.Empty; // The text after #
        public Dictionary<string, string> Parameters { get; set; } = new();
        public string Url { get; set; }
    }
}
