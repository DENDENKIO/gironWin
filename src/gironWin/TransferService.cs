using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

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

        private void Log(string message) => DebugLog?.Invoke(this, message);

        // ★ 修正: リトライ前に再度 SetInput を呼ばない（追記バグの原因）
        //   クリアは TransferAsync 冒頭で1回行い、ここでは本文挿入だけ試みる
        private async Task<bool> TrySetInputWithRetryAsync(
            IAiSiteAdapter adapter, WebView2 webView, string text)
        {
            int[] waitMs = { 0, 1500, 4000 };
            for (int i = 0; i < waitMs.Length; i++)
            {
                if (waitMs[i] > 0)
                {
                    Log($"[SetInput] {adapter.SiteName} retry wait {waitMs[i]}ms...");
                    await Task.Delay(waitMs[i]);

                    // ★ 修正: リトライ前に再クリアのみ行い、本文は1回だけ渡す
                    //   （以前はここで SetInput(empty) → SetInput(text) と2回呼んでいた）
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        adapter.SetInputAsync(webView, string.Empty)
                    ).Task.Unwrap();
                    await Task.Delay(300);
                }

                bool ok = await Application.Current.Dispatcher.InvokeAsync(() =>
                    adapter.SetInputAsync(webView, text)
                ).Task.Unwrap();

                Log($"[SetInput] {adapter.SiteName} attempt={i + 1} result={ok}");
                if (ok)
                {
                    // ★ デバッグログ: DOM の実際の中身を読み返す（追記バグ診断用）
                    try
                    {
                        string checkScript = @"
(() => {
    const selectors = [
        '#ask-input[contenteditable=""true""]',
        '#ask-input',
        'div[contenteditable=""true""][role=""textbox""]',
        'div[role=""textbox""][contenteditable=""true""]',
        'div[contenteditable=""true""]',
        'textarea',
        'input[type=""text""]'
    ];
    let el = null;
    for (const sel of selectors) {
        const found = Array.from(document.querySelectorAll(sel)).filter(e => {
            const s = window.getComputedStyle(e);
            return s.display !== 'none' && s.visibility !== 'hidden' && e.offsetParent !== null;
        });
        if (found.length > 0) { el = found[0]; break; }
    }
    if (!el) return 'DOM-NOT-FOUND';
    const val = (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') 
        ? el.value 
        : (el.innerText || el.textContent || '');
    return JSON.stringify({ len: val.length, preview: val.substring(0, 100).replace(/\n/g, ' ') });
})();";
                        string valJson = await webView.ExecuteScriptAsync(checkScript);
                        Log($"[DEBUG SetInput] {adapter.SiteName} DOM state: {valJson}");
                    }
                    catch (Exception ex) { Log($"[DEBUG SetInput] DOM check failed: {ex.Message}"); }

                    return true;
                }
            }
            return false;
        }

        // SendAsync を最大3回試みるヘルパー
        private async Task<bool> TrySendWithRetryAsync(
            IAiSiteAdapter adapter, WebView2 webView, string siteName)
        {
            int[] waitMs = { 600, 3000, 10000 };

            for (int i = 0; i < waitMs.Length; i++)
            {
                await Task.Delay(waitMs[i]);

                bool ok = await Application.Current.Dispatcher.InvokeAsync(() =>
                    adapter.SendAsync(webView)
                ).Task.Unwrap();

                Log($"[Send] {siteName} attempt={i + 1} result={ok}");
                if (ok) return true;

                if (i < waitMs.Length - 1)
                    Log($"[Send] {siteName} retry in {waitMs[i + 1]}ms...");
            }
            return false;
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
                text = await Application.Current.Dispatcher.InvokeAsync(() =>
                    sourceAdapter.GetSelectedTextAsync(sourceWebView)
                ).Task.Unwrap();
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

            // ★ デバッグ: 転送テキスト全体の先頭・末尾をログ出力
            {
                string preview = text.Length <= 500
                    ? text
                    : text[..300] + $"\n...(中略 {text.Length - 400}文字)...\n" + text[^100..];
                Log($"[Transfer] FinalText.Length={text.Length}");
                Log($"[Transfer] FinalText.Content=\n---BEGIN---\n{preview}\n---END---");
            }

            // ★ 入力前に入力欄を必ず1回だけ空クリア
            Log($"[Transfer] Clearing input on {targetAdapter.SiteName}...");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                targetAdapter.SetInputAsync(targetWebView, string.Empty)
            ).Task.Unwrap();
            await Task.Delay(300);

            bool inputOk = await TrySetInputWithRetryAsync(targetAdapter, targetWebView, text);
            if (!inputOk)
                return TransferResult.Fail($"入力欄への設定に失敗しました（3回試行）。Target={targetAdapter.SiteName}");

            if (submit)
            {
                bool sendOk = await TrySendWithRetryAsync(targetAdapter, targetWebView, targetAdapter.SiteName);
                if (!sendOk)
                    return TransferResult.Fail($"送信操作に失敗しました（3回試行）。Target={targetAdapter.SiteName}");
            }

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

            bool inputOk = await TrySetInputWithRetryAsync(targetAdapter, targetWebView, text);
            if (!inputOk)
                return TransferResult.Fail($"入力欄への設定に失敗しました（3回試行）。Target={targetAdapter.SiteName}");

            if (submit)
            {
                bool sendOk = await TrySendWithRetryAsync(targetAdapter, targetWebView, targetAdapter.SiteName);
                if (!sendOk)
                    return TransferResult.Fail($"送信操作に失敗しました（3回試行）。Target={targetAdapter.SiteName}");
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
