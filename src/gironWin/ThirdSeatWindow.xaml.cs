using System.Windows;

namespace gironWin
{
    public partial class ThirdSeatWindow : Window
    {
        private readonly ThirdSeatInputRequest _request;

        public ThirdSeatWindow(ThirdSeatInputRequest request)
        {
            InitializeComponent();
            _request = request;

            TurnLabel.Text   = request.TurnNumber.ToString();
            RoleLabel.Text   = request.Role;
            ContextLabel.Text = string.IsNullOrWhiteSpace(request.Context)
                ? "(\u30b3\u30f3\u30c6\u30ad\u30b9\u30c8\u306a\u3057)"
                : request.Context;

            InputTextBox.Focus();
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            string text = InputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            _request.OnSubmit?.Invoke(text);
            Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            _request.OnSubmit?.Invoke(string.Empty);
            Close();
        }
    }
}
