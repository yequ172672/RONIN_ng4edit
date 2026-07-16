using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RONIN.Browser;
using RONIN.Formats;
using RONIN.Preview;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    [NotifyPropertyChangedFor(nameof(IsModelModdingEnabled))]
    private AssetEntry? _selectedEntry;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasBackEntry))] private AssetEntry? _previousEntry;

    [ObservableProperty] private bool _isCompressEnabled;

    /// <summary>Whether there are staged modifications waiting for patch generation.</summary>
    public bool HasStagedChanges => ModifiedAssets.Count > 0;

    /// <summary>Latest patch generation and backup details shown by the modding UI.</summary>
    [ObservableProperty] private string _patchCommitStatus = "No patch generated in this session.";
    [ObservableProperty] private string _backupSnapshotStatus = "No backup snapshot created in this session.";
    [ObservableProperty] private string _verificationStatus = "Patch verification has not run.";

    /// <summary>Path to the game's Assets directory. Configured via ModdingDialog or app settings.</summary>
    [ObservableProperty] private string _gamePath = "";

    private readonly AssetPreviewRegistry _previewRegistry;

    [ObservableProperty] private object? _previewContent;
    [ObservableProperty] private bool _isTextureModdingEnabled;

    /// <summary>Tracks all assets modified during this session for patch generation.</summary>
    public ObservableCollection<ModifiedAssetEntry> ModifiedAssets { get; } = new();

    public bool HasSelectedEntry => SelectedEntry is not null;
    public bool HasBackEntry => PreviousEntry is not null;
    public bool IsModelModdingEnabled => SelectedEntry?.Type == AssetType.SkeletalMesh;

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
        ModifiedAssets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStagedChanges));
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
            IsTextureModdingEnabled = false;
            StatusText = $"Cannot preview {entry.Type}";
            ProgressCurrent = 0;
            return;
        }

        try
        {
            PreviewContent = await provider.LoadPreviewAsync(entry);
            IsTextureModdingEnabled = entry.Type == AssetType.Texture;
        }
        catch (Exception ex)
        {
            PreviewContent = null;
            IsTextureModdingEnabled = false;
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
    private void ExportTextureAsDds()
    {
        if (SelectedEntry is null)
        {
            StatusText = "No package selected.";
            return;
        }

        if (SelectedEntry.Type != AssetType.Texture)
        {
            StatusText = "DDS export is only available for texture packages.";
            return;
        }

        try
        {
            string archiveRootDirectory = SelectedEntry.ContentDirectory
                ?? SelectedEntry.SubEntries?.FirstOrDefault()?.ContentDirectory
                ?? throw new InvalidDataException($"Texture '{SelectedEntry.Path}' has no content directory.");

            var texture = TexturePackageDds.Extract(SelectedEntry, archiveRootDirectory);
            var saveDialog = new SaveFileDialog
            {
                Title = "Export Texture Package as DDS",
                FileName = Path.ChangeExtension(SelectedEntry.FileName, ".dds"),
                Filter = "DDS files (*.dds)|*.dds|All files (*.*)|*.*"
            };

            if (saveDialog.ShowDialog() == true)
            {
                File.WriteAllBytes(saveDialog.FileName, texture.DdsBytes);
                StatusText = $"Exported DDS: {saveDialog.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"DDS export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ReplaceSelectedPackageWithDds()
    {
        if (SelectedEntry is null)
        {
            StatusText = "No package selected.";
            return;
        }

        if (SelectedEntry.Type != AssetType.Texture)
        {
            StatusText = "DDS import is only available for texture packages.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select DDS file for texture package",
            Filter = "DDS files (*.dds)|*.dds|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            byte[] ddsBytes = File.ReadAllBytes(dialog.FileName);
            TexturePackageDds.ValidateImportAgainstAsset(ddsBytes, SelectedEntry);

            ModifiedAssets.Add(new ModifiedAssetEntry
            {
                ParentEntry = SelectedEntry,
                SubEntry = null,
                ModifiedData = ddsBytes,
                OriginalData = Array.Empty<byte>(),
                Compress = IsCompressEnabled
            });

            StatusText = $"Marked {SelectedEntry.FileName} for DDS replacement ({ddsBytes.Length} bytes). Generate patch to apply.";
        }
        catch (Exception ex)
        {
            StatusText = $"DDS import failed: {ex.Message}";
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
            PatchCommitStatus = "Preparing verified patch transaction...";
            BackupSnapshotStatus = "Creating and verifying backup snapshot...";
            VerificationStatus = "Writing and verifying staged CSV/DAT...";
            StatusText = "Generating patch files...";

            string patchId = PatchGenerator.GeneratePatch(GamePath, ModifiedAssets.ToList(), IsCompressEnabled);

            string? snapshotPath = BackupManager.GetLatestBackupPath(GamePath);
            BackupSnapshotStatus = snapshotPath is null
                ? "No backup snapshot was created (no existing patch pair to back up)."
                : $"Backup verified: {snapshotPath}";
            PatchCommitStatus = $"Committed @{patchId}.csv + @{patchId}.dat under {Path.GetFullPath(GamePath)}";
            VerificationStatus = "Verified: staged bytes, CSV metadata, committed files, and backup hashes.";
            StatusText = $"Patch @{patchId} generated and verified.";

            // Staged changes are cleared only after GeneratePatch returns successfully.
            ModifiedAssets.Clear();
        }
        catch (Exception ex)
        {
            VerificationStatus = $"Verification failed: {ex.Message}";
            PatchCommitStatus = "Patch commit failed; staged changes were retained.";
            BackupSnapshotStatus = "Backup/commit status unavailable because generation did not complete.";
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
            CompressData = IsCompressEnabled,
        };

        if (dialog.ShowDialog() == true)
        {
            GamePath = dialog.GamePath;
            IsCompressEnabled = dialog.CompressData;
            StatusText = "Patch generator settings updated.";
        }
    }

    [RelayCommand]
    private async Task ExportModelTextureSetPngAsync()
    {
        if (!IsModelModdingEnabled || SelectedEntry is null || Library is null)
        {
            StatusText = "Select a skeletal model package from a loaded asset database first.";
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select the parent folder for the PNG texture set"
        };
        if (dialog.ShowDialog() != true) return;

        string stem = Path.GetFileNameWithoutExtension(SelectedEntry.FileName);
        string destination = Path.Combine(dialog.FolderName, $"{stem}_texture_set_png");
        try
        {
            IsBusy = true;
            StatusText = "Exporting model texture set as PNG...";
            ModelTextureSetManifest manifest = await Task.Run(() =>
                ModelTextureSetService.ExportPng(SelectedEntry, Library.All, destination));
            StatusText = $"Exported {manifest.Textures.Count} PNG textures to {destination}";
        }
        catch (Exception ex)
        {
            StatusText = $"PNG texture-set export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportModelTextureSetAsync()
    {
        if (!IsModelModdingEnabled || SelectedEntry is null || Library is null)
        {
            StatusText = "Select a skeletal model package from a loaded asset database first.";
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select a RONIN PNG texture-set folder"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            StatusText = "Validating and importing image texture set...";
            string manifestPath = Path.Combine(dialog.FolderName, ModelTextureSetService.ManifestFileName);
            if (!File.Exists(manifestPath))
                throw new InvalidDataException($"Texture set is missing '{ModelTextureSetService.ManifestFileName}'.");
            ModelTextureSetManifest manifest = ModelTextureSetService.DeserializeManifest(
                await File.ReadAllTextAsync(manifestPath));
            if (manifest.SchemaVersion != 2)
                throw new InvalidDataException("Selected folder is not a supported PNG texture set.");
            if (!manifest.ModelAssetId.Equals(SelectedEntry.StringAssetID, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Texture set belongs to '{manifest.ModelPath}', not the selected model.");

            ModelTextureImportPlan plan = await Task.Run(() =>
                ModelTextureSetService.PlanImport(dialog.FolderName, Library.All, IsCompressEnabled));

            foreach (ModifiedAssetEntry change in plan.Changes)
                StageOrReplace(change);

            StatusText = plan.Changes.Count == 0
                ? $"No texture changes ({plan.UnchangedCount} unchanged); nothing staged."
                : $"Staged {plan.Changes.Count} changed textures; {plan.UnchangedCount} unchanged. Generate patch to apply.";
        }
        catch (Exception ex)
        {
            StatusText = $"Image texture-set import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StageOrReplace(ModifiedAssetEntry change)
    {
        ModifiedAssetEntry? existing = ModifiedAssets.FirstOrDefault(item =>
            item.ParentEntry.AssetID.Equals(change.ParentEntry.AssetID) &&
            string.Equals(item.SubEntry?.FileName, change.SubEntry?.FileName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            ModifiedAssets.Remove(existing);
        ModifiedAssets.Add(change);
    }
}
