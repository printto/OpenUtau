using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using OpenUtau.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class SingerCardViewModel : ViewModelBase {
        public CatalogSinger Singer { get; }
        public string Id => Singer.id;
        public string Name => string.IsNullOrEmpty(Singer.name) ? Singer.id : Singer.name;
        public string Author => Singer.author;
        public string Description => Singer.description;
        public bool HasImage => !string.IsNullOrEmpty(Singer.image_url);
        public bool IsExternal => !string.IsNullOrEmpty(Singer.page_url);
        public string PageUrl => Singer.page_url;

        [Reactive] public Bitmap? Image { get; set; }
        [Reactive] public string DownloadUrl { get; set; } = string.Empty;
        [Reactive] public string LatestVersion { get; set; } = string.Empty;
        [Reactive] public bool IsInstalled { get; set; }
        [Reactive] public string InstalledVersion { get; set; } = string.Empty;
        [Reactive] public bool Resolved { get; set; }
        [Reactive] public bool Busy { get; set; }
        [Reactive] public double Progress { get; set; }

        public bool HasUpdate => IsInstalled && Resolved
            && !string.IsNullOrEmpty(LatestVersion)
            && !string.Equals(InstalledVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);
        public bool CanAction => !Busy && Resolved && !string.IsNullOrEmpty(DownloadUrl) && (!IsInstalled || HasUpdate);
        public string VersionDisplay => Resolved && !string.IsNullOrEmpty(LatestVersion)
            ? (IsInstalled ? $"{InstalledVersion} → {LatestVersion}" : LatestVersion)
            : (IsInstalled ? InstalledVersion : string.Empty);
        public string ActionLabel => !IsInstalled
            ? ThemeManager.GetString("singercatalog.action.install")
            : (HasUpdate ? ThemeManager.GetString("singercatalog.action.update")
                         : ThemeManager.GetString("singercatalog.action.installed"));
        public bool ShowProgress => Busy;
        public bool ShowVersion => !IsExternal && !Busy;
        public bool ShowInstallButton => !IsExternal && !Busy;
        public bool ShowWebButton => IsExternal && !Busy;

        public SingerCardViewModel(CatalogSinger singer) {
            Singer = singer;
            this.WhenAnyValue(x => x.IsInstalled, x => x.InstalledVersion, x => x.LatestVersion, x => x.Resolved, x => x.Busy)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(HasUpdate));
                    this.RaisePropertyChanged(nameof(CanAction));
                    this.RaisePropertyChanged(nameof(VersionDisplay));
                    this.RaisePropertyChanged(nameof(ActionLabel));
                    this.RaisePropertyChanged(nameof(ShowProgress));
                    this.RaisePropertyChanged(nameof(ShowVersion));
                    this.RaisePropertyChanged(nameof(ShowInstallButton));
                    this.RaisePropertyChanged(nameof(ShowWebButton));
                });
            if (HasImage) {
                _ = LoadImageAsync(Singer.image_url);
            }
        }

        async Task LoadImageAsync(string url) {
            try {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "OpenUtau");
                client.Timeout = TimeSpan.FromSeconds(30);
                var bytes = await client.GetByteArrayAsync(url);
                using var ms = new System.IO.MemoryStream(bytes);
                Image = new Bitmap(ms);
            } catch (Exception e) {
                Log.Warning(e, $"Failed to load singer image {url}");
            }
        }
    }

    public class SingerCatalogViewModel : ViewModelBase {
        public ObservableCollection<SingerCardViewModel> BuiltInSingers { get; } = new ObservableCollection<SingerCardViewModel>();
        public ObservableCollection<SingerCardViewModel> PublicSingers { get; } = new ObservableCollection<SingerCardViewModel>();
        [Reactive] public string Status { get; set; } = string.Empty;
        public bool HasStatus => !string.IsNullOrEmpty(Status);
        [Reactive] public bool IsLoading { get; set; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        public SingerCatalogViewModel() {
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
            this.WhenAnyValue(x => x.Status).Subscribe(_ => this.RaisePropertyChanged(nameof(HasStatus)));
            _ = RefreshAsync();
        }

        public async Task RefreshAsync() {
            try {
                IsLoading = true;
                Status = ThemeManager.GetString("singercatalog.status.fetching");
                var catalog = await SingerCatalog.Inst.FetchCatalogAsync();
                var installed = SingerCatalog.Inst.GetInstalledRecords();

                BuiltInSingers.Clear();
                PublicSingers.Clear();
                foreach (var s in catalog) {
                    var card = new SingerCardViewModel(s);
                    bool isPublic = card.IsExternal
                        || string.Equals(s.category, "Public Singers", StringComparison.OrdinalIgnoreCase);
                    if (isPublic) {
                        PublicSingers.Add(card);
                    } else {
                        if (installed.TryGetValue(card.Id, out var rec)) {
                            card.IsInstalled = true;
                            card.InstalledVersion = rec.version;
                        }
                        card.DownloadUrl = s.download_url;
                        card.LatestVersion = s.version;
                        card.Resolved = true;
                        BuiltInSingers.Add(card);
                    }
                }

                Status = (BuiltInSingers.Count + PublicSingers.Count) == 0
                    ? ThemeManager.GetString("singercatalog.status.empty")
                    : string.Empty;
            } catch (Exception e) {
                Status = ThemeManager.GetString("singercatalog.status.error");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            } finally {
                IsLoading = false;
            }
        }

        public async Task<string?> DownloadAsync(SingerCardViewModel card) {
            var progress = new Progress<int>(p => card.Progress = p);
            return await SingerCatalog.Inst.DownloadToTempAsync(card.DownloadUrl, progress, card.Singer.sha256);
        }

        public bool NeedsWizard(SingerCardViewModel card) {
            return SingerCatalog.NeedsWizard(card.Singer, card.DownloadUrl);
        }

        public async Task InstallSilentAsync(SingerCardViewModel card, string tempPath) {
            await SingerCatalog.Inst.InstallDownloadedAsync(tempPath, card.Singer.type, card.Singer.encoding);
            MarkInstalled(card);
            TryDelete(tempPath);
        }

        public void MarkInstalled(SingerCardViewModel card) {
            SingerCatalog.Inst.SetInstalled(card.Id, card.LatestVersion, card.DownloadUrl);
            card.IsInstalled = true;
            card.InstalledVersion = card.LatestVersion;
        }

        public void ReportError(Exception e) {
            Status = ThemeManager.GetString("singercatalog.status.failed");
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
        }

        static void TryDelete(string path) {
            try {
                if (System.IO.File.Exists(path)) {
                    System.IO.File.Delete(path);
                }
            } catch { }
        }
    }
}
