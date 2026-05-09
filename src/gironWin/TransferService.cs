using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace gironWin
{
    public class TransferService
    {
        private readonly AiSiteAdapterResolver _adapterResolver;
        private readonly ObservableCollection<TransferRecord> _records;

        public event EventHandler<string>? DebugLog;

        public TransferService(
            AiSiteAdapterResolver adapterResolver,
            ObservableCollection<TransferRecord> records)
        {
            _adapterResolver = adapterResolver;
            _records = records;
        }

        private void Log(string message)
        {
            DebugLog?.Invoke(this, message);
        }

        public async Task<TransferResult> TransferAsync(
            WebView2 sourceWebView,
            WebView2 targetWebView,
            string sourceUrl,
            string targetUrl,
            bool submit,
            bool appendBridge,
            string? manualText = null)
        {
            var sourceAdapter = _adapterResolver.Resolve(sourceUrl);
            var targetAdapter = _adapterResolver.Resolve(targetUrl);

            if (sourceAdapter == null)
                return TransferResult.Fail("送信元サイトのアダプタが見つかりません。");

            if (targetAdapter == null)
                return TransferResult.Fail("送信先サイトのアダプタが見つかりません。");

            Log($"[Transfer] Source={sourceAdapter.SiteName}, Target={targetAdapter.SiteName}, Submit={submit}");

            string text = manualText ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                text = await sourceAdapter.GetSelectedTextAsync(sourceWebView);
                Log($"[Transfer] SelectedText.Length={text?.Length ?? 0}");
            }
            else
            {
                Log($"[Transfer] ManualText.Length={text.Length}");
            }

            if (string.IsNullOrWhiteSpace(text))
                return TransferResult.Fail("転送するテキストが空です。");

            if (appendBridge)
                text += "\n\nこの意見についてどう考えますか？";

            Log($"[Transfer] FinalText.Length={text.Length}");
            Log($"[Transfer] FinalText.Preview={(text.Length > 120 ? text[..120] : text)}");

            bool inputOk = await targetAdapter.SetInputAsync(targetWebView, text);
            Log($"[Transfer] SetInputAsync={inputOk}");

            if (!inputOk)
                return TransferResult.Fail($"入力欄への設定に失敗しました。Target={targetAdapter.SiteName}");

            bool sendOk = true;
            if (submit)
            {
                await Task.Delay(800);
                sendOk = await targetAdapter.SendAsync(targetWebView);
                Log($"[Transfer] SendAsync={sendOk}");

                if (!sendOk)
                    return TransferResult.Fail($"送信操作に失敗しました。Target={targetAdapter.SiteName}");
            }

            var record = new TransferRecord
            {
                Timestamp = DateTime.Now,
                SourceSite = sourceAdapter.SiteName,
                TargetSite = targetAdapter.SiteName,
                Direction = $"{sourceAdapter.SiteName} → {targetAdapter.SiteName}",
                Text = text,
                Submitted = submit,
                Status = submit ? "送信完了" : "入力完了"
            };

            _records.Insert(0, record);

            return TransferResult.Ok(submit
                ? $"{targetAdapter.SiteName} に送信しました。"
                : $"{targetAdapter.SiteName} の入力欄へ挿入しました。");
        }

        public async Task<TransferResult> ReuseAsync(
            TransferRecord record,
            WebView2 targetWebView,
            string targetUrl,
            bool submit,
            string? overrideText = null)
        {
            var targetAdapter = _adapterResolver.Resolve(targetUrl);
            if (targetAdapter == null)
                return TransferResult.Fail("送信先サイトのアダプタが見つかりません。");

            string text = overrideText ?? record.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return TransferResult.Fail("再利用するテキストが空です。");

            Log($"[Reuse] Target={targetAdapter.SiteName}, Submit={submit}, Length={text.Length}");

            bool inputOk = await targetAdapter.SetInputAsync(targetWebView, text);
            Log($"[Reuse] SetInputAsync={inputOk}");

            if (!inputOk)
                return TransferResult.Fail($"入力欄への設定に失敗しました。Target={targetAdapter.SiteName}");

            if (submit)
            {
                await Task.Delay(800);
                bool sendOk = await targetAdapter.SendAsync(targetWebView);
                Log($"[Reuse] SendAsync={sendOk}");

                if (!sendOk)
                    return TransferResult.Fail($"送信操作に失敗しました。Target={targetAdapter.SiteName}");
            }

            return TransferResult.Ok(submit
                ? $"{targetAdapter.SiteName} に再送信しました。"
                : $"{targetAdapter.SiteName} の入力欄へ再挿入しました。");
        }
    }

    public sealed class TransferResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; } = "";

        public static TransferResult Ok(string message) =>
            new TransferResult { Success = true, Message = message };

        public static TransferResult Fail(string message) =>
            new TransferResult { Success = false, Message = message };
    }
}
