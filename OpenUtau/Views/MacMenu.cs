using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    internal static class MacMenu {
        public static readonly KeyModifiers CmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        public static string Str(string key) => ThemeManager.GetString(key);

        public static KeyGesture Cmd(Key key, KeyModifiers extra = KeyModifiers.None) =>
            new KeyGesture(key, CmdKey | extra);

        public static KeyGesture Plain(Key key) => new KeyGesture(key);

        internal static Action? MenuActionCompleted;

        private static void Invoke(Action action) {
            action();
            var completed = MenuActionCompleted;
            if (completed != null) {
                Avalonia.Threading.Dispatcher.UIThread.Post(completed);
            }
        }

        public static NativeMenuItem Item(
            string header, Action action, KeyGesture? gesture = null, bool enabled = true) {
            var item = new NativeMenuItem(header) {
                Gesture = gesture,
                IsEnabled = enabled,
            };
            if (enabled) {
                item.Click += (sender, args) => Invoke(action);
            } else {
                item.Command = NeverCommand.Instance;
            }
            return item;
        }

        public static NativeMenuItem Toggle(string header, bool isChecked, Action action) {
            var item = new NativeMenuItem(header) {
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = isChecked,
            };
            item.Click += (sender, args) => Invoke(action);
            return item;
        }

        public static NativeMenuItemSeparator Separator() => new NativeMenuItemSeparator();

        public static NativeMenuItem SubMenu(string header, params NativeMenuItemBase[] items) =>
            SubMenu(header, (IEnumerable<NativeMenuItemBase>)items);

        public static NativeMenuItem SubMenu(string header, IEnumerable<NativeMenuItemBase> items) {
            var menu = new NativeMenu();
            foreach (var item in items) {
                menu.Add(item);
            }
            return new NativeMenuItem(header) { Menu = menu };
        }

        public static NativeMenuItem FromViewModel(MenuItemViewModel vm) {
            var item = new NativeMenuItem(vm.Header ?? string.Empty) {
                Command = vm.Command,
                CommandParameter = vm.CommandParameter,
                IsEnabled = vm.IsEnabled,
                Gesture = vm.InputGesture,
            };
            if (vm.Items != null && vm.Items.Count > 0) {
                var menu = new NativeMenu();
                foreach (var child in vm.Items) {
                    menu.Add(FromViewModel(child));
                }
                item.Menu = menu;
            }
            return item;
        }

        public static NativeMenuItem SubMenu(string header, IEnumerable<MenuItemViewModel> items) {
            var menu = new NativeMenu();
            foreach (var vm in items) {
                menu.Add(FromViewModel(vm));
            }
            return new NativeMenuItem(header) { Menu = menu };
        }

        public static MenuItem TagSender(object tag) => new MenuItem { Tag = tag };

        private sealed class NeverCommand : System.Windows.Input.ICommand {
            public static readonly NeverCommand Instance = new NeverCommand();
            public bool CanExecute(object? parameter) => false;
            public void Execute(object? parameter) { }
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }
    }
}
