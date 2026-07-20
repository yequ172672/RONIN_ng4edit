using Microsoft.Win32;
using System.Windows;

namespace RONIN;

/// <summary>
/// Dialog for configuring patch generator settings (game path, compress toggle).
/// Displays staged-change count and latest patch/backup/verification status.
/// </summary>
public sealed partial class ModdingDialog : Window
{
    /// <summary>Path to the game's Assets directory.</summary>
    public string GamePath
    {
        get => GamePathTextBox.Text;
        set => GamePathTextBox.Text = value;
    }

    /// <summary>Whether to compress data in patch .dat files.</summary>
    public bool CompressData
    {
        get => CompressCheckBox.IsChecked == true;
        set => CompressCheckBox.IsChecked = value;
    }

    /// <summary>Number of staged modifications.</summary>
    public int StagedChangeCount
    {
        get => _stagedChangeCount;
        set { _stagedChangeCount = value; StagedCountText.Text = $"{value} pending replacement(s)"; }
    }
    private int _stagedChangeCount;

    /// <summary>Descriptive status of the last patch commit attempt.</summary>
    public string PatchCommitStatus
    {
        get => _patchCommitStatus;
        set { _patchCommitStatus = value ?? ""; PatchCommitText.Text = _patchCommitStatus; }
    }
    private string _patchCommitStatus = "No patch generated in this session.";

    /// <summary>Descriptive status of the last backup snapshot.</summary>
    public string BackupSnapshotStatus
    {
        get => _backupSnapshotStatus;
        set { _backupSnapshotStatus = value ?? ""; BackupSnapshotText.Text = _backupSnapshotStatus; }
    }
    private string _backupSnapshotStatus = "No backup snapshot created in this session.";

    /// <summary>Descriptive status of the last patch verification run.</summary>
    public string VerificationStatus
    {
        get => _verificationStatus;
        set { _verificationStatus = value ?? ""; VerificationText.Text = _verificationStatus; }
    }
    private string _verificationStatus = "Patch verification has not run.";

    public ModdingDialog()
    {
        InitializeComponent();

        // Set up binding context for GamePath/CompressData
        DataContext = this;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Game Assets Directory"
        };

        if (dialog.ShowDialog() == true)
        {
            GamePath = dialog.FolderName;
        }
    }

    private void GeneratePatch_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GamePath) || !System.IO.Directory.Exists(GamePath))
        {
            StatusTextBlock.Text = "Please enter a valid game directory path.";
            return;
        }

        // Close the dialog with a result that signals "generate patch"
        DialogResult = true;

        // Trigger the generate patch command via the main ViewModel
        if (Owner?.DataContext is MainWindowViewModel vm)
        {
            vm.GamePath = GamePath;
            vm.IsCompressEnabled = CompressData;
            Close();
            vm.GeneratePatchCommand.Execute(null);
        }
        else
        {
            Close();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
