using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// 送信前の承認待ちアイテムを管理するキュー。
    /// AutoDebateService が Enqueue し、UI 側が Approve / Reject する。
    /// </summary>
    public class ApprovalQueue
    {
        public ObservableCollection<ApprovalItem> Items { get; } = new();

        /// <summary>
        /// 承認待ちアイテムを積む。
        /// 承認か却下が行われるまで呼び出し元を非同期に待機させる。
        /// </summary>
        public Task<ApprovalResult> EnqueueAsync(
            string sourceSite,
            string targetSite,
            string text,
            bool submit,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<ApprovalResult>();
            ct.Register(() => tcs.TrySetCanceled());

            var item = new ApprovalItem
            {
                ItemId = Guid.NewGuid().ToString(),
                SourceSite = sourceSite,
                TargetSite = targetSite,
                Text = text,
                Submit = submit,
                CreatedAt = DateTime.Now,
                _tcs = tcs
            };

            Items.Add(item);
            return tcs.Task;
        }

        public void Approve(ApprovalItem item, string? editedText = null)
        {
            Items.Remove(item);
            item._tcs.TrySetResult(new ApprovalResult(true, editedText ?? item.Text));
        }

        public void Reject(ApprovalItem item)
        {
            Items.Remove(item);
            item._tcs.TrySetResult(new ApprovalResult(false, item.Text));
        }
    }

    public sealed class ApprovalItem
    {
        public string ItemId { get; init; } = string.Empty;
        public string SourceSite { get; init; } = string.Empty;
        public string TargetSite { get; init; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool Submit { get; init; }
        public DateTime CreatedAt { get; init; }

        public string PreviewText => Text.Length > 100 ? Text[..100] + "…" : Text;
        public string Direction => $"{SourceSite} → {TargetSite}";

        internal TaskCompletionSource<ApprovalResult> _tcs = null!;
    }

    public sealed class ApprovalResult
    {
        public bool Approved { get; }
        public string Text { get; }

        public ApprovalResult(bool approved, string text)
        {
            Approved = approved;
            Text = text;
        }
    }
}
