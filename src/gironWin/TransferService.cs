using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// 転送・再利用ロジックを担う。
    /// MainWindow はこのサービスを通じて操作し、UI 操作には直接関与しない。
    /// </summary>
    public class TransferService
    {
        private readonly AiSiteAdapterResolver _adapterResolver;
        private readonly ObservableCollection<TransferRecord> _records;

        public TransferService(
            AiSiteAdapterResolver adapterResolver,
            ObservableCollection<TransferRecord> records)
        {
            _adapterResolver = adapterResolver;
            _records = records;
        }

        /// <summary>
        /// 転送を実行する。
        /// </summary>
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

            string rawText = manualText
                ?? await sourceAdapter.GetSelectedTextAsync(sourceWebView);

            string text = BuildTransferText(rawText, appendBridge);

            if (string.IsNullOrWhiteSpace(text))
            {
                return TransferResult.Fail("選択された文字列がありません。");
            }

            return await ExecuteTransferAsync(
                sourceAdapter.SiteName,
                targetAdapter,
                targetWebView,
                text,
                submit);
        }

        /// <summary>
        /// 履歴レコードを再利用して転送する。
        /// </summary>
        public async Task<TransferResult> ReuseAsync(
            TransferRecord record,
            WebView2 targetWebView,
            string targetUrl,
            bool submit,
            string? overrideText = null)
        {
            if (record == null)
                return TransferResult.Fail("履歴が選択されていません。");

            var targetAdapter = _adapterResolver.Resolve(targetUrl);
            if (targetAdapter == null)
                return TransferResult.Fail("送信先サイトのアダプタが見つかりません。");

            string text = overrideText ?? record.Text;

            if (string.IsNullOrWhiteSpace(text))
                return TransferResult.Fail("再利用テキストが空です。");

            return await ExecuteTransferAsync(
                record.SourceSite,
                targetAdapter,
                targetWebView,
                text,
                submit);
        }

        // ---------------------------------------------------------------

        private async Task<TransferResult> ExecuteTransferAsync(
            string sourceSiteName,
            IAiSiteAdapter targetAdapter,
            WebView2 targetWebView,
            string text,
            bool submit)
        {
            bool inputOk = await targetAdapter.SetInputAsync(targetWebView, text);
            if (!inputOk)
            {
                AddRecord(sourceSiteName, targetAdapter.SiteName, text, submit, "入力失敗");
                return TransferResult.Fail($"{targetAdapter.SiteName} の入力欄が見つかりませんでした。");
            }

            if (!submit)
            {
                AddRecord(sourceSiteName, targetAdapter.SiteName, text, false, "入力のみ");
                return TransferResult.Ok($"{targetAdapter.SiteName} へ入力しました。");
            }

            await Task.Delay(300);

            bool sendOk = await targetAdapter.SendAsync(targetWebView);
            if (!sendOk)
            {
                AddRecord(sourceSiteName, targetAdapter.SiteName, text, true, "送信失敗");
                return TransferResult.Fail($"{targetAdapter.SiteName} への送信に失敗しました。");
            }

            AddRecord(sourceSiteName, targetAdapter.SiteName, text, true, "送信成功");
            return TransferResult.Ok($"{targetAdapter.SiteName} へ送信しました。");
        }

        private string BuildTransferText(string sourceText, bool appendBridge)
        {
            if (string.IsNullOrWhiteSpace(sourceText))
                return string.Empty;

            return appendBridge
                ? $"{sourceText}\n\nこのように考えていますがどうですか？"
                : sourceText;
        }

        private void AddRecord(
            string sourceSite,
            string targetSite,
            string text,
            bool submitted,
            string status)
        {
            _records.Insert(0, new TransferRecord
            {
                Timestamp = DateTime.Now,
                SourceSite = sourceSite,
                TargetSite = targetSite,
                Direction = $"{sourceSite} → {targetSite}",
                Text = text,
                Submitted = submitted,
                Status = status,
                ApprovalStatus = ApprovalStatuses.NotRequired
            });
        }
    }

    /// <summary>
    /// 転送操作の結果を表す。
    /// </summary>
    public sealed class TransferResult
    {
        public bool Success { get; private init; }
        public string Message { get; private init; } = string.Empty;

        public static TransferResult Ok(string message) =>
            new() { Success = true, Message = message };

        public static TransferResult Fail(string message) =>
            new() { Success = false, Message = message };
    }
}
