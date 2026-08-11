using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using QRCoder;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class ShareCodeDialog : Window {
        public UProject? OpenedProject { get; private set; }

        public enum StartMode {
            Choose,   // show both choices
            Share,    // upload immediately and show the code
        }

        UProject? project;
        StartMode startMode = StartMode.Choose;
        string url = string.Empty;
        string code = string.Empty;
        bool busy;

        public ShareCodeDialog() {
            InitializeComponent();
            MacWindow.MakeSeamless(this);
        }

        public void Init(UProject project, StartMode mode) {
            this.project = project;
            startMode = mode;
        }

        protected override void OnOpened(EventArgs e) {
            base.OnOpened(e);
            bool canShare = project != null && Pmws.VoiceParts(project).Count > 0;
            if (!canShare) {
                SharePrompt.IsVisible = false;
                DividerRow.IsVisible = false;
                CodeInput.IsVisible = true;
                CodeInput.Focus();
                return;
            }
            if (startMode == StartMode.Share) {
                OnShareClicked(this, new RoutedEventArgs());
            }
        }

        void SetStatus(string text) {
            StatusText.Text = text;
            StatusText.IsVisible = !string.IsNullOrEmpty(text);
        }

        async void OnShareClicked(object? sender, RoutedEventArgs e) {
            if (busy || project == null) {
                return;
            }
            if (Pmws.VoiceParts(project).Count == 0) {
                SetStatus(ThemeManager.GetString("dialogs.pmws.notracks"));
                return;
            }
            try {
                busy = true;
                ShareButton.IsEnabled = false;
                SetStatus(ThemeManager.GetString("dialogs.sharecode.uploading"));
                var result = await PmwsLink.ShareAsync(project);
                url = result.Url;
                code = result.Code;
                CodeText.Text = result.Code;
                ExpiryText.Text = string.Format(
                    ThemeManager.GetString("dialogs.sharecode.expiry"), result.ExpiresInHours);
                QrImage.Source = RenderQr(result.Url);
                ResultPanel.IsVisible = true;
                ResultActions.IsVisible = true;
                SharePrompt.IsVisible = false;
                SetStatus(string.Empty);
            } catch (Exception ex) {
                Log.Error(ex, "Failed to create share code");
                SetStatus(ex.Message);
                ShareButton.IsEnabled = true;
            } finally {
                busy = false;
            }
        }

        async void OnOpenCodeClicked(object? sender, RoutedEventArgs e) {
            if (busy) {
                return;
            }
            if (!CodeInput.IsVisible) {
                CodeInput.IsVisible = true;
                CodeInput.Focus();
                return;
            }
            string code = (CodeInput.Text ?? string.Empty).Trim();
            if (code.Length == 0) {
                CodeInput.Focus();
                return;
            }
            try {
                busy = true;
                OpenCodeButton.IsEnabled = false;
                SetStatus(ThemeManager.GetString("dialogs.opensharecode.fetching"));
                OpenedProject = await PmwsLink.OpenShareAsync(code);
                Close();
            } catch (Exception ex) {
                Log.Error(ex, "Failed to open share code");
                SetStatus(ex.Message);
                OpenCodeButton.IsEnabled = true;
            } finally {
                busy = false;
            }
        }

        static Bitmap? RenderQr(string text) {
            try {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
                return new Bitmap(new MemoryStream(new PngByteQRCode(data).GetGraphic(10)));
            } catch (Exception ex) {
                Log.Error(ex, "Failed to render QR code");
                return null;
            }
        }

        void OnOpenWebClicked(object? sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(url)) {
                SetStatus(ThemeManager.GetString("dialogs.sharecode.failed"));
                return;
            }
            try {
                // OS.OpenWeb is unreliable here since this url has a query string.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            } catch (Exception ex) {
                Log.Error(ex, $"Failed to open {url}");
                try {
                    OS.OpenWeb(url);
                } catch (Exception fallbackEx) {
                    Log.Error(fallbackEx, "Fallback opener failed too");
                    SetStatus(ex.Message);
                }
            }
        }

        async void OnCopyClicked(object? sender, RoutedEventArgs e) {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(code)) {
                await clipboard.SetTextAsync(code);
                SetStatus(ThemeManager.GetString("dialogs.sharecode.copied"));
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                Close();
            } else if (e.Key == Key.Return && CodeInput.IsVisible && CodeInput.IsFocused) {
                e.Handled = true;
                OnOpenCodeClicked(this, new RoutedEventArgs());
            } else {
                base.OnKeyDown(e);
            }
        }
    }
}
