<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# Browser

## Purpose
Provides the hierarchical asset tree navigation system. Converts the flat list of parsed assets into a folder-tree structure and wraps it in WPF-compatible ViewModel objects for the TreeView UI.

## Key Files
| File | Description |
|------|-------------|
| `FolderNode.cs` | Tree node model — splits asset paths into segments, builds a nested folder hierarchy with `BuildTree()` |
| `FolderNodeViewModel.cs` | Observable ViewModel wrapper around `FolderNode` — exposes `Children` as `ObservableCollection` for WPF binding |

## Subdirectories
*(None)*

## For AI Agents

### Working In This Directory
- `FolderNode.BuildTree()` is a static factory that takes `IEnumerable<AssetEntry>` and returns a root node
- Paths are split by `/` to build nested `Subfolders` dictionaries (case-insensitive)
- ViewModel layer wraps the model for WPF data binding with `ObservableCollection`

### Testing Requirements
- Feed a test set of `AssetEntry` paths to `BuildTree()` and verify the tree structure

### Common Patterns
- `Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase)` for case-insensitive folder lookups
- ViewModel wraps model with `[ObservableProperty]` for MVVM binding

## Dependencies

### Internal
- `YakumoLib/Assets/AssetEntry` — Asset entry model used to build the tree

### External
- CommunityToolkit.Mvvm — ObservableObject base class

<!-- MANUAL: -->