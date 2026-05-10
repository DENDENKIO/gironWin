using gironWin.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    public static class LogReaderLauncher
    {
        private const string LogReaderExeName = "gironWin.LogReader.exe";

        public static void Open(
            IReadOnlyList<TransferRecord> records,
            int startIndex,
            QuoteService? quoteService = null)
        {
            string? exePath = FindLogReaderExe();

            if (exePath == null)
            {
                // 探索した全パスをデバッグ表示
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                System.Windows.MessageBox.Show(
                    "gironWin.LogReader.exe が見つかりません。\n\n" +
                    "【対処方法】\n" +
                    "Visual Studio のツールバーのプラットフォームを\n" +
                    "「Any CPU」→「x64」に変更してから\n" +
                    "gironWin.LogReader を右クリック→ビルド\n\n" +
                    $"探索起点: {baseDir}",
                    "ログリーダー未ビルド",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            string pipeId  = Guid.NewGuid().ToString("N");
            string tmpPath = Path.Combine(
                Path.GetTempPath(), $"giron_logreader_{pipeId}.json");

            var payload = new LogReaderPayload
            {
                Records    = new List<TransferRecord>(records),
                StartIndex = startIndex,
                PipeId     = pipeId
            };
            File.WriteAllText(tmpPath,
                JsonSerializer.Serialize(payload), Encoding.UTF8);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = exePath,
                    Arguments       = $"\"{tmpPath}\"",
                    UseShellExecute = false
                });

                if (quoteService != null)
                    _ = ListenForQuotesAsync(pipeId, quoteService);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"LogReader 起動失敗:\n{ex.Message}\n\nパス: {exePath}");
            }
        }

        /// <summary>
        /// WinUI 3 の出力パスは TFM + アーキテクチャ サブフォルダが付く。
        /// 例: bin\Debug\net8.0-windows10.0.22621.0\x64\gironWin.LogReader.exe
        /// Any CPU ではEXEが生成されないため x64/x86/arm64 を優先検索。
        /// </summary>
        private static string? FindLogReaderExe()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // ① 同じフォルダ（発行時・手動コピー時）
            string same = Path.Combine(baseDir, LogReaderExeName);
            if (File.Exists(same)) return same;

            // ② baseDirから "src" フォルダまで親を辿る
            DirectoryInfo? srcDir = new DirectoryInfo(baseDir);
            while (srcDir != null &&
                   !srcDir.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
                srcDir = srcDir.Parent;

            if (srcDir == null) return null;

            string logReaderBinRoot = Path.Combine(
                srcDir.FullName, "gironWin.LogReader", "bin");

            if (!Directory.Exists(logReaderBinRoot)) return null;

            // ③ 全再帰検索（TFM・構成・アーキテクチャのどの組み合わせでも対応）
            string[] allExes = Directory.GetFiles(
                logReaderBinRoot, LogReaderExeName,
                SearchOption.AllDirectories);

            if (allExes.Length == 0) return null;

            // 優先順位: x64 > x86 > arm64 > その他、かつ Release > Debug
            static int Score(string path)
            {
                int arch   = path.Contains("x64")   ? 3 :
                             path.Contains("x86")   ? 2 :
                             path.Contains("arm64") ? 1 : 0;
                int config = path.Contains("Release") ? 10 : 0;
                return arch + config;
            }

            string best = allExes[0];
            int bestScore = Score(allExes[0]);
            foreach (string p in allExes)
            {
                int s = Score(p);
                if (s > bestScore) { bestScore = s; best = p; }
            }
            return best;
        }

        private static async Task ListenForQuotesAsync(
            string pipeId, QuoteService quoteService)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromHours(2));
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        $"giron_quote_{pipeId}",
                        PipeDirection.In, 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cts.Token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string json = await reader.ReadToEndAsync();

                    var cb = JsonSerializer.Deserialize<QuoteCallbackPayload>(json);
                    if (cb == null) continue;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        quoteService.AddPartialQuote(new PartialQuote
                        {
                            SourceTurnNumber = cb.SourceTurnNumber,
                            QuotedText       = cb.QuotedText,
                            TargetSeat       = cb.TargetSeat,
                            RegisteredAt     = DateTime.Now
                        });
                    });
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }
    }
}
