using System;
using System.Windows;
using System.Windows.Threading;

namespace gironWin
{
    public partial class HumanPriorityWindow : Window
    {
        private readonly HumanPriorityInputRequest _req;
        private readonly DispatcherTimer _timer;
        private int _remainingMs;
        private bool _handled;

        public HumanPriorityWindow(HumanPriorityInputRequest req)
        {
            InitializeComponent();
            _req         = req;
            _remainingMs = req.TimeoutMs;

            SummaryBox.Text = string.IsNullOrWhiteSpace(req.Summary)
                ? "（サマリーなし）"
                : req.Summary;

            TimerLabel.Text = $"残り {_remainingMs / 1000}秒";

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            InputBox.Focus();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _remainingMs -= 200;
            double pct = Math.Max(0, (double)_remainingMs / _req.TimeoutMs * 100);
            TimerBar.Value  = pct;
            TimerLabel.Text = $"残り {Math.Max(0, _remainingMs / 1000)}秒";

            if (_remainingMs <= 0)
                Skip();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_handled) return;
            _handled = true;
            _timer.Stop();
            string text = InputBox.Text.Trim();
            _req.OnInputReady?.Invoke(string.IsNullOrWhiteSpace(text) ? null : text);
            Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e) => Skip();

        private void Skip()
        {
            if (_handled) return;
            _handled = true;
            _timer.Stop();
            _req.OnInputReady?.Invoke(null);
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            if (!_handled)
                _req.OnInputReady?.Invoke(null);
            base.OnClosed(e);
        }
    }
}
