using System.Windows;

namespace gironWin
{
    public partial class TextPreviewWindow : Window
    {
        public string EditedText => PreviewTextBox.Text;

        public TextPreviewWindow(string initialText)
        {
            InitializeComponent();
            PreviewTextBox.Text = initialText;
            PreviewTextBox.Focus();
            PreviewTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
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
