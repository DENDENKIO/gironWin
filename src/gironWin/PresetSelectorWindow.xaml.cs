using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class PresetSelectorWindow : Window
    {
        public DebatePreset? SelectedPreset { get; private set; }

        public PresetSelectorWindow()
        {
            InitializeComponent();

            var items = BuiltInPresets.All.Select(p => new PresetViewModel(p)).ToList();
            PresetListBox.ItemsSource = items;
            if (items.Count > 0) PresetListBox.SelectedIndex = 0;
        }

        private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetViewModel vm)
            {
                LeftPromptText.Text   = $"左席: {vm.Preset.LeftPrompt}";
                RightPromptText.Text  = $"右席: {vm.Preset.RightPrompt}";
                ResearchModeText.Text = vm.Preset.ResearchMode ? "⚗ 研究モード有効" : string.Empty;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetViewModel vm)
            {
                SelectedPreset = vm.Preset;
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private sealed class PresetViewModel
        {
            public DebatePreset Preset { get; }
            public string Name => Preset.Name;
            public string TurnPolicyDescription => Preset.TurnPolicy switch
            {
                TurnPolicy.CritiqueThenRefine  => "提案 → 批判 → 改善",
                TurnPolicy.ResearchReviewLoop  => "仮説 → 証明案 → 反例 → 査読",
                TurnPolicy.ModeratorSelect     => "司会選択",
                _                              => "左右往復"
            };
            public PresetViewModel(DebatePreset p) => Preset = p;
        }
    }
}
