using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using YakumoLib.Assets;
using YakumoLib.Modding;

namespace RONIN;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    public async Task ExportNg4ModelWorkspaceAsync()
    {
        if (!TryGetSelectedModel(out AssetEntry model) || Library is null)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder for the NG4 model workspace"
        };
        if (dialog.ShowDialog() != true)
            return;

        await RunWorkflowAsync("Exporting GLB and TGA texture workspace...", () =>
        {
            Ng4ModWorkspaceExportResult result = Ng4ModWorkspaceService.Export(model, Library.All, dialog.FolderName);
            return $"Workspace exported: {result.WorkspacePath}";
        });
    }

    [RelayCommand]
    public async Task ExportNg4TextureSetPngAsync()
    {
        if (!TryGetSelectedModel(out AssetEntry model) || Library is null)
            return;
        if (!TrySelectOutputFolder("Select a folder for the PNG texture set", out string destination))
            return;

        await RunWorkflowAsync("Exporting PNG texture set...", () =>
        {
            ModelTextureSetManifest manifest = ModelTextureSetService.ExportPng(model, Library.All, destination);
            return $"PNG texture set exported: {manifest.Textures.Count} texture(s).";
        });
    }

    [RelayCommand]
    public async Task ExportNg4TextureSetTgaAsync()
    {
        if (!TryGetSelectedModel(out AssetEntry model) || Library is null)
            return;
        if (!TrySelectOutputFolder("Select a folder for the TGA texture set", out string destination))
            return;

        await RunWorkflowAsync("Exporting TGA texture set...", () =>
        {
            ModelTextureSetManifest manifest = ModelTextureSetService.ExportTga(model, Library.All, destination);
            return $"TGA texture set exported: {manifest.Textures.Count} texture(s).";
        });
    }

    [RelayCommand]
    public async Task PackageNg4ModAsync()
    {
        if (Library is null)
        {
            StatusText = "Load AssetDatabase before packaging an NG4MOD.";
            return;
        }

        var glbDialog = new OpenFileDialog
        {
            Title = "Select an edited GLB from an NG4 workspace",
            Filter = "GLB model (*.glb)|*.glb"
        };
        if (glbDialog.ShowDialog() != true)
            return;

        var metadata = new Ng4ModMetadataDialog();
        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        if (owner is not null)
            metadata.Owner = owner;
        if (metadata.ShowDialog() != true)
            return;

        var saveDialog = new SaveFileDialog
        {
            Title = "Save NG4MOD package",
            Filter = "NG4MOD package (*.ng4mod)|*.ng4mod",
            FileName = metadata.ModId + ".ng4mod",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (saveDialog.ShowDialog() != true)
            return;

        Ng4ModWorkspacePackageRequest request = new(
            metadata.ModId,
            metadata.ModName,
            metadata.Version,
            metadata.Author,
            metadata.Description,
            [],
            saveDialog.FileName,
            CoverPath: string.IsNullOrWhiteSpace(metadata.CoverPath) ? null : metadata.CoverPath);

        await RunWorkflowAsync("Building and verifying NG4MOD v2 package...", () =>
        {
            Ng4ModWorkspacePackageResult result = Ng4ModWorkspaceService.Package(glbDialog.FileName, Library, request);
            return $"NG4MOD exported and verified: {result.PackagePath} ({result.ChangedTextureCount} changed texture(s)).";
        });
    }

    private bool TryGetSelectedModel(out AssetEntry model)
    {
        model = SelectedEntry!;
        if (Library is null)
        {
            StatusText = "Load AssetDatabase before using the NG4 model workflow.";
            model = null!;
            return false;
        }
        if (model.Type != AssetType.SkeletalMesh)
        {
            StatusText = "Select a SkeletalMesh package first.";
            model = null!;
            return false;
        }
        return true;
    }

    private static bool TrySelectOutputFolder(string title, out string destination)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (dialog.ShowDialog() == true)
        {
            destination = dialog.FolderName;
            return true;
        }
        destination = null!;
        return false;
    }

    private async Task RunWorkflowAsync(string status, Func<string> operation)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusText = status;
        try
        {
            string result = await Task.Run(operation);
            StatusText = result;
        }
        catch (Exception exception)
        {
            StatusText = $"NG4 workflow failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
