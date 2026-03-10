using CoilTrainingUI.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private async void BatchManagement_Click(object sender, RoutedEventArgs e)
        {
            string inboxRoot = GetTrainingInboxRoot();
            string? preferredImagePath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;

            var window = new BatchManagerWindow(inboxRoot, _batchMergeService)
            {
                Owner = this
            };

            window.ShowDialog();

            if (window.HasLibraryChanges)
            {
                RefreshAllImagesFromTrainingInbox(
                    preferredImagePath: preferredImagePath,
                    preferredBatchRoot: window.PreferredBatchRoot);
            }

            if (window.RequestedAction == BatchManagerRequestedAction.TrainSelected &&
                window.RequestedBatches.Count > 0)
            {
                await TrainSelectedBatchesAsync(window.RequestedBatches.ToList());
            }
        }
    }
}
