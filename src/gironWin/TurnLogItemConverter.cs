using System;
using System.Globalization;
using System.Windows.Data;

namespace gironWin
{
    /// <summary>
    /// ログテキスト内の \n リテラルを実際の改行に変換する ValueConverter。
    /// TurnLogListBox の ItemTemplate で使用。
    /// </summary>
    [ValueConversion(typeof(string), typeof(string))]
    public sealed class TurnLogTextConverter : IValueConverter
    {
        public static readonly TurnLogTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string s) return string.Empty;

            // リテラル \n → 実改行、リテラル \r\n → 実改行
            s = s.Replace("\\r\\n", "\n")
                 .Replace("\\n",    "\n")
                 .Replace("\\t",    "    ");

            // 3行を超える場合は折りたたんで末尾に「…(全N行)」を付与
            // ※ PreviewWindow で全文確認させるため、ここは要約表示
            var lines = s.Split('\n');
            if (lines.Length > 4)
            {
                string preview = string.Join("\n", lines[..4]);
                return $"{preview}\n…（全 {lines.Length} 行 — クリックで全文表示）";
            }
            return s;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// DebateDirection → 席ラベル文字列に変換。
    /// </summary>
    [ValueConversion(typeof(DebateDirection), typeof(string))]
    public sealed class DirectionToLabelConverter : IValueConverter
    {
        public static readonly DirectionToLabelConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is DebateDirection d ? d switch
            {
                DebateDirection.LeftToRight  => "← 左 → 右",
                DebateDirection.RightToLeft  => "← 右 → 左",
                DebateDirection.ThirdToLeft  => "第3 → 左",
                DebateDirection.ThirdToRight => "第3 → 右",
                _                            => d.ToString()
            } : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// DebateDirection → 席バッジ背景色ブラシに変換。
    /// </summary>
    [ValueConversion(typeof(DebateDirection), typeof(System.Windows.Media.SolidColorBrush))]
    public sealed class DirectionToColorBrushConverter : IValueConverter
    {
        public static readonly DirectionToColorBrushConverter Instance = new();

        private static readonly System.Windows.Media.SolidColorBrush Left   =
            new(System.Windows.Media.Color.FromRgb(0x19, 0x76, 0xD2));   // #1976D2
        private static readonly System.Windows.Media.SolidColorBrush Right  =
            new(System.Windows.Media.Color.FromRgb(0x00, 0x83, 0x8F));   // #00838F
        private static readonly System.Windows.Media.SolidColorBrush Third  =
            new(System.Windows.Media.Color.FromRgb(0x7B, 0x1F, 0xA2));   // #7B1FA2

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is DebateDirection d ? d switch
            {
                DebateDirection.LeftToRight  => Left,
                DebateDirection.RightToLeft  => Right,
                _                            => Third
            } : Left;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
