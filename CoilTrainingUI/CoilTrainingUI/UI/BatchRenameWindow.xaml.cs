using System.Windows;

namespace CoilTrainingUI
{
    public partial class BatchRenameWindow : Window
    {
        public string BatchName => BatchNameTextBox.Text.Trim();

        public BatchRenameWindow(string currentBatchName)
        {
            InitializeComponent();
            BatchNameTextBox.Text = currentBatchName ?? string.Empty;

            Loaded += (_, _) =>
            {
                BatchNameTextBox.Focus();
                BatchNameTextBox.SelectAll();
            };
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(BatchName))
            {
                MessageBox.Show(
                    "배치명은 비워둘 수 없습니다.",
                    "배치명 변경",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }
    }
}
