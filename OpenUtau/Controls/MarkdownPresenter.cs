using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Controls {
    public class MarkdownPresenter : ContentControl {
        public static readonly StyledProperty<string> MarkdownProperty =
            AvaloniaProperty.Register<MarkdownPresenter, string>(nameof(Markdown), string.Empty);

        public string Markdown {
            get => GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        static MarkdownPresenter() {
            MarkdownProperty.Changed.AddClassHandler<MarkdownPresenter>((c, _) => c.Rebuild());
        }

        void Rebuild() {
            Content = Build(Markdown ?? string.Empty);
        }

        static readonly Regex HeadingRegex = new Regex(@"^(#{1,6})\s+(.*)$");
        static readonly Regex BulletRegex = new Regex(@"^\s*[-*+]\s+(.*)$");
        static readonly Regex NumberedRegex = new Regex(@"^\s*(\d+)\.\s+(.*)$");
        static readonly Regex InlineRegex = new Regex(
            @"(\*\*(?<bold>.+?)\*\*)" +
            @"|(__(?<bold2>.+?)__)" +
            @"|(\*(?<italic>.+?)\*)" +
            @"|(_(?<italic2>.+?)_)" +
            @"|(`(?<code>.+?)`)" +
            @"|(\[(?<linktext>.+?)\]\((?<url>[^)]+)\))" +
            @"|(?<bareurl>https?://[^\s]+)");

        static Control Build(string markdown) {
            var panel = new StackPanel { Spacing = 2 };
            string[] lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            foreach (string raw in lines) {
                string line = raw.TrimEnd();
                if (string.IsNullOrWhiteSpace(line)) {
                    panel.Children.Add(new Control { Height = 6 });
                    continue;
                }
                var heading = HeadingRegex.Match(line);
                if (heading.Success) {
                    int level = heading.Groups[1].Value.Length;
                    var tb = new TextBlock {
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeight.Bold,
                        FontSize = level <= 1 ? 18 : level == 2 ? 15 : 13,
                        Margin = new Thickness(0, 4, 0, 2),
                    };
                    AddInlines(tb.Inlines!, heading.Groups[2].Value);
                    panel.Children.Add(tb);
                    continue;
                }
                var bullet = BulletRegex.Match(line);
                if (bullet.Success) {
                    panel.Children.Add(ListItem("•  ", bullet.Groups[1].Value));
                    continue;
                }
                var numbered = NumberedRegex.Match(line);
                if (numbered.Success) {
                    panel.Children.Add(ListItem($"{numbered.Groups[1].Value}.  ", numbered.Groups[2].Value));
                    continue;
                }
                var para = new TextBlock { TextWrapping = TextWrapping.Wrap };
                AddInlines(para.Inlines!, line);
                panel.Children.Add(para);
            }
            return panel;
        }

        static Control ListItem(string marker, string text) {
            var grid = new Grid {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(8, 0, 0, 0),
            };
            var markerBlock = new TextBlock { Text = marker, VerticalAlignment = VerticalAlignment.Top };
            var content = new TextBlock { TextWrapping = TextWrapping.Wrap };
            AddInlines(content.Inlines!, text);
            Grid.SetColumn(markerBlock, 0);
            Grid.SetColumn(content, 1);
            grid.Children.Add(markerBlock);
            grid.Children.Add(content);
            return grid;
        }

        static void AddInlines(InlineCollection inlines, string text) {
            int pos = 0;
            foreach (Match m in InlineRegex.Matches(text)) {
                if (m.Index > pos) {
                    inlines.Add(new Run(text.Substring(pos, m.Index - pos)));
                }
                if (m.Groups["bold"].Success || m.Groups["bold2"].Success) {
                    string s = m.Groups["bold"].Success ? m.Groups["bold"].Value : m.Groups["bold2"].Value;
                    inlines.Add(new Run(s) { FontWeight = FontWeight.Bold });
                } else if (m.Groups["italic"].Success || m.Groups["italic2"].Success) {
                    string s = m.Groups["italic"].Success ? m.Groups["italic"].Value : m.Groups["italic2"].Value;
                    inlines.Add(new Run(s) { FontStyle = FontStyle.Italic });
                } else if (m.Groups["code"].Success) {
                    inlines.Add(new Run(m.Groups["code"].Value) { FontFamily = FontFamily.Parse("Consolas, monospace") });
                } else if (m.Groups["linktext"].Success) {
                    inlines.Add(Link(m.Groups["linktext"].Value, m.Groups["url"].Value));
                } else if (m.Groups["bareurl"].Success) {
                    inlines.Add(Link(m.Groups["bareurl"].Value, m.Groups["bareurl"].Value));
                }
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) {
                inlines.Add(new Run(text.Substring(pos)));
            }
        }

        static Inline Link(string label, string url) {
            var tb = new TextBlock {
                Text = label,
                Foreground = Brushes.DodgerBlue,
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            tb.PointerPressed += (_, _) => {
                try {
                    OS.OpenWeb(url);
                } catch (Exception e) {
                    Log.Error(e, $"Failed to open link {url}");
                }
            };
            return new InlineUIContainer(tb) { BaselineAlignment = BaselineAlignment.TextBottom };
        }
    }
}
