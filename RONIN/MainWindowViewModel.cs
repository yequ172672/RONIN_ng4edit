using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RONIN.Browser;
using RONIN.Preview;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography.Pkcs;
using System.Windows.Input;
using YakumoLib.Assets;
using YakumoLib.Database;

namespace RONIN;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _progressCurrent;
    [ObservableProperty] private int _progressTotal = 1;
    [ObservableProperty] private string _activePath = "/";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    [NotifyPropertyChangedFor(nameof(IsModelSelected))]
    private AssetEntry _selectedEntry = null;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasBackEntry))] private AssetEntry _previousEntry = null;

    private readonly AssetPreviewRegistry _previewRegistry;

    [ObservableProperty] private object? _previewContent;

    public bool HasSelectedEntry => SelectedEntry is not null;
    public bool HasBackEntry => PreviousEntry is not null;
    public bool IsModelSelected => SelectedEntry?.Type == AssetType.SkeletalMesh;

    public AssetLibrary? Library { get; private set; }
    
    public ObservableCollection<FolderNodeViewModel> RootFolders { get; } = new();
    public ObservableCollection<AssetEntry> DebugAssets { get; } = new();

    public MainWindowViewModel()
    {
        _previewRegistry = new AssetPreviewRegistry();
        _previewRegistry.Register(new TexturePreviewProvider());
        _previewRegistry.Register(new AtlasTexturePreviewProvider());
        _previewRegistry.Register(new AnimationPreviewProvider());
        _previewRegistry.Register(new FontPreviewProvider());
        _previewRegistry.Register(new ModelPreviewProvider());
        _previewRegistry.Register(new AssetTablePreviewProvider());
    }




    [RelayCommand]
    private async Task LoadAssetDatabaseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Asset Database (AssetDatabase.dat)|AssetDatabase.dat|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Select AssetDatabase.dat"
        };
        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        ProgressCurrent = 0;
        ProgressTotal = 1;
        StatusText = "Starting load...";

        var progress = new Progress<string>(p =>
        {
            StatusText = p;
        });

        try
        {
            var csvDirectory = Path.GetDirectoryName(dialog.FileName)!;

            // Run the heavy work on a background thread — never block the UI thread
            Library = await Task.Run(() =>
                AssetLibrary.Load(Path.GetDirectoryName(dialog.FileName), progress));

            var root = await Task.Run(() => FolderNode.BuildTree(Library.All));

            RootFolders.Clear();
            RootFolders.Add(new FolderNodeViewModel(root));


            StatusText = $"Done! Loaded {Library.Count} assets.";
            ProgressTotal = 1;
            ProgressCurrent = 1;

            DebugAssets.Clear();
            foreach (var asset in Library.All)
                DebugAssets.Add(asset);

        }
        catch (Exception ex)
        {
            StatusText = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ShowPreviewAsync(AssetEntry entry)
    {
        var provider = _previewRegistry.GetProvider(entry);
        if (provider is null)
        {
            PreviewContent = null;
            StatusText = $"Cannot preview {entry.Type}";
            ProgressCurrent = 0;
            return;
        }

        try
        {
            PreviewContent = await provider.LoadPreviewAsync(entry);

        }catch (Exception ex)
        {
            PreviewContent = null;
            StatusText = $"Preview failed: {ex.Message}";
        }



    }

    public void SelectFile(AssetEntry entry)
    {
        PreviousEntry = null;
        SelectedEntry = entry;
        _ = ShowPreviewAsync(entry);
    }

    private RelayCommand goBackToPreviousEntry;
    public ICommand GoBackToPreviousEntry => goBackToPreviousEntry ??= new RelayCommand(PerformGoBackToPreviousEntry);

    private void PerformGoBackToPreviousEntry()
    {
        SelectedEntry = PreviousEntry;
        PreviousEntry = null;
        SelectFile(SelectedEntry);
    }

    private RelayCommand exportRaw;
    public ICommand ExportRaw => exportRaw ??= new RelayCommand(PerformExportRaw);

    private void PerformExportRaw()
    {
        if (SelectedEntry == null)
        {
            StatusText = "No file is selected!";
        }
        else
        {
            OpenFolderDialog dialog = new()
            {
                Title = "Select a folder to export too",
            };

            if (dialog.ShowDialog() == true)
            {
                string folderPath = dialog.FolderName;
                AssetExtractor.ExtractAll(SelectedEntry, folderPath);
            }


        }
    }
}
