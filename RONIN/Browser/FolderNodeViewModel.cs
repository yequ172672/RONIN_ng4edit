using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RONIN.Browser
{
    public sealed partial class FolderNodeViewModel : ObservableObject
    {
        public FolderNode Model { get; }
        public string Name => Model.Name;
        public ObservableCollection<FolderNodeViewModel> Children { get; }

        public FolderNodeViewModel(FolderNode model)
        {
            Model = model;
            Children = new ObservableCollection<FolderNodeViewModel>(
                model.Subfolders.Values
                    .OrderBy(f => f.Name)
                    .Select(f => new FolderNodeViewModel(f)));
        }
    }
}
