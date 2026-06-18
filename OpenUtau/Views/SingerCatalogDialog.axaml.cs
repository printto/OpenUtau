using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class SingerCatalogDialog : Window {
        public SingerCatalogDialog() {
            InitializeComponent();
        }

        async void OnInstallClick(object sender, RoutedEventArgs e) {
            if (DataContext is SingerCatalogViewModel vm
                && sender is Button button
                && button.DataContext is SingerCardViewModel card) {
                await SingerRepositoryActions.InstallAsync(this, vm, card);
            }
        }

        void OnOpenWebClick(object sender, RoutedEventArgs e) {
            if (sender is Button button && button.DataContext is SingerCardViewModel card) {
                SingerRepositoryActions.OpenWebsite(card);
            }
        }
    }
}
