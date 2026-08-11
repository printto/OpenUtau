using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class MainWindow {
        private NativeMenu? nativeMenu;

        private bool PianoRollFocused =>
            pianoRoll != null &&
            (pianoRollWindow != null ? pianoRollWindow.IsActive : PianoRollContainer.IsKeyboardFocusWithin);

        private bool PianoRollMenusEnabled =>
            pianoRoll != null && (pianoRollWindow == null || pianoRollWindow.IsActive);

        private void InstallNativeMenu() {
            if (!OS.IsMacOS()) {
                return;
            }
            MainMenu.IsVisible = false;

            nativeMenu = new NativeMenu();
            nativeMenu.Opening += (sender, args) => RebuildNativeMenu();
            MacMenu.MenuActionCompleted = () => RebuildNativeMenu(false);
            Activated += (sender, args) => RebuildNativeMenu(true);
            viewModel.PropertyChanged += (sender, args) => {
                if (args.PropertyName != null && MenuRelevantProperties.Contains(args.PropertyName)) {
                    InvalidateNativeMenu();
                }
            };
            RebuildNativeMenu();
            NativeMenu.SetMenu(this, nativeMenu);
        }

        private void AttachNativeMenu(Window? window) {
            if (!OS.IsMacOS() || nativeMenu == null || window == null) {
                return;
            }
            NativeMenu.SetMenu(window, nativeMenu);
            window.Activated += (sender, args) => InvalidateNativeMenu();
            window.Deactivated += (sender, args) => InvalidateNativeMenu();
        }

        private static readonly HashSet<string> MenuRelevantProperties = new() {
            nameof(MainWindowViewModel.Page),
            nameof(MainWindowViewModel.CanUndo),
            nameof(MainWindowViewModel.CanRedo),
            nameof(MainWindowViewModel.UndoText),
            nameof(MainWindowViewModel.RedoText),
        };

        private DateTime lastMenuDataRefresh = DateTime.MinValue;
        private bool nativeMenuDirty;

        /// Coalesces bursts of state changes into one rebuild on the next dispatcher pass.
        internal void InvalidateNativeMenu() {
            if (nativeMenu == null || nativeMenuDirty) {
                return;
            }
            nativeMenuDirty = true;
            Dispatcher.UIThread.Post(() => {
                nativeMenuDirty = false;
                RebuildNativeMenu(false);
            }, DispatcherPriority.Background);
        }

        private void RebuildNativeMenu() => RebuildNativeMenu(true);

        private void RebuildNativeMenu(bool refreshData) {
            if (nativeMenu == null) {
                return;
            }
            if (refreshData && DateTime.UtcNow - lastMenuDataRefresh > TimeSpan.FromSeconds(5)) {
                lastMenuDataRefresh = DateTime.UtcNow;
                viewModel.RefreshOpenRecent();
                viewModel.RefreshTemplates();
                viewModel.RefreshCacheSize();
            }

            nativeMenu.Items.Clear();
            nativeMenu.Add(BuildFileMenu());
            nativeMenu.Add(BuildEditMenu());
            nativeMenu.Add(BuildNoteEditMenu());
            foreach (var item in BuildPianoRollMenus()) {
                nativeMenu.Add(item);
            }
            nativeMenu.Add(BuildProjectMenu());
            nativeMenu.Add(BuildToolsMenu());
            nativeMenu.Add(BuildHelpMenu());
        }

        private NativeMenuItem BuildFileMenu() {
            bool open = viewModel.ProjectOpen;
            var exportAudio = MacMenu.SubMenu(MacMenu.Str("menu.file.exportaudio"),
                MacMenu.Item(MacMenu.Str("menu.file.exportwav"),
                    () => OnMenuExportWav(this, new RoutedEventArgs()), null, open),
                MacMenu.Item(MacMenu.Str("menu.file.exportwavto"),
                    () => OnMenuExportWavTo(this, new RoutedEventArgs()), null, open),
                MacMenu.Item(MacMenu.Str("menu.file.exportmixdown"),
                    () => OnMenuExportMixdown(this, new RoutedEventArgs()), null, open));
            exportAudio.IsEnabled = open;
            var exportProject = MacMenu.SubMenu(MacMenu.Str("menu.file.exportproject"),
                MacMenu.Item(MacMenu.Str("menu.file.exportust"),
                    () => OnMenuExportUst(this, new RoutedEventArgs()), null, open),
                MacMenu.Item(MacMenu.Str("menu.file.exportustto"),
                    () => OnMenuExportUstTo(this, new RoutedEventArgs()), null, open),
                MacMenu.Item(MacMenu.Str("menu.file.exportmidi"),
                    () => OnMenuExportMidi(this, new RoutedEventArgs()), null, open),
                MacMenu.Item(MacMenu.Str("menu.file.exportds"),
                    () => OnMenuExportDsTo(this, new RoutedEventArgs()), null, open),
                MacMenu.Item(MacMenu.Str("menu.file.exportpmws"),
                    () => OnMenuExportPmws(this, new RoutedEventArgs()), null, open));
            exportProject.IsEnabled = open;
            return MacMenu.SubMenu(MacMenu.Str("menu.file"),
                MacMenu.Item(MacMenu.Str("menu.file.new"),
                    () => OnMenuNew(this, new RoutedEventArgs()), MacMenu.Cmd(Key.N)),
                MacMenu.SubMenu(MacMenu.Str("menu.file.newfromtemplate"), viewModel.OpenTemplatesMenuItems),
                MacMenu.Item(MacMenu.Str("menu.file.savetemplate"),
                    () => OnMenuSaveTemplate(this, new RoutedEventArgs()), null, open),
                MacMenu.Separator(),
                MacMenu.Item(MacMenu.Str("menu.file.open"),
                    () => OnMenuOpen(this, new RoutedEventArgs()), MacMenu.Cmd(Key.O)),
                MacMenu.SubMenu(MacMenu.Str("menu.file.openrecent"), viewModel.OpenRecentMenuItems),
                MacMenu.Item(MacMenu.Str("menu.file.opensharecode"),
                    () => OnMenuOpenShareCode(this, new RoutedEventArgs())),
                MacMenu.Separator(),
                MacMenu.Item(MacMenu.Str("menu.file.save"),
                    () => OnMenuSave(this, new RoutedEventArgs()), MacMenu.Cmd(Key.S), open),
                MacMenu.Item(MacMenu.Str("menu.file.saveas"),
                    () => OnMenuSaveAs(this, new RoutedEventArgs()), MacMenu.Cmd(Key.S, KeyModifiers.Shift), open),
                MacMenu.Item(MacMenu.Str("menu.file.sharetodevice"),
                    () => OnMenuShareToDevice(this, new RoutedEventArgs()), null, open),
                MacMenu.Separator(),
                MacMenu.Item(MacMenu.Str("menu.file.importtracks"),
                    () => OnMenuImportTracks(this, new RoutedEventArgs()), null, open),
                MacMenu.Item(MacMenu.Str("menu.file.importaudio"),
                    () => OnMenuImportAudio(this, new RoutedEventArgs()), null, open),
                MacMenu.Separator(),
                exportAudio,
                exportProject,
                MacMenu.Item(MacMenu.Str("menu.file.openexportlocation"),
                    () => OnMenuOpenProjectLocation(this, new RoutedEventArgs()), null, open),
                MacMenu.Separator(),
                MacMenu.Item(MacMenu.Str("menu.file.sendtowebsynth"),
                    () => OnMenuSendToWebSynth(this, new RoutedEventArgs()), null, open));
        }

        private NativeMenuItem BuildEditMenu() {
            var menu = MacMenu.SubMenu(MacMenu.Str("menu.edit"),
                MacMenu.Item(viewModel.UndoText, () => viewModel.Undo(),
                    MacMenu.Cmd(Key.Z), viewModel.CanUndo),
                MacMenu.Item(viewModel.RedoText, () => viewModel.Redo(),
                    MacMenu.Cmd(Key.Y), viewModel.CanRedo));
            menu.IsEnabled = viewModel.ProjectOpen;
            return menu;
        }

        private NativeMenuItem BuildNoteEditMenu() {
            var items = new List<NativeMenuItemBase> {
                MacMenu.Item(MacMenu.Str("menu.edit.cut"), Cut, MacMenu.Cmd(Key.X)),
                MacMenu.Item(MacMenu.Str("menu.edit.copy"), Copy, MacMenu.Cmd(Key.C)),
                MacMenu.Item(MacMenu.Str("menu.edit.paste"), Paste, MacMenu.Cmd(Key.V)),
                MacMenu.Item(MacMenu.Str("menu.edit.pasteplainnotes"),
                    () => pianoRoll?.ViewModel.PastePlain(), MacMenu.Cmd(Key.V, KeyModifiers.Shift)),
                MacMenu.Item(MacMenu.Str("menu.edit.delete"), Delete, MacMenu.Plain(Key.Delete)),
                MacMenu.Separator(),
                MacMenu.Item(MacMenu.Str("menu.edit.selectall"), SelectAll, MacMenu.Cmd(Key.A)),
            };
            if (pianoRoll != null) {
                items.AddRange(pianoRoll.BuildNoteEditTail());
            }
            return MacMenu.SubMenu(MacMenu.Str("menu.noteedit"), items);
        }

        private List<NativeMenuItem> BuildPianoRollMenus() {
            if (pianoRoll == null) {
                return new List<NativeMenuItem>();
            }
            var menus = pianoRoll.BuildViewAndBatchMenus();
            foreach (var menu in menus) {
                menu.IsEnabled = PianoRollMenusEnabled;
            }
            return menus;
        }

        private NativeMenuItem BuildProjectMenu() {
            bool open = viewModel.ProjectOpen;
            var menu = MacMenu.SubMenu(MacMenu.Str("menu.project"),
                MacMenu.Item(MacMenu.Str("menu.project.expressions"),
                    () => OnMenuExpressionss(this, new RoutedEventArgs()), null, open),
                MacMenu.Item(MacMenu.Str("menu.project.remaptimeaxis"),
                    () => OnMenuRemapTimeaxis(this, new RoutedEventArgs()), null, open));
            menu.IsEnabled = viewModel.ProjectOpen;
            return menu;
        }

        private NativeMenuItem BuildToolsMenu() {
            return MacMenu.SubMenu(MacMenu.Str("menu.tools"),
                MacMenu.SubMenu(MacMenu.Str("menu.tools.layout"),
                    MacMenu.Item(MacMenu.Str("menu.tools.layout.reset"),
                        () => OnMenuLayoutReset(this, new RoutedEventArgs())),
                    MacMenu.Item(MacMenu.Str("menu.tools.layout.vsplit11"),
                        () => OnMenuLayoutVSplit11(this, new RoutedEventArgs())),
                    MacMenu.Item(MacMenu.Str("menu.tools.layout.vsplit12"),
                        () => OnMenuLayoutVSplit12(this, new RoutedEventArgs())),
                    MacMenu.Item(MacMenu.Str("menu.tools.layout.vsplit13"),
                        () => OnMenuLayoutVSplit13(this, new RoutedEventArgs())),
                    MacMenu.Item(MacMenu.Str("menu.tools.layout.hsplit11"),
                        () => OnMenuLayoutHSplit11(this, new RoutedEventArgs())),
                    MacMenu.Item(MacMenu.Str("menu.tools.layout.hsplit12"),
                        () => OnMenuLayoutHSplit12(this, new RoutedEventArgs())),
                    MacMenu.Item(MacMenu.Str("menu.tools.layout.hsplit13"),
                        () => OnMenuLayoutHSplit13(this, new RoutedEventArgs()))),
                MacMenu.Item(MacMenu.Str("menu.tools.fullscreen"), ToggleFullScreen, MacMenu.Plain(Key.F11)),
                MacMenu.Item(viewModel.ClearCacheHeader,
                    () => OnMenuClearCache(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.tools.debugwindow"),
                    () => OnMenuDebugWindow(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("phoneticassistant.caption"),
                    () => OnMenuPhoneticAssistant(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.tools.singer.install"),
                    () => OnMenuInstallSinger(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.tools.singer.catalog"),
                    () => OnMenuSingerCatalog(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.tools.singers"),
                    () => OnMenuSingers(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.tools.wavtoolresampler.install"),
                    () => OnMenuInstallWavtoolResampler(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.tools.packages"),
                    () => OnMenuPackageManager(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.tools.prefs"),
                    () => OnMenuPreferences(this, new RoutedEventArgs()), MacMenu.Cmd(Key.OemComma)));
        }

        private NativeMenuItem BuildHelpMenu() {
            return MacMenu.SubMenu(MacMenu.Str("menu.help"),
                MacMenu.Item(MacMenu.Str("menu.help.wiki"),
                    () => OnMenuWiki(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.help.checkupdate"),
                    () => OnMenuCheckUpdate(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.help.reportissue"),
                    () => OnMenuReportIssue(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("menu.help.logslocation"),
                    () => OnMenuLogsLocation(this, new RoutedEventArgs())));
        }

        private void Cut() {
            if (PianoRollFocused) {
                pianoRoll!.ViewModel.Cut();
            } else {
                viewModel.TracksViewModel.CutParts();
            }
        }

        private void Copy() {
            if (PianoRollFocused) {
                pianoRoll!.ViewModel.Copy();
            } else {
                viewModel.TracksViewModel.CopyParts();
            }
        }

        private void Paste() {
            if (PianoRollFocused) {
                pianoRoll!.ViewModel.Paste();
            } else {
                viewModel.TracksViewModel.PasteParts();
            }
        }

        private void Delete() {
            if (PianoRollFocused) {
                pianoRoll!.ViewModel.Delete();
            } else {
                viewModel.TracksViewModel.DeleteSelectedParts();
            }
        }

        private void SelectAll() {
            if (PianoRollFocused) {
                pianoRoll!.ViewModel.SelectAll();
            } else {
                viewModel.TracksViewModel.SelectAllParts();
            }
        }

        private void ToggleFullScreen() {
            if (PianoRollFocused) {
                pianoRoll!.ToggleFullScreenFromMenu();
            } else {
                OnMenuFullScreen(this, new RoutedEventArgs());
            }
        }
    }
}
