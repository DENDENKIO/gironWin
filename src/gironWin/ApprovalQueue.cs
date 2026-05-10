using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// 送信前の承認待ちアイテムを管理するキュー。
    /// AutoDebateService が EnqueueAsync し、UI 側が Approve / Reject する。
    /// Phase 3-5: ApprovalRequested イベント追加（MainWindow 承認パネルと接続）。
    /// </summary>
    public class ApprovalQueue
    {
        public ObservableCollection<ApprovalItem> Items { get; } = new();

        /// <summary>
        /// 承認リクエストが積まれたとき UI へ通知するイベント。
        /// MainWindow はこれを購読して承認パネルを表示する。
        /// </summary>
        public event EventHandler<ApprovalRequestedEventArgs>? ApprovalRequested;

        // 現在待機中のアイテム（シングルキュー前提）
        private ApprovalItem? _pendingItem;

        /// <summary>
        /// 承認待ちアイテムを積む。承認か却下まで非同期待機する。
        /// </summary>
        public Task<ApprovalResult> EnqueueAsync(
            string sourceSite,
            string targetSite,
            string text,
            bool submit,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<ApprovalResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled());

            var item = new ApprovalItem
            {
                ItemId     = Guid.NewGuid().ToString(),
                SourceSite = sourceSite,
                TargetSite = targetSite,
                Text       = text,
                Submit     = submit,
                CreatedAt  = DateTime.Now,
                _tcs       = tcs
            };

            _pendingItem = item;
            Items.Add(item);

            // UI へ通知
            ApprovalRequested?.Invoke(this, new ApprovalRequestedEventArgs
            {
                Source    = item.SourceSite,
                Target    = item.TargetSite,
                Direction = item.Direction,
                Text      = item.Text,
                Item      = item
            });

            return tcs.Task;
        }

        // ---------------------------------------------------------------
        // Approve / Reject — ApprovalItem 指定版（既存互換）
        // ---------------------------------------------------------------

        public void Approve(ApprovalItem item, string? editedText = null)
        {
            Items.Remove(item);
            if (_pendingItem == item) _pendingItem = null;
            item._tcs.TrySetResult(new ApprovalResult(true, editedText ?? item.Text));
        }

        public void Reject(ApprovalItem item)
        {
            Items.Remove(item);
            if (_pendingItem == item) _pendingItem = null;
            item._tcs.TrySetResult(new ApprovalResult(false, item.Text));
        }

        // ---------------------------------------------------------------
        // Approve / Reject — MainWindow 承認パネル用（テキストのみ渡す版）
        // ---------------------------------------------------------------

        /// <summary>
        /// 承認パネルの編集済みテキストを渡して承認する。
        /// 現在の pending item に対して動作する。
        /// </summary>
        public void Approve(string editedText)
        {
            if (_pendingItem == null) return;
            Approve(_pendingItem, editedText);
        }

        /// <summary>
        /// 現在の pending item を却下する。
        /// </summary>
        public void Reject()
        {
            if (_pendingItem == null) return;
            Reject(_pendingItem);
        }
    }

    // ---------------------------------------------------------------
    // イベント引数
    // ---------------------------------------------------------------

    /// <summary>
    /// Phase 3-5: 承認リクエストイベント引数。
    /// MainWindow が承認パネルに表示するために使用する。
    /// </summary>
    public sealed class ApprovalRequestedEventArgs : EventArgs
    {
        /// <summary>送信元 (例: "Perplexity")</summary>
        public string       Source    { get; init; } = string.Empty;
        /// <summary>送信先 (例: "Gemini")</summary>
        public string       Target    { get; init; } = string.Empty;
        /// <summary>送信方向 (例: "Perplexity → Gemini")</summary>
        public string       Direction { get; init; } = string.Empty;
        /// <summary>承認対象テキスト</summary>
        public string       Text      { get; init; } = string.Empty;
        /// <summary>対応する ApprovalItem（直接操作する場合）</summary>
        public ApprovalItem Item      { get; init; } = null!;
    }

    // ---------------------------------------------------------------
    // ApprovalItem / ApprovalResult
    // ---------------------------------------------------------------

    public sealed class ApprovalItem
    {
        public string   ItemId     { get; init; } = string.Empty;
        public string   SourceSite { get; init; } = string.Empty;
        public string   TargetSite { get; init; } = string.Empty;
        public string   Text       { get; set;  } = string.Empty;
        public bool     Submit     { get; init; }
        public DateTime CreatedAt  { get; init; }

        public string PreviewText => Text.Length > 100 ? Text[..100] + "…" : Text;
        public string Direction   => $"{SourceSite} → {TargetSite}";

        internal TaskCompletionSource<ApprovalResult> _tcs = null!;
    }

    public sealed class ApprovalResult
    {
        public bool   Approved { get; }
        public string Text     { get; }

        public ApprovalResult(bool approved, string text)
        {
            Approved = approved;
            Text     = text;
        }
    }
}
