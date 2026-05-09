using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class RoleSettingsWindow : Window
    {
        public PromptProfile? ResultProfile { get; private set; }

        private readonly PromptProfile _current;

        public RoleSettingsWindow(PromptProfile? current = null)
        {
            InitializeComponent();
            _current = current ?? PromptProfile.Default;
            LoadProfile(_current);
        }

        private void LoadProfile(PromptProfile p)
        {
            LeftNameTextBox.Text    = p.LeftName;
            RightNameTextBox.Text   = p.RightName;
            LeftPromptTextBox.Text  = p.LeftSystemPrompt;
            RightPromptTextBox.Text = p.RightSystemPrompt;
            TopicTextBox.Text       = p.Topic;

            // \u30d7\u30ea\u30bb\u30c3\u30c8\u30b3\u30f3\u30dc
            ProfileComboBox.SelectedIndex = p.ProfileId switch
            {
                "debate"   => 1,
                "research" => 2,
                "critique" => 3,
                _          => 0
            };
        }

        // ---------------------------------------------------------------
        // \u30d7\u30ea\u30bb\u30c3\u30c8\u5909\u66f4
        // ---------------------------------------------------------------
        private void ProfileComboBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (ProfileComboBox == null || LeftNameTextBox == null) return;
            var preset = ProfileComboBox.SelectedIndex switch
            {
                1 => PromptProfile.DebatePreset,
                2 => PromptProfile.ResearchPreset,
                3 => PromptProfile.CritiquePreset,
                _ => PromptProfile.Default
            };
            LoadProfile(preset);
        }

        // ---------------------------------------------------------------
        // OK / Cancel
        // ---------------------------------------------------------------
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ResultProfile = new PromptProfile
            {
                LeftName          = LeftNameTextBox.Text.Trim(),
                RightName         = RightNameTextBox.Text.Trim(),
                LeftSystemPrompt  = LeftPromptTextBox.Text.Trim(),
                RightSystemPrompt = RightPromptTextBox.Text.Trim(),
                Topic             = TopicTextBox.Text.Trim(),
                ProfileId         = ProfileComboBox.SelectedIndex switch
                {
                    1 => "debate",
                    2 => "research",
                    3 => "critique",
                    _ => "custom"
                }
            };
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
