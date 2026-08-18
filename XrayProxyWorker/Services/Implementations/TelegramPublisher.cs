using System.Net;
using System.Text;
using System.Text.Json;
using XrayProxyWorker.Dtos.XrayProxyWorker.Dtos;
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
                // به‌روزرسانی نام کانفیگ به تگ کانال روی همان لینک اصلی
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

🔹 لیست کانفیگ‌ها:{unifiedCopyAllBlock}

💡 برای دریافت کانفیگ‌های بیشتر و آپدیت‌های روزانه، حتماً عضو کانال ما بشید:
✅کانفیگ‌های بیشتر: <a href=""t.me/connect_iran"">connect_iran</a>

📢 تست کن، استفاده کن، لذت ببر! اینترنت آزاد حق توئه!

🤖 <i>این پیام به صورت خودکار توسط ربات ایجاد شده است.</i>";

            string jsonPayload = JsonSerializer.Serialize(new
            {
                chatId = ChannelId,
                messageText = messageTemplate
            });

            int maxRetries = 3;
            int delayBetweenRetriesMs = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                try
                {
                    _logger.LogInformation("Sending HTTP request POST to Worker (Attempt {Attempt}/{Max})", attempt, maxRetries);

                    // ساختن دقیق درخواست با ویژگی‌های شبیه‌ساز مرورگر و اجبار استفاده از HTTP/2
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://flat-sunset-4db4.mmdyousefi95.workers.dev/send")
                    {
                        Version = HttpVersion.Version20,
                        VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };

                    // اعمال هدرهای شناسایی مرورگر کروم جهت گمراه‌سازی ماژول‌های شنود شبکه (DPI)
                    request.Headers.Clear();
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                    request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
                    request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                    request.Headers.Add("Sec-Fetch-Site", "cross-site");
                    request.Headers.Add("Sec-Fetch-Mode", "cors");
                    request.Headers.Add("Sec-Fetch-Dest", "empty");

                    // افزودن هدرهای مدرن کمکی مرورگر کروم (User-Agent Client Hints)
                    request.Headers.Add("sec-ch-ua", "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"122\"");
                    request.Headers.Add("sec-ch-ua-mobile", "?0");
                    request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");

                    var response = await _httpClient.SendAsync(request, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Successfully broadcasted all verified configs safely on attempt {Attempt}.", attempt);
                        return;
                    }

                    string errorResponseJson = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("Worker responded with error code {StatusCode}: {ErrorDetails}", response.StatusCode, errorResponseJson);

                    if ((int)response.StatusCode == 422 || response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex is HttpRequestException || ex is TimeoutException)
                {
                    _logger.LogWarning("Attempt {Attempt} timed out or met network turbulence. Exception: {Message}", attempt, ex.Message);
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(delayBetweenRetriesMs, ct);
                }
            }

            _logger.LogError("All {MaxRetries} attempts to broadcast configurations were exhausted due to network routing limits.", maxRetries);
        }

        private string UpdateConfigName(string url, string newName)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            int hashIndex = url.IndexOf('#');

            if (hashIndex >= 0)
            {
                return url.Substring(0, hashIndex) + "#" + newName;
            }

            return url.Trim() + "#" + newName;
        }
    }
}