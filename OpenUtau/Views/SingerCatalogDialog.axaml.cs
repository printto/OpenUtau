using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class SingerCatalogDialog : Window {
        public SingerCatalogDialog() {
            InitializeComponent();
        }

        void OnBackgroundPointerPressed(object sender, PointerPressedEventArgs e) {
            // Clicking outside the search box drops its focus.
            if (e.Source is Control control && control.FindAncestorOfType<TextBox>() != null) {
                return;
            }
            if (sender is Control root) {
                root.Focus();
            }
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
