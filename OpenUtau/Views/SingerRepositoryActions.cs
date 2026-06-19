using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;

namespace OpenUtau.App.Views {
    static class SingerRepositoryActions {
        public static async Task InstallAsync(Window? owner, SingerCatalogViewModel vm, SingerCardViewModel card) {
            if (owner == null) {
                return;
            }
            try {
                var confirmTemplate = card.HasUpdate
                    ? ThemeManager.GetString("singercatalog.confirm.update")
                    : ThemeManager.GetString("singercatalog.confirm.install");
                var caption = ThemeManager.GetString("singercatalog.caption");
                var result = await MessageBox.Show(owner,
                    string.Format(confirmTemplate, card.Name), caption,
                    MessageBox.MessageBoxButtons.YesNo);
                if (result != MessageBox.MessageBoxResult.Yes) {
                    return;
                }
                card.Progress = 0;
                card.Busy = true;
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
                    await setup.ShowDialog(owner);
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

        public static void OpenWebsite(SingerCardViewModel card) {
            if (!string.IsNullOrEmpty(card.PageUrl)) {
                OS.OpenWeb(card.PageUrl);
            }
        }
    }
}
