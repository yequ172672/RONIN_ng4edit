using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using YakumoLib.Assets;
using YakumoLib.Modding;

namespace RONIN;

/// <summary>
/// Partial class extension of MainWindow providing modding-related event handlers
/// and programmatic menu wiring for items that lack Command bindings in XAML.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Called from the constructor (via InitializeComponent) to wire up
    /// modding menu items that do not have declarative Command bindings.
    /// </summary>
    private void WireModdingMenuItems()
    {
        // Find the "Modding Tools" top-level menu
        if (!(FindName("ModdingToolsMenu") is MenuItem moddingMenu))
        {
            // Walk the visual tree to find it
            var found = FindModdingToolsMenu();
            if (found is null)
                return;
            moddingMenu = found;
        }

        foreach (var item in moddingMenu.Items)
        {
            if (item is MenuItem menuItem)
            {
                WireMenuItem(menuItem);
                continue;
            }

            // Check for nested containers (e.g., the CheckBox inside "Patch Generator Settings")
            if (item is StackPanel panel || item is ContentControl content)
            {
                // Handle inline content
            }
        }
    }

    private MenuItem? FindModdingToolsMenu()
    {
        // Search the main menu bar for "Modding Tools"
        var menu = LogicalTreeHelper.GetChildren(this)
            .OfType<DockPanel>()
            .SelectMany(d => LogicalTreeHelper.GetChildren(d).OfType<ToolBar>())
            .SelectMany(t => LogicalTreeHelper.GetChildren(t).OfType<Menu>())
            .FirstOrDefault();

        if (menu is null)
            return null;

        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi && mi.Header?.ToString() == "Modding Tools")
                return mi;
        }

        return null;
    }

    private void WireMenuItem(MenuItem menuItem)
    {
        string header = menuItem.Header?.ToString() ?? "";

        switch (header)
        {
            case "Export Selected Subfile":
                menuItem.Click += ExportSelectedSubfile_Click;
                break;

            case "Replace Selected Subfile":
                menuItem.Click += ReplaceSelectedSubfile_Click;
                break;

            case "Replace Selected Package (BETA)":
                menuItem.Click += ReplaceSelectedPackage_Click;
                break;

            case "Replace Selected Package with .FBX":
                menuItem.IsEnabled = true;
                menuItem.Click += ReplaceWithFbx_Click;
                break;

            case "Generate Patch (.csv/.dat)":
                menuItem.Click += GeneratePatch_Click;
                break;

            case "Patch Generator Settings":
                menuItem.Click += PatchGeneratorSettings_Click;
                // Find and enable the checkbox
                EnableCompressCheckbox(menuItem);
                break;
        }
    }

    private static void EnableCompressCheckbox(MenuItem parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is CheckBox checkBox)
            {
                checkBox.IsEnabled = true;
                var binding = new Binding("IsCompressEnabled")
                {
                    Source = ((MainWindow)Application.Current.MainWindow).DataContext
                };
                checkBox.SetBinding(CheckBox.IsCheckedProperty, binding);
            }
        }
    }

    // ========== Click Event Handlers ==========

    private void ExportSelectedSubfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        // Determine whether a subfile is selected in the SubfileList
        SubAssetEntry? subEntry = SubfileList?.SelectedItem as SubAssetEntry;

        if (subEntry is null)
        {
            // No specific subfile selected; offer to export the whole package as raw
            vm.ExportRaw.Execute(null);
            return;
        }

        // Ask user: export as raw binary or as FBX?
        var choiceDialog = new ExportChoiceDialog(subEntry.FileName);
        choiceDialog.Owner = this;

        if (choiceDialog.ShowDialog() == true)
        {
            if (choiceDialog.ExportAsFbx && subEntry.FileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            {
                vm.ExportSelectedSubfileAsFbxCommand.Execute(subEntry);
            }
            else
            {
                vm.ExportSelectedSubfileCommand.Execute(subEntry);
            }
        }
    }

    private void ReplaceSelectedSubfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        SubAssetEntry? subEntry = SubfileList?.SelectedItem as SubAssetEntry;
        vm.ReplaceSelectedSubfileCommand.Execute(subEntry);
    }

    private void ReplaceSelectedPackage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.ReplaceSelectedPackageCommand.Execute(null);
    }

    private void ReplaceWithFbx_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        _ = vm.ReplaceWithFbxCommand.ExecuteAsync(null);
    }

    private void GeneratePatch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.GeneratePatchCommand.Execute(null);
    }

    private void PatchGeneratorSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.OpenPatchGeneratorSettingsCommand.Execute(null);
    }
}

/// <summary>
/// Simple dialog asking the user whether to export a subfile as raw binary or FBX.
/// </summary>
internal sealed class ExportChoiceDialog : Window
{
    private readonly System.Windows.Controls.RadioButton _rawRadio;
    private readonly System.Windows.Controls.RadioButton _fbxRadio;
    private readonly System.Windows.Controls.Button _okButton;
    private readonly System.Windows.Controls.Button _cancelButton;

    public bool ExportAsFbx => _fbxRadio.IsChecked == true;

    public ExportChoiceDialog(string fileName)
    {
        Title = $"Export: {fileName}";
        Width = 380;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(15) };

        stack.Children.Add(new System.Windows.Controls.Label
        {
            Content = $"How would you like to export \"{fileName}\"?",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _rawRadio = new System.Windows.Controls.RadioButton
        {
            Content = "Raw binary (.mdl/.dat)",
            IsChecked = true,
            Margin = new Thickness(10, 5, 0, 5)
        };
        stack.Children.Add(_rawRadio);

        bool canFbx = fileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase);
        _fbxRadio = new System.Windows.Controls.RadioButton
        {
            Content = "Converted (.fbx)",
            IsEnabled = canFbx,
            Margin = new Thickness(10, 5, 0, 5)
        };
        stack.Children.Add(_fbxRadio);

        if (!canFbx)
        {
            stack.Children.Add(new System.Windows.Controls.Label
            {
                Content = "(FBX export is only available for .mdl subfiles)",
                FontStyle = FontStyles.Italic,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(20, 0, 0, 5)
            });
        }

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };

        _okButton = new System.Windows.Controls.Button
        {
            Content = "Export",
            Width = 80,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        _okButton.Click += (_, _) => { DialogResult = true; Close(); };

        _cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 28,
            IsCancel = true
        };

        buttonPanel.Children.Add(_okButton);
        buttonPanel.Children.Add(_cancelButton);
        stack.Children.Add(buttonPanel);

        Content = stack;
    }
}
