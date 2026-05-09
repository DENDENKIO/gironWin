using System;
using System.Windows;

namespace gironWin
{
    public partial class ThirdSeatWindow : Window
    {
        private readonly Action<string?> _onInputReady;

        public ThirdSeatWindow(ThirdSeatInputRequest request)
        {
            InitializeComponent();
            _onInputReady = request.OnInputReady ?? (_ => { });

            RoleTitleTextBlock.Text = $"第3席: {request.DisplayName} ({request.Role})";
            SummaryTextBox.Text    = request.Summary;
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            _onInputReady(InputTextBox.Text);
            Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            _onInputReady(null);
            Close();
        }
    }
}
