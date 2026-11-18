using gironWin.Shared;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace gironWin.LogReader
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            string path = e.Args.Length > 0 ? e.Args[0] : string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException("ログデータが指定されていません。本体のログ一覧からログリーダーを開いてください。");
                path = Path.GetFullPath(path);
                string json = File.ReadAllText(path, Encoding.UTF8);
                var payload = JsonSerializer.Deserialize<LogReaderPayload>(json)
                    ?? throw new InvalidDataException("ログデータが null です。");
                if (payload.Records == null || payload.Records.Count == 0)
                    throw new InvalidDataException("本体から受け取ったログが0件です。表示対象のログを確認してください。");
                if (payload.Records.Any(record => record == null))
                    throw new InvalidDataException("ログ一覧に null のレコードが含まれています。");

                var window = gironWin.LogReader.MainWindow.FromValidatedPayload(payload);
                MainWindow = window;
                window.Show();
                try { File.Delete(path); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogReader] 一時ファイル削除失敗: {ex}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogReader] 起動失敗: {ex}");
                MessageBox.Show(
                    $"ログリーダーを起動できませんでした。\n\n{ex.Message}\n\n入力ファイル: {path}\n\n読み込みに失敗したファイルは削除していません。",
                    "ログ読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }
}
