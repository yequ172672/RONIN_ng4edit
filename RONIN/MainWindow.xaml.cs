using Microsoft.Win32;
using RONIN.Browser;
using RONIN.Formats;
using RONIN.Preview;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using YakumoLib.Assets;

namespace RONIN
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
        {
            

            if (sender is TreeViewItem { DataContext: FolderNodeViewModel folder } &&
            DataContext is MainWindowViewModel vm)
            {
                
                vm.ActivePath = folder.Model.FullPath;

                vm.DebugAssets.Clear();
                foreach (var asset in folder.Model.Assets)
                    vm.DebugAssets.Add(asset);
            }
            e.Handled = true;
        }

        private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double totalSize = 120 + 100 + 100 + 100 + 290 + 70;

            FileNameColumn.Width = Math.Max(100,
                FileList.ActualWidth - totalSize - 35);
        }

        private void FileList_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is ListView { SelectedItem: AssetEntry entry } &&
DataContext is MainWindowViewModel vm)
            {
                vm.SelectFile(entry);

                /*vm.PreviousEntry = null;
                vm.SelectedEntry = entry;
                _ = vm.ShowPreviewAsync(entry);*/
            }
        }

        private void ExportRaw_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                if (vm.SelectedEntry == null)
                {
                    vm.StatusText = "No file is selected!";
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
                        AssetExtractor.ExtractAll(vm.SelectedEntry, folderPath);
                    }


                }

            }
        }

        private void AssetTableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListView { SelectedItem: AssetTableEntry entry } &&
  DataContext is MainWindowViewModel vm)
            {
                AssetEntry aentry = vm.Library.GetByUUID(entry.data);
                if (aentry != null)
                {
                    vm.PreviousEntry = vm.SelectedEntry;
                    vm.SelectedEntry = aentry;
                    _ = vm.ShowPreviewAsync(aentry);

                }
                else
                {
                    vm.StatusText = "Couldn't find asset!";
                }

            }
        }


    }
}