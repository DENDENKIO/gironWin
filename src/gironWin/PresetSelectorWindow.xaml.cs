using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class PresetSelectorWindow : Window
    {
        public DebatePreset? SelectedPreset { get; private set; }

        private readonly List<DebatePreset> _builtins = new()
        {
            new DebatePreset
            {
                Name        = "建設的討論",
                Description = "提案→批判→改善の3フェーズで討論する。",
                TurnPolicy  = TurnPolicy.CritiqueThenRefine,
                LeftPrompt  = "あなたは革新的なアイデアを提案する討論者です。",
                RightPrompt = "あなたは批判的・建設的なフィードバックを行う討論者です。",
                ResearchMode = false
            },
            new DebatePreset
            {
                Name        = "アプリ設計レビュー",
                Description = "アプリの仕様・UI・リスクをレビューする。",
                TurnPolicy  = TurnPolicy.RoundRobin,
                LeftPrompt  = "あなたはアプリ設計を提案するアーキテクトです。",
                RightPrompt = "あなたはコードレビュアーとしてリスクと改善点を指摘します。",
                ResearchMode = false
            },
            new DebatePreset
            {
                Name        = "数学研究",
                Description = "命題・証明案・反例・査読の4フェーズで数学的議論を行う。",
                TurnPolicy  = TurnPolicy.ResearchReviewLoop,
                LeftPrompt  = "あなたは証明案を提示する数学者です。",
                RightPrompt = "あなたは反例を探索・査読する数学者です。",
                ResearchMode = true
            },
            new DebatePreset
            {
                Name        = "ソクラテス式問答",
                Description = "問いと答えを繰り返しながら概念を深める。",
                TurnPolicy  = TurnPolicy.ModeratorSelect,
                LeftPrompt  = "あなたはソクラテスとして深い問いを投げかけます。",
                RightPrompt = "あなたは誠実に答え、自分の立場を論証します。",
                ResearchMode = false
            },
            new DebatePreset
            {
                Name        = "究極の専門家 vs たとえ上手な素人",
                Description = "高度な専門家と、たとえ話で理解しようとする素人の対話。",
                TurnPolicy  = TurnPolicy.RoundRobin,
                LeftPrompt  = PromptProfile.UltimateExpertVsBeginnerPreset.LeftSystemPrompt,
                RightPrompt = PromptProfile.UltimateExpertVsBeginnerPreset.RightSystemPrompt,
                ResearchMode = false
            },
            new DebatePreset
            {
                Name        = "人間割り込み討論",
                Description = "人間がターンごとに割り込める討論モード。",
                TurnPolicy  = TurnPolicy.HumanPriority,
                LeftPrompt  = "あなたは議題を深掘りする討論者です。",
                RightPrompt = "あなたは対案を提示する討論者です。",
                ResearchMode = false
            }
        };

        public PresetSelectorWindow()
        {
            InitializeComponent();
            RefreshList();
        }

        private void RefreshList()
        {
            PresetListBox.Items.Clear();

            // ビルトイン
            foreach (var p in _builtins)
                PresetListBox.Items.Add(new PresetListItem { Preset = p, IsCustom = false });

            // カスタム（JSON から読み込み）
            foreach (var p in CustomPresetRepository.LoadAll())
                PresetListBox.Items.Add(new PresetListItem { Preset = p, IsCustom = true });

            PresetListBox.DisplayMemberPath = "DisplayName";
        }

        private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetListItem item)
                DescriptionTextBlock.Text = item.Preset.Description;

            bool isCustom = (PresetListBox.SelectedItem as PresetListItem)?.IsCustom == true;
            EditButton.IsEnabled   = isCustom;
            DeleteButton.IsEnabled = isCustom;
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is not PresetListItem item) return;
            SelectedPreset = item.Preset;
            DialogResult   = true;
            Close();
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new CustomPresetEditorWindow { Owner = this };
            if (win.ShowDialog() == true && win.ResultPreset != null)
                RefreshList();
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is not PresetListItem item || !item.IsCustom) return;
            var win = new CustomPresetEditorWindow(item.Preset) { Owner = this };
            if (win.ShowDialog() == true)
                RefreshList();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is not PresetListItem item || !item.IsCustom) return;
            var result = MessageBox.Show(
                $"「{item.Preset.Name}」を削除しますか？",
                "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                CustomPresetRepository.Delete(item.Preset);
                RefreshList();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
    }

    /// <summary>ListBox 表示用ラッパー</summary>
    internal class PresetListItem
    {
        public DebatePreset Preset  { get; init; } = null!;
        public bool         IsCustom { get; init; }
        public string DisplayName =>
            IsCustom ? $"★ {Preset.Name}" : Preset.Name;
    }
}
