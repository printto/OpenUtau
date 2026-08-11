using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.Views;

namespace OpenUtau.App.Controls {
    public partial class PianoRoll {
        internal List<NativeMenuItem> BuildViewAndBatchMenus() {
            return new List<NativeMenuItem> {
                BuildViewMenu(),
                BuildBatchMenu(),
            };
        }

        internal void ToggleFullScreenFromMenu() => OnMenuFullScreen(this, new RoutedEventArgs());

        internal List<NativeMenuItemBase> BuildNoteEditTail() {
            return new List<NativeMenuItemBase> {
                MacMenu.Item(MacMenu.Str("pianoroll.menu.searchnote"),
                    () => OnMenuSearchNote(this, new RoutedEventArgs()),
                    MacMenu.Cmd(Key.F)),
                MacMenu.Separator(),
                MacMenu.Item(MacMenu.Str("pianoroll.menu.part.singer"),
                    () => OnMenuSingers(this, new RoutedEventArgs())),
                MacMenu.SubMenu(MacMenu.Str("menu.edit.lockunselectednotes"),
                    MacMenu.Toggle(MacMenu.Str("menu.edit.lockunselectednotes.pitchpoints"),
                        ViewModel.LockPitchPoints,
                        () => OnMenuLockPitchPoints(this, new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("menu.edit.lockunselectednotes.vibrato"),
                        ViewModel.LockVibrato,
                        () => OnMenuLockVibrato(this, new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("menu.edit.lockunselectednotes.expressions"),
                        ViewModel.LockExpressions,
                        () => OnMenuLockExpressions(this, new RoutedEventArgs()))),
                MacMenu.Separator(),
                MacMenu.Item(MacMenu.Str("pianoroll.menu.lyrics.edit"),
                    () => OnMenuEditLyrics(this, new RoutedEventArgs())),
                MacMenu.Item(MacMenu.Str("pianoroll.menu.notedefaults"),
                    () => OnMenuNoteDefaults(this, new RoutedEventArgs())),
            };
        }

        private NativeMenuItem BuildViewMenu() {
            return MacMenu.SubMenu(MacMenu.Str("menu.view"),
                MacMenu.Toggle(MacMenu.Str("prefs.appearance.showportrait"),
                    ViewModel.ShowPortrait, () => OnMenuShowPortrait(this, new RoutedEventArgs())),
                MacMenu.Toggle(MacMenu.Str("prefs.appearance.showicon"),
                    ViewModel.ShowIcon, () => OnMenuShowIcon(this, new RoutedEventArgs())),
                MacMenu.Toggle(MacMenu.Str("prefs.appearance.showghostnotes"),
                    ViewModel.ShowGhostNotes, () => OnMenuShowGhostNotes(this, new RoutedEventArgs())),
                MacMenu.Toggle(MacMenu.Str("prefs.appearance.trackcolor"),
                    ViewModel.UseTrackColor, () => OnMenuUseTrackColor(this, new RoutedEventArgs())),
                MacMenu.Separator(),
                MacMenu.SubMenu(MacMenu.Str("prefs.appearance.degree"),
                    MacMenu.Toggle(MacMenu.Str("prefs.appearance.degree.off"),
                        ViewModel.DegreeStyle0,
                        () => OnMenuDegreeStyle(MacMenu.TagSender("0"), new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("prefs.appearance.degree.solfege"),
                        ViewModel.DegreeStyle1,
                        () => OnMenuDegreeStyle(MacMenu.TagSender("1"), new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("prefs.appearance.degree.numbered"),
                        ViewModel.DegreeStyle2,
                        () => OnMenuDegreeStyle(MacMenu.TagSender("2"), new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("prefs.appearance.useflats"),
                        ViewModel.UseFlats, () => OnMenuUseFlats(this, new RoutedEventArgs()))),
                MacMenu.SubMenu(MacMenu.Str("prefs.playback.lockstarttime"),
                    MacMenu.Toggle(MacMenu.Str("prefs.playback.lockstarttime.off"),
                        ViewModel.LockStartTime0,
                        () => OnMenuLockStartTime(MacMenu.TagSender("0"), new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("prefs.playback.lockstarttime.on"),
                        ViewModel.LockStartTime1,
                        () => OnMenuLockStartTime(MacMenu.TagSender("1"), new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("prefs.playback.lockstarttime.onlycursor"),
                        ViewModel.LockStartTime2,
                        () => OnMenuLockStartTime(MacMenu.TagSender("2"), new RoutedEventArgs()))),
                MacMenu.SubMenu(MacMenu.Str("prefs.playback.autoscroll"),
                    MacMenu.Toggle(MacMenu.Str("prefs.playback.autoscrollmode.off"),
                        ViewModel.PlaybackAutoScroll0,
                        () => OnMenuPlaybackAutoScroll(MacMenu.TagSender("0"), new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("prefs.playback.autoscrollmode.stationarycursor"),
                        ViewModel.PlaybackAutoScroll1,
                        () => OnMenuPlaybackAutoScroll(MacMenu.TagSender("1"), new RoutedEventArgs())),
                    MacMenu.Toggle(MacMenu.Str("prefs.playback.autoscrollmode.pagescroll"),
                        ViewModel.PlaybackAutoScroll2,
                        () => OnMenuPlaybackAutoScroll(MacMenu.TagSender("2"), new RoutedEventArgs()))),
                MacMenu.Separator(),
                MacMenu.SubMenu(MacMenu.Str("pianoroll.menu.view.pianoroll"),
                    MacMenu.Toggle(MacMenu.Str("pianoroll.menu.view.pianoroll.detach"),
                        ViewModel.PianoRollDetached,
                        () => OnMenuDetachPianoRoll(this, new RoutedEventArgs())),
                    MacMenu.Item(MacMenu.Str("pianoroll.menu.view.pianoroll.hide"),
                        () => OnMenuHidePianoRoll(this, new RoutedEventArgs()))));
        }

        private NativeMenuItem BuildBatchMenu() {
            return MacMenu.SubMenu(MacMenu.Str("pianoroll.menu.batch"),
                MacMenu.SubMenu(MacMenu.Str("pianoroll.menu.notes"), ViewModel.NoteBatchEdits),
                MacMenu.SubMenu(MacMenu.Str("pianoroll.menu.lyrics"), ViewModel.LyricBatchEdits),
                MacMenu.SubMenu(MacMenu.Str("pianoroll.menu.reset"), ViewModel.ResetBatchEdits),
                MacMenu.SubMenu(MacMenu.Str("pianoroll.menu.external"), ViewModel.ExternalBatchEdits),
                MacMenu.SubMenu(MacMenu.Str("pianoroll.menu.part.legacypluginexp"), ViewModel.LegacyPlugins));
        }
    }
}
