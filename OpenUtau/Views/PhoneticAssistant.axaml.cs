using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class PhoneticAssistant : Window {
        PhoneticAssistantViewModel viewModel;
        public PhoneticAssistant() {
            InitializeComponent();
            MacWindow.MakeSeamless(this);
            DataContext = viewModel = new PhoneticAssistantViewModel();
        }

        public void OnCopy(object sender, RoutedEventArgs e) {
            Clipboard?.SetTextAsync(viewModel.Phonemes);
        }
    }
}
