using Microsoft.Win32;
using System.Windows;

namespace RONIN;

/// <summary>
/// Dialog for configuring patch generator settings (game path, compress toggle).
/// Allows direct generation or saving settings.
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

    public ModdingDialog()
    {
        InitializeComponent();

        // Set up binding context
        DataContext = this;
        StatusTextBlock.Text = "Configure the game directory and compression settings, then click Generate Patch.";
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
