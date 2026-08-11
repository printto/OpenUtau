using Avalonia.Controls;
using Avalonia.Platform;

namespace OpenUtau.App.Views {
    internal static class MacWindow {
        public static void MakeSeamless(Window window) {
            if (!OS.IsMacOS()) {
                return;
            }
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            window.ExtendClientAreaTitleBarHeightHint = -1;
        }
    }
}
