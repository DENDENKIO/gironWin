using System;
using System.Windows;

namespace gironWin
{
    public partial class ApprovalWindow : Window
    {
        public bool   IsApproved  { get; private set; }
        public string EditedText  { get; private set; } = string.Empty;

        public ApprovalWindow(ApprovalItem item)
        {
            InitializeComponent();

            DirectionTextBlock.Text  = $"送信: {item.Direction}";
            TimestampTextBlock.Text  = $"作成: {item.CreatedAt:HH:mm:ss}";
            EditTextBox.Text         = item.Text;

            EditTextBox.TextChanged += (_, _) =>
                CharCountTextBlock.Text = $"{EditTextBox.Text.Length} 文字";

            CharCountTextBlock.Text = $"{item.Text.Length} 文字";
        }

        private void ApproveButton_Click(object sender, RoutedEventArgs e)
        {
            IsApproved = true;
            EditedText = EditTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void RejectButton_Click(object sender, RoutedEventArgs e)
        {
            IsApproved = false;
            DialogResult = false;
            Close();
        }
    }
}
