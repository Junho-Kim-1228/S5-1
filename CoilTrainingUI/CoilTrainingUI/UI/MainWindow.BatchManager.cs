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
            string projectRoot = FindProjectRoot("capstone_design");
            string inboxRoot = GetTrainingInboxRoot();
            string? preferredImagePath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;

            var window = new BatchManagerWindow(inboxRoot, projectRoot, _batchMergeService)
            {
                Owner = this
            };
            _openBatchManager = window;
            bool refreshedWhileOpen = false;
            window.LibraryChanged += (sender, args) =>
            {
                refreshedWhileOpen = true;
                string? currentImagePath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;
                RefreshAllImagesFromTrainingInbox(
                    preferredImagePath: currentImagePath,
                    preferredBatchRoot: args.PreferredBatchRoot);
            };

            try
            {
                window.ShowDialog();
            }
            finally
            {
                _openBatchManager = null;
            }

            if (window.HasLibraryChanges && !refreshedWhileOpen)
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
