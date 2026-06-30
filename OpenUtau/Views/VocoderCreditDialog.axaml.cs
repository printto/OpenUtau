using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views {
    public partial class VocoderCreditDialog : Window {
        public VocoderCreditDialog() {
            InitializeComponent();
        }

        void OnOk(object? sender, RoutedEventArgs e) {
            if (DontShowAgain.IsChecked == true) {
                Preferences.Default.ShownPotluckVocoderCredit = true;
                Preferences.Save();
            }
            Close();
        }
    }
}
