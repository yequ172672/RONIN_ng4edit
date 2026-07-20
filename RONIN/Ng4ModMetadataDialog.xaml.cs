using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace RONIN;

public sealed partial class Ng4ModMetadataDialog : Window
{
    public string ModId { get => ModIdTextBox.Text.Trim(); set => ModIdTextBox.Text = value; }
    public string Version { get => VersionTextBox.Text.Trim(); set => VersionTextBox.Text = value; }
    public string ModName { get => NameTextBox.Text.Trim(); set => NameTextBox.Text = value; }
    public string Author { get => AuthorTextBox.Text.Trim(); set => AuthorTextBox.Text = value; }
    public string Description { get => DescriptionTextBox.Text; set => DescriptionTextBox.Text = value; }
    public string CoverPath { get => CoverPathTextBox.Text.Trim(); set => CoverPathTextBox.Text = value; }

    public Ng4ModMetadataDialog() => InitializeComponent();

    private void BrowseCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select NG4MOD PNG cover",
            Filter = "PNG images (*.png)|*.png"
        };
        if (dialog.ShowDialog(this) == true) CoverPath = dialog.FileName;
    }

    private void Package_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ModId) || string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(ModName))
        {
            ValidationText.Text = "Mod ID, version, and name are required.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(CoverPath) &&
            (!Path.GetExtension(CoverPath).Equals(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(CoverPath)))
        {
            ValidationText.Text = "Cover must be an existing PNG file.";
            return;
        }
        DialogResult = true;
    }
}
