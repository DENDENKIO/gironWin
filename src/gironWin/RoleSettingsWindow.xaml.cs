using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class RoleSettingsWindow : Window
    {
        public string LeftPrompt  { get; private set; } = string.Empty;
        public string RightPrompt { get; private set; } = string.Empty;

        public RoleSettingsWindow(string currentLeft, string currentRight)
        {
            InitializeComponent();

            // プリセットを ComboBox に登録
            foreach (var p in PromptProfile.Presets)
            {
                LeftPresetCombo.Items.Add(p);
                RightPresetCombo.Items.Add(p);
            }

            LeftPromptTextBox.Text  = currentLeft;
            RightPromptTextBox.Text = currentRight;

            // 現在値に一致するプリセットを初期選択
            LeftPresetCombo.SelectedItem  = PromptProfile.Presets.FirstOrDefault(p => p.SystemPrompt == currentLeft)
                                             ?? PromptProfile.Presets[0];
            RightPresetCombo.SelectedItem = PromptProfile.Presets.FirstOrDefault(p => p.SystemPrompt == currentRight)
                                             ?? PromptProfile.Presets[0];
        }

        private void LeftPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LeftPresetCombo.SelectedItem is PromptProfile p)
                LeftPromptTextBox.Text = p.SystemPrompt;
        }

        private void RightPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RightPresetCombo.SelectedItem is PromptProfile p)
                RightPromptTextBox.Text = p.SystemPrompt;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            LeftPrompt  = LeftPromptTextBox.Text.Trim();
            RightPrompt = RightPromptTextBox.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
