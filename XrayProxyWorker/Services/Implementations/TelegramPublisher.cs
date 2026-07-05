using System.Text;
using System.Text.Json;
using XrayProxyWorker.Dtos;
using XrayProxyWorker.Services.Interfaces;
namespace XrayProxyWorker.Services.Implementations
{
    public class TelegramPublisher : ITelegramPublisher
    {
        #region Fields
        private readonly ILogger<TelegramPublisher> _logger;
        private readonly HttpClient _httpClient;
        private const string ChannelId = "@connect_iran";
        #endregion

        #region Ctor
        public TelegramPublisher(ILogger<TelegramPublisher> logger,
            HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }
        #endregion

        public async Task BroadcastTopProxiesAsync(List<(V2RayConfig Config, long Latency)> topTen, CancellationToken ct)
        {
            if (topTen == null || !topTen.Any())
            {
                _logger.LogWarning("Telegram broadcast skipped: Top 10 proxy list is empty.");
                return;
            }

            _logger.LogInformation("Formatting safe full-width template for Telegram channel...");

            var configListSb = new System.Text.StringBuilder();
            var itemsToProcess = topTen.Take(10).ToList();

            foreach (var item in itemsToProcess)
            {
                // به‌روزرسانی نام کانفیگ به تگ کانال روی همان لیتک اصلی
                string rawShareLink = UpdateConfigName(item.Config.Url, "telegram%40connect_iran");

                if (!string.IsNullOrWhiteSpace(rawShareLink))
                {
                    string safeHtmlLink = System.Net.WebUtility.HtmlEncode(rawShareLink.Trim());
                    configListSb.AppendLine(safeHtmlLink);
                }
            }

            // قرار دادن کانفیگ‌های ایمن شده داخل تگ پیش‌فرض تمام‌عرض برای کپی یکجای بی‌نقص
            string unifiedCopyAllBlock = $"<pre>{configListSb.ToString().TrimEnd()}</pre>";

            string messageTemplate =
                    $@"📡 کانفیگ‌های پرسرعت و تست‌شده برای اتصال پایدار به اینترنت آزاد!
🌍 مناسب تلگرام، واتساپ، اینستا و وبگردی بدون محدودیت

🎯 برای استفاده راحت‌تر، اپ پیشنهادی ما: v2rayNG روی اندروید و v2box روی ios

🔹 لیست کانفیگ‌ها:
{unifiedCopyAllBlock}

💡 برای دریافت کانفیگ‌های بیشتر و آپدیت‌های روزانه، حتماً عضو کانال ما بشید:
✅کانفیگ‌های بیشتر: <a href=""t.me/connect_iran"">connect_iran</a>

📢 تست کن، استفاده کن، لذت ببر! اینترنت آزاد حق توئه!

🤖 <i>این پیام به صورت خودکار توسط ربات ایجاد شده است.</i>";


            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new
                {
                    chatId = ChannelId,
                    messageText = messageTemplate
                }), Encoding.UTF8, "application/json");

                await _httpClient.PostAsync("https://flat-sunset-4db4.mmdyousefi95.workers.dev/send", content);

                _logger.LogInformation("Successfully broadcasted all 10 verified configs safely.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send update HTML message payload to Telegram API endpoint.");
            }
        }

        private string UpdateConfigName(string url, string newName)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            // پیدا کردن محل هشتگ در لینک کانفیگ
            int hashIndex = url.IndexOf('#');

            if (hashIndex >= 0)
            {
                // جدا کردن بخش قبل از هشتگ و چسباندن نام جدید
                return url.Substring(0, hashIndex) + "#" + newName;
            }

            // اگر از قبل هشتگ نداشت، هشتگ و نام جدید را به انتها اضافه کن
            return url.Trim() + "#" + newName;
        }
    }
}