using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class PresetSelectorWindow : Window
    {
        public DebatePreset? SelectedPreset { get; private set; }

        private static readonly List<DebatePreset> _presets = new()
        {
            new DebatePreset
            {
                Name        = "\u5efa\u8a2d\u7684\u8a0e\u8ad6",
                TurnPolicy  = TurnPolicy.CritiqueThenRefine,
                ResearchMode = false,
                Description = "\u5de6\u5e2d\u304c\u63d0\u6848\u3057\u3001\u53f3\u5e2d\u304c\u6279\u5224\u30fb\u6539\u5584\u3059\u308b\u3002\u53f8\u4f1a\u306f\u30e6\u30fc\u30b6\u30fc\u3002",
                LeftPrompt  = "\u3042\u306a\u305f\u306f\u63d0\u6848\u5f79\u3067\u3059\u3002\u5177\u4f53\u7684\u3067\u5b9f\u73fe\u53ef\u80fd\u306a\u6848\u3092\u63d0\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                RightPrompt = "\u3042\u306a\u305f\u306f\u6279\u5224\u5f79\u3067\u3059\u3002\u8ad6\u7406\u7684\u306a\u5f31\u70b9\u3068\u6539\u5584\u6848\u3092\u6307\u6458\u3057\u3066\u304f\u3060\u3055\u3044\u3002"
            },
            new DebatePreset
            {
                Name        = "\u30a2\u30d7\u30ea\u8a2d\u8a08\u30ec\u30d3\u30e5\u30fc",
                TurnPolicy  = TurnPolicy.CritiqueThenRefine,
                ResearchMode = false,
                Description = "\u5de6\u5e2d\u304c\u5b9f\u88c5\u6848\u3092\u63d0\u793a\u3057\u3001\u53f3\u5e2d\u304c\u30a2\u30fc\u30ad\u30c6\u30af\u30c1\u30e3\u3068\u30ea\u30b9\u30af\u3092\u5be9\u67fb\u3059\u308b\u3002",
                LeftPrompt  = "\u3042\u306a\u305f\u306f\u5b9f\u88c5\u62c5\u5f53AI\u3067\u3059\u3002\u5177\u4f53\u7684\u306a\u5b9f\u88c5\u6848\u3068\u30b3\u30fc\u30c9\u3092\u63d0\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                RightPrompt = "\u3042\u306a\u305f\u306f\u30a2\u30fc\u30ad\u30c6\u30af\u30c8\u517c\u30ea\u30b9\u30af\u6307\u6458AI\u3067\u3059\u3002\u8a2d\u8a08\u4e0a\u306e\u554f\u984c\u3068\u30ea\u30b9\u30af\u3092\u6307\u6458\u3057\u3066\u304f\u3060\u3055\u3044\u3002"
            },
            new DebatePreset
            {
                Name        = "\u6570\u5b66\u7814\u7a76",
                TurnPolicy  = TurnPolicy.ResearchReviewLoop,
                ResearchMode = true,
                Description = "\u5de6\u5e2d\u304c\u8a3c\u660e\u6848\u3092\u63d0\u793a\u3057\u3001\u53f3\u5e2d\u304c\u53cd\u4f8b\u3068\u8ad6\u7406\u306e\u7a74\u3092\u63a2\u3059\u3002",
                LeftPrompt  = "\u3042\u306a\u305f\u306f\u8a3c\u660e\u62c5\u5f53AI\u3067\u3059\u3002\u547d\u984c\u3078\u306e\u8a3c\u660e\u65b9\u91dd\u3068\u88dc\u984c\u5019\u88dc\u3092\u63d0\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                RightPrompt = "\u3042\u306a\u305f\u306f\u53cd\u4f8b\u63a2\u7d22AI\u3067\u3059\u3002\u53cd\u4f8b\u5019\u88dc\u30fb\u8ad6\u7406\u306e\u7a74\u30fb\u672a\u691c\u8a3c\u7b87\u6240\u3092\u6307\u6458\u3057\u3066\u304f\u3060\u3055\u3044\u3002"
            },
            new DebatePreset
            {
                Name        = "\u67fb\u8aad\u30e2\u30fc\u30c9",
                TurnPolicy  = TurnPolicy.ModeratorSelect,
                ResearchMode = false,
                Description = "\u6587\u66f8\u30fb\u8ad6\u6587\u30fb\u4ed5\u69d8\u66f8\u306b\u5bfe\u3057\u3066\u6279\u5224\u7684\u67fb\u8aad\u3068\u6539\u5584\u63d0\u6848\u3092\u884c\u3046\u3002",
                LeftPrompt  = "\u3042\u306a\u305f\u306f\u67fb\u8aad\u8005AI\u3067\u3059\u3002\u8ad6\u7406\u306e\u6b20\u9665\u30fb\u6839\u62e0\u4e0d\u8db3\u30fb\u66d6\u6627\u306a\u8a18\u8ff0\u3092\u6307\u6458\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                RightPrompt = "\u3042\u306a\u305f\u306f\u6539\u5584\u8005AI\u3067\u3059\u3002\u67fb\u8aad\u30b3\u30e1\u30f3\u30c8\u3092\u53d7\u3051\u3066\u5177\u4f53\u7684\u306a\u6539\u5584\u6848\u3092\u63d0\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002"
            },
            new DebatePreset
            {
                Name        = "\u81ea\u7531\u8a0e\u8ad6 (RoundRobin)",
                TurnPolicy  = TurnPolicy.RoundRobin,
                ResearchMode = false,
                Description = "\u30c6\u30fc\u30de\u3092\u81ea\u7531\u306b\u8a0e\u8ad6\u3059\u308b\u3002\u30bf\u30fc\u30f3\u5236\u3067\u5747\u7b49\u306b\u767a\u8a00\u3059\u308b\u3002",
                LeftPrompt  = string.Empty,
                RightPrompt = string.Empty
            }
        };

        public PresetSelectorWindow()
        {
            InitializeComponent();
            PresetListBox.ItemsSource = _presets;
        }

        private void PresetListBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            SelectedPreset = PresetListBox.SelectedItem as DebatePreset;
            ApplyButton.IsEnabled = SelectedPreset != null;
            DescriptionLabel.Text = SelectedPreset?.Description ?? string.Empty;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == null) return;
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
