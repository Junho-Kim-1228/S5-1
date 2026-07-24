using System;
using System.Windows;

namespace CoilTrainingUI
{
    public partial class OperationProgressWindow : Window
    {
        public OperationProgressWindow(string title)
        {
            InitializeComponent();
            Title = title;
        }

        public void UpdateProgress(
            int percent,
            string status,
            bool isIndeterminate = false,
            string? detail = null)
        {
            RunOnUi(() =>
            {
                int clamped = Math.Max(0, Math.Min(100, percent));
                StatusTextBlock.Text = string.IsNullOrWhiteSpace(status) ? "작업 중..." : status;
                OperationProgressBar.IsIndeterminate = isIndeterminate;
                if (!isIndeterminate)
                    OperationProgressBar.Value = clamped;
                ProgressDetailTextBlock.Text = detail ?? "";
                ProgressDetailTextBlock.Visibility = string.IsNullOrWhiteSpace(detail)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                PercentTextBlock.Text = isIndeterminate ? "진행 중..." : $"{clamped}%";
            });
        }

        public void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            RunOnUi(() =>
            {
                LogTextBox.AppendText(line + Environment.NewLine);
                LogTextBox.ScrollToEnd();
            });
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.BeginInvoke(action);
        }
    }
}
