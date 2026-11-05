using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace gironWin
{
    public partial class MainWindow
    {
        private bool _debateCommandPending;
        private bool _isWindowClosing;
        private bool _allowWindowClose;
        private Task _interventionTask = Task.CompletedTask;
        private readonly List<Window> _debateInputWindows = new();

        private void DisableDebateControls()
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            PauseResumeButton.IsEnabled = false;
            InterventionButton.IsEnabled = false;
            ApprovalPanel.Visibility = Visibility.Collapsed;
        }

        private void ApplyStoppedUi()
        {
            if (_isWindowClosing || _webViewsReleased) return;
            StartButton.IsEnabled = !_debateCommandPending;
            StopButton.IsEnabled = false;
            PauseResumeButton.IsEnabled = false;
            PauseResumeButton.Content = "一時停止";
            InterventionButton.IsEnabled = false;
            ApprovalPanel.Visibility = Visibility.Collapsed;
            _lastTurnRecords = _turnRecords.ToList();
            _lastResearchService = _debateService?.ResearchService;
        }

        private async Task StopCurrentDebateAsync()
        {
            var service = _debateService;
            service?.Stop();
            CloseDebateInputWindows();
            if (service != null) await service.Completion;
            // Already-started browser operations are drained, not forcibly killed.
            await _interventionTask;
        }

        private void RunCurrentDebateUi(object? sender, Action action)
        {
            if (_isWindowClosing || _webViewsReleased || Dispatcher.HasShutdownStarted) return;
            Dispatcher.Invoke(() =>
            {
                if (_isWindowClosing || _webViewsReleased
                    || !ReferenceEquals(sender, _debateService)) return;
                action();
            });
        }

        private void TrackDebateInputWindow(Window win, Action? onClosed)
        {
            _debateInputWindows.Add(win);
            win.Closed += (_, _) =>
            {
                _debateInputWindows.Remove(win);
                onClosed?.Invoke();
            };
        }

        private void CloseDebateInputWindows()
        {
            foreach (var win in _debateInputWindows.ToArray()) win.Close();
        }

        private void DetachDebateService()
        {
            if (_debateService != null)
            {
                _debateService.StatusChanged -= DebateService_StatusChanged;
                _debateService.TurnAdvanced -= DebateService_TurnAdvanced;
                _debateService.DebateStopped -= DebateService_DebateStopped;
                _debateService.ThirdSeatInputRequired -= DebateService_ThirdSeatInputRequired;
                _debateService.HumanPriorityInputRequired -= DebateService_HumanPriorityInputRequired;
                _debateService.ResearchTagsExtracted -= DebateService_ResearchTagsExtracted;
            }
            if (_approvalQueue != null)
                _approvalQueue.ApprovalRequested -= ApprovalQueue_ApprovalRequested;
        }
    }
}
