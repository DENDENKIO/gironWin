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
using System.Windows;

namespace gironWin
{
    public static class LogReaderLauncher
    {
        private const string ExeName = "gironWin.LogReader.exe";

        public static void Open(IReadOnlyList<TransferRecord> records,
            int startIndex, QuoteService? quoteService = null)
        {
            try
            {
                string expected = FindExpectedExe();
                if (!File.Exists(expected))
                {
                    MessageBox.Show($"対応するログリーダーがありません。\n本体と同じ構成でソリューション全体をビルドしてください。\n\n必要な場所:\n{expected}",
                        "ログリーダー未ビルド", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var payload = new LogReaderPayload
                {
                    Records = new List<TransferRecord>(records),
                    StartIndex = startIndex,
                    PipeId = Guid.NewGuid().ToString("N")
                };
                _ = RunReaderAsync(expected, payload, quoteService);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"LogReader準備失敗:\n{ex.Message}");
            }
        }

        private static string FindExpectedExe()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(baseDir); dir != null; dir = dir.Parent)
            {
                if (!dir.Name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || dir.Parent == null
                    || !dir.Parent.Name.Equals("gironWin", StringComparison.OrdinalIgnoreCase))
                    continue;
                var src = dir.Parent.Parent;
                if (src == null) break;
                string relative = Path.GetRelativePath(dir.FullName, baseDir);
                return Path.GetFullPath(Path.Combine(src.FullName,
                    "gironWin.LogReader", "bin", relative, ExeName));
            }
            return Path.Combine(baseDir, ExeName);
        }

        private static async Task RunReaderAsync(string exePath,
            LogReaderPayload payload, QuoteService? quotes)
        {
            string temp = Path.Combine(Path.GetTempPath(), $"giron_logreader_{payload.PipeId}.json");
            using var cts = new CancellationTokenSource();
            var app = Application.Current;
            ExitEventHandler onExit = (_, _) => cts.Cancel();
            if (app != null) app.Exit += onExit;
            Task listener = Task.CompletedTask;
            Process? process = null;
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(payload), Encoding.UTF8);
                if (quotes != null)
                    listener = ListenForQuotesAsync(payload.PipeId, quotes, cts.Token);
                AppLogger.Info(LogCategory.System, $"LogReader起動: {exePath}");
                var info = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath)!,
                    UseShellExecute = false
                };
                info.ArgumentList.Add(temp);
                process = Process.Start(info)
                    ?? throw new InvalidOperationException("プロセスを起動できませんでした。");
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                AppLogger.Warn(LogCategory.System, $"LogReader失敗: {ex.Message}");
                if (app != null && !app.Dispatcher.HasShutdownStarted)
                    MessageBox.Show($"LogReader起動・監視失敗:\n{ex.Message}\n\n{exePath}");
            }
            finally
            {
                cts.Cancel();
                try { await listener; }
                catch (Exception ex) { Debug.WriteLine(ex); }
                if (app != null) app.Exit -= onExit;
                process?.Dispose();
                try { File.Delete(temp); }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
        }

        private static async Task ListenForQuotesAsync(string pipeId,
            QuoteService quotes, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        $"giron_quote_{pipeId}", PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string json = await reader.ReadToEndAsync(ct);
                    var cb = JsonSerializer.Deserialize<QuoteCallbackPayload>(json);
                    if (cb == null) continue;
                    var app = Application.Current;
                    if (app == null || app.Dispatcher.HasShutdownStarted) break;
                    await app.Dispatcher.InvokeAsync(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        quotes.AddPartialQuote(new PartialQuote
                        {
                            SourceTurnNumber = cb.SourceTurnNumber,
                            QuotedText = cb.QuotedText,
                            TargetSeat = cb.TargetSeat,
                            RegisteredAt = DateTime.Now
                        });
                    }, System.Windows.Threading.DispatcherPriority.Normal, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LogReader quote] {ex}");
                    try { await Task.Delay(250, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
    }
}
