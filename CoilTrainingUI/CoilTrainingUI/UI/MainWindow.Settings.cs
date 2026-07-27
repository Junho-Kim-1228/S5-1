using CoilTrainingUI.Services;
using System;
using System.Windows;

namespace CoilTrainingUI;

public partial class MainWindow
{
    private void TrainingSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string settingsRoot = FindProjectRoot("capstone_design");
            var store = new TrainingSettingsStore(settingsRoot);
            AppSettings settings = store.LoadEffectiveSettings();
            var window = new TrainingSettingsWindow(store, settings) { Owner = this };
            if (window.ShowDialog() == true)
            {
                MessageBox.Show(
                    "학습 설정을 저장했습니다. 다음 학습부터 적용됩니다.",
                    "학습 설정",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "학습 설정을 열 수 없습니다.\n" + ex.Message,
                "학습 설정",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
