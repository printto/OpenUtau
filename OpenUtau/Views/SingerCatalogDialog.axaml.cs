using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;

namespace OpenUtau.App.Views {
    public partial class SingerCatalogDialog : Window {
        public SingerCatalogDialog() {
            InitializeComponent();
        }

        void OnOpenWebClick(object sender, RoutedEventArgs e) {
            if (sender is Button button
                && button.DataContext is SingerCardViewModel card
                && !string.IsNullOrEmpty(card.PageUrl)) {
                OS.OpenWeb(card.PageUrl);
            }
        }

        async void OnInstallClick(object sender, RoutedEventArgs e) {
            if (DataContext is not SingerCatalogViewModel vm
                || sender is not Button button
                || button.DataContext is not SingerCardViewModel card) {
                return;
            }
            try {
                card.Busy = true;
                var confirmTemplate = card.HasUpdate
                    ? ThemeManager.GetString("singercatalog.confirm.update")
                    : ThemeManager.GetString("singercatalog.confirm.install");
                var caption = ThemeManager.GetString("singercatalog.caption");
                var result = await MessageBox.Show(this,
                    string.Format(confirmTemplate, card.Name), caption,
                    MessageBox.MessageBoxButtons.YesNo);
                if (result != MessageBox.MessageBoxResult.Yes) {
                    return;
                }

                var tempPath = await vm.DownloadAsync(card);
                if (string.IsNullOrEmpty(tempPath)) {
                    return;
                }

                if (vm.NeedsWizard(card)) {
                    var setup = new SingerSetupDialog() {
                        DataContext = new SingerSetupViewModel() {
                            ArchiveFilePath = tempPath,
                        },
                    };
                    await setup.ShowDialog(this);
                    vm.MarkInstalled(card);
                } else {
                    await vm.InstallSilentAsync(card, tempPath);
                }
            } catch (Exception ex) {
                vm.ReportError(ex);
            } finally {
                card.Busy = false;
            }
        }
    }
}
