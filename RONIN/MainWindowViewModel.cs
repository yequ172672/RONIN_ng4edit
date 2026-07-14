using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RONIN.Browser;
using RONIN.Formats;
using RONIN.Preview;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using YakumoLib.Assets;
using YakumoLib.Database;
using YakumoLib.Formats;
using YakumoLib.Modding;

namespace RONIN;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _progressCurrent;
    [ObservableProperty] private int _progressTotal = 1;
    [ObservableProperty] private string _activePath = "/";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedEntry))] private AssetEntry? _selectedEntry;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasBackEntry))] private AssetEntry? _previousEntry;

    [ObservableProperty] private bool _isCompressEnabled;

    /// <summary>Path to the game's Assets directory. Configured via ModdingDialog or app settings.</summary>
    [ObservableProperty] private string _gamePath = "";

    private readonly AssetPreviewRegistry _previewRegistry;

    [ObservableProperty] private object? _previewContent;

    /// <summary>Tracks all assets modified during this session for patch generation.</summary>
    public ObservableCollection<ModifiedAssetEntry> ModifiedAssets { get; } = new();

    public bool HasSelectedEntry => SelectedEntry is not null;
    public bool HasBackEntry => PreviousEntry is not null;

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

            // Infer game path from the selected AssetDatabase.dat directory
            GamePath = csvDirectory;

            // Run the heavy work on a background thread
            Library = await Task.Run(() =>
                AssetLibrary.Load(csvDirectory, progress));

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
        }
        catch (Exception ex)
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

    private RelayCommand? goBackToPreviousEntry;
    public ICommand GoBackToPreviousEntry => goBackToPreviousEntry ??= new RelayCommand(PerformGoBackToPreviousEntry);

    private void PerformGoBackToPreviousEntry()
    {
        if (SelectedEntry is null) return;
        PreviousEntry = null;
        SelectFile(SelectedEntry);
    }

    private RelayCommand? exportRaw;
    public ICommand ExportRaw => exportRaw ??= new RelayCommand(PerformExportRaw);

    private void PerformExportRaw()
    {
        if (SelectedEntry == null)
        {
            StatusText = "No file is selected!";
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder to export to",
        };

        if (dialog.ShowDialog() == true)
        {
            string folderPath = dialog.FolderName;
            AssetExtractor.ExtractAll(SelectedEntry, folderPath);
            StatusText = $"Exported {SelectedEntry.FileName} to {folderPath}";
        }
    }

    // ========== Modding Commands ==========

    [RelayCommand]
    private async Task ExportAsFbxAsync()
    {
        if (SelectedEntry is null)
        {
            StatusText = "No package selected.";
            return;
        }

        // Find the first MDL subfile
        var mdlSub = SelectedEntry.SubEntries?.FirstOrDefault(s =>
            s.FileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase));
        if (mdlSub is null)
        {
            StatusText = "No MDL file found in selected package.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Converting MDL to FBX...";

            byte[] mdlData = await Task.Run(() =>
                AssetExtractor.GetSubBlob(mdlSub, SelectedEntry, mdlSub.ContentDirectory));

            var saveDialog = new SaveFileDialog
            {
                Title = "Export Package as FBX",
                FileName = $"{SelectedEntry.FileName}.fbx",
                Filter = "FBX files (*.fbx)|*.fbx|All files (*.*)|*.*"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var model = await Task.Run(() => MDLParserExtended.Parse(mdlData));
                string fbx = await Task.Run(() => MdlToFbxConverter.ConvertToFbx(model));
                await File.WriteAllTextAsync(saveDialog.FileName, fbx);
                StatusText = $"Exported FBX: {saveDialog.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"FBX export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ExportSelectedSubfile(SubAssetEntry? subEntry)
    {
        if (SelectedEntry is null)
        {
            StatusText = "No package selected.";
            return;
        }

        if (subEntry is null)
        {
            StatusText = "No subfile selected.";
            return;
        }

        try
        {
            byte[] data = AssetExtractor.GetSubBlob(subEntry, SelectedEntry, subEntry.ContentDirectory);

            var saveDialog = new SaveFileDialog
            {
                Title = "Export Subfile",
                FileName = subEntry.FileName,
                Filter = "All files (*.*)|*.*"
            };

            if (saveDialog.ShowDialog() == true)
            {
                File.WriteAllBytes(saveDialog.FileName, data);
                StatusText = $"Exported subfile: {subEntry.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportSelectedSubfileAsFbxAsync(SubAssetEntry? subEntry)
    {
        if (SelectedEntry is null || subEntry is null)
        {
            StatusText = "No subfile selected.";
            return;
        }

        if (!subEntry.FileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Only MDL subfiles can be exported as FBX.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Converting MDL to FBX...";

            byte[] mdlData = await Task.Run(() => AssetExtractor.GetSubBlob(subEntry, SelectedEntry, subEntry.ContentDirectory));

            var saveDialog = new SaveFileDialog
            {
                Title = "Export Subfile as FBX",
                FileName = Path.ChangeExtension(subEntry.FileName, ".fbx"),
                Filter = "FBX files (*.fbx)|*.fbx|All files (*.*)|*.*"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var model = await Task.Run(() => YakumoLib.Formats.MDLParserExtended.Parse(mdlData));
                string fbx = await Task.Run(() => YakumoLib.Formats.MdlToFbxConverter.ConvertToFbx(model));
                await File.WriteAllTextAsync(saveDialog.FileName, fbx);
                StatusText = $"Exported FBX: {saveDialog.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"FBX export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ReplaceSelectedSubfile(SubAssetEntry? subEntry)
    {
        if (SelectedEntry is null || subEntry is null)
        {
            StatusText = "No subfile selected.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Select replacement for {subEntry.FileName}",
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            byte[] originalData = AssetExtractor.GetSubBlob(subEntry, SelectedEntry, subEntry.ContentDirectory);
            byte[] newData = File.ReadAllBytes(dialog.FileName);

            // Backup original files
            if (!string.IsNullOrEmpty(GamePath) && Directory.Exists(GamePath))
            {
                var backup = new BackupManager(GamePath);
                backup.Backup(subEntry.SourceArchive + ".dat");
            }

            ModifiedAssets.Add(new ModifiedAssetEntry
            {
                ParentEntry = SelectedEntry,
                SubEntry = subEntry,
                ModifiedData = newData,
                OriginalData = originalData,
                Compress = IsCompressEnabled
            });

            StatusText = $"Marked {subEntry.FileName} for replacement ({newData.Length} bytes). Generate patch to apply.";
        }
        catch (Exception ex)
        {
            StatusText = $"Replace failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ReplaceSelectedPackage()
    {
        if (SelectedEntry is null)
        {
            StatusText = "No package selected.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Select replacement data for {SelectedEntry.FileName}",
            Filter = "All files (*.*)|*.*|DAT files (*.dat)|*.dat|CSV files (*.csv)|*.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            byte[] newData = File.ReadAllBytes(dialog.FileName);

            // Backup original archives
            if (!string.IsNullOrEmpty(GamePath) && Directory.Exists(GamePath))
            {
                var backup = new BackupManager(GamePath);
                foreach (var sub in SelectedEntry.SubEntries ?? [])
                {
                    if (sub.SourceArchive is not null)
                        backup.Backup(sub.SourceArchive + ".dat");
                }
            }

            ModifiedAssets.Add(new ModifiedAssetEntry
            {
                ParentEntry = SelectedEntry,
                SubEntry = null,
                ModifiedData = newData,
                OriginalData = [],
                Compress = IsCompressEnabled
            });

            StatusText = $"Marked {SelectedEntry.FileName} for replacement ({newData.Length} bytes). Generate patch to apply.";
        }
        catch (Exception ex)
        {
            StatusText = $"Replace failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReplaceWithFbxAsync()
    {
        if (SelectedEntry is null)
        {
            StatusText = "No package selected.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select FBX file for conversion",
            Filter = "FBX files (*.fbx)|*.fbx|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            IsBusy = true;
            StatusText = "Converting FBX to MDL...";

            // Find the modeldata.mdl subfile
            var modelDataSub = SelectedEntry.SubEntries?
                .FirstOrDefault(s => s.FileName.Equals("modeldata.mdl", StringComparison.OrdinalIgnoreCase));

            if (modelDataSub is null)
            {
                StatusText = "No modeldata.mdl found in selected package.";
                return;
            }

            // Get original MDL data
            byte[] originalMdl = await Task.Run(() =>
                AssetExtractor.GetSubBlob(modelDataSub, SelectedEntry, modelDataSub.ContentDirectory));

            // Backup original
            if (!string.IsNullOrEmpty(GamePath) && Directory.Exists(GamePath))
            {
                var backup = new BackupManager(GamePath);
                backup.Backup(modelDataSub.SourceArchive + ".dat");
            }

            // Convert FBX to MDL
            byte[] newMdl = await Task.Run(() => YakumoLib.Formats.FbxToMdlConverter.Convert(dialog.FileName, originalMdl));

            ModifiedAssets.Add(new ModifiedAssetEntry
            {
                ParentEntry = SelectedEntry,
                SubEntry = modelDataSub,
                ModifiedData = newMdl,
                OriginalData = originalMdl,
                Compress = IsCompressEnabled
            });

            StatusText = $"Converted FBX to MDL ({newMdl.Length} bytes). Generate patch to apply.";
        }
        catch (Exception ex)
        {
            StatusText = $"FBX conversion failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void GeneratePatch()
    {
        if (ModifiedAssets.Count == 0)
        {
            StatusText = "No modified assets to patch. Replace a subfile or package first.";
            return;
        }

        if (string.IsNullOrEmpty(GamePath) || !Directory.Exists(GamePath))
        {
            StatusText = "Game path is not set or does not exist. Configure it in Patch Generator Settings.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Generating patch files...";

            string patchId = PatchGenerator.GeneratePatch(GamePath, ModifiedAssets.ToList(), IsCompressEnabled);

            StatusText = $"Generated @{patchId}.csv/.dat in {GamePath}";

            // Optionally clear the modified list after generating patch
            ModifiedAssets.Clear();
        }
        catch (Exception ex)
        {
            StatusText = $"Patch generation failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenPatchGeneratorSettings()
    {
        var dialog = new ModdingDialog
        {
            Owner = Application.Current.MainWindow,
            GamePath = GamePath,
            CompressData = IsCompressEnabled
        };

        if (dialog.ShowDialog() == true)
        {
            GamePath = dialog.GamePath;
            IsCompressEnabled = dialog.CompressData;
            StatusText = "Patch generator settings updated.";
        }
    }
}
