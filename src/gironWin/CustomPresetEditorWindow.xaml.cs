using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class CustomPresetEditorWindow : Window
    {
        /// <summary>編集結果。SaveButton_Click 後に設定される。</summary>
        public DebatePreset? ResultPreset { get; private set; }

        /// <summary>新規作成モード</summary>
        public CustomPresetEditorWindow() => InitializeComponent();

        /// <summary>既存プリセット編集モード</summary>
        public CustomPresetEditorWindow(DebatePreset existing) : this()
        {
            NameBox.Text         = existing.Name;
            DescBox.Text         = existing.Description;
            LeftPromptBox.Text   = existing.LeftPrompt;
            RightPromptBox.Text  = existing.RightPrompt;
            TopicBox.Text        = existing.Topic;
            ResearchCheckBox.IsChecked = existing.ResearchMode;

            foreach (ComboBoxItem item in PolicyCombo.Items)
                if (item.Tag?.ToString() == existing.TurnPolicy.ToString())
                { item.IsSelected = true; break; }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("プリセット名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var policyStr = (PolicyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                            ?? "RoundRobin";
            Enum.TryParse<TurnPolicy>(policyStr, out var policy);

            ResultPreset = new DebatePreset
            {
                Name         = name,
                Description  = DescBox.Text.Trim(),
                TurnPolicy   = policy,
                LeftPrompt   = LeftPromptBox.Text.Trim(),
                RightPrompt  = RightPromptBox.Text.Trim(),
                Topic        = TopicBox.Text.Trim(),
                ResearchMode = ResearchCheckBox.IsChecked == true
            };

            // JSON に永続保存
            CustomPresetRepository.Save(ResultPreset);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    // ---------------------------------------------------------------
    // カスタムプリセット永続化ヘルパー
    // ---------------------------------------------------------------
    public static class CustomPresetRepository
    {
        private static string Dir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "gironWin", "presets");

        public static void Save(DebatePreset preset)
        {
            Directory.CreateDirectory(Dir);
            string safeName = MakeSafeFileName(preset.Name);
            string path     = Path.Combine(Dir, $"{safeName}.json");
            string json     = JsonSerializer.Serialize(preset,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public static void Delete(DebatePreset preset)
        {
            string safeName = MakeSafeFileName(preset.Name);
            string path     = Path.Combine(Dir, $"{safeName}.json");
            if (File.Exists(path)) File.Delete(path);
        }

        public static System.Collections.Generic.List<DebatePreset> LoadAll()
        {
            var list = new System.Collections.Generic.List<DebatePreset>();
            if (!Directory.Exists(Dir)) return list;

            foreach (var f in Directory.GetFiles(Dir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(f);
                    var p = JsonSerializer.Deserialize<DebatePreset>(json);
                    if (p != null) list.Add(p);
                }
                catch { /* 破損ファイルはスキップ */ }
            }
            return list;
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
