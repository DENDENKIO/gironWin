using System.Windows;

namespace gironWin
{
    public enum InterventionTarget { Left, Right, Both }

    public partial class InterventionWindow : Window
    {
        public bool   ShouldSend   { get; private set; }
        public string Text         { get; private set; } = string.Empty;
        public InterventionTarget Target { get; private set; } = InterventionTarget.Left;

        public InterventionWindow(string initialText = "")
        {
            InitializeComponent();
            InterventionTextBox.Text = initialText;
        }

        private void SendAndResumeButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldSend   = true;
            Text         = InterventionTextBox.Text;
            Target       = SendRightRadio.IsChecked == true ? InterventionTarget.Right
                         : SendBothRadio.IsChecked  == true ? InterventionTarget.Both
                         : InterventionTarget.Left;
            DialogResult = true;
            Close();
        }

        private void ResumeOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldSend   = false;
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
