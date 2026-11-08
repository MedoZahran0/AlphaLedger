using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.App.Views;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AlphaLedger.App.ViewModels;

/// <summary>
/// Archive browser. The tree starts with the year folders on disk
/// (Archive\2026, Archive\2027, ...), and each year contains its company folders.
/// </summary>
public partial class ArchiveViewModel : ObservableObject
{
    private HashSet<string> _expandedFolderPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _backHistory = new();
    private readonly List<string> _forwardHistory = new();
    private bool _applyingHistory;
    private readonly ICompanyService _companyService;
    private readonly ICompanyArchiveService _archiveService;
    private readonly ICompanyFolderService _folderService;
    private readonly ISettingsService _settings;
    private readonly IAuditService _audit;
    private readonly IArchiveAttachmentService _attachments;

    public ArchiveViewModel(
        ICompanyService companyService,
        ICompanyArchiveService archiveService,
        ICompanyFolderService folderService,
        ISettingsService settings,
        IAuditService audit,
        IArchiveAttachmentService attachments)
    {
        _companyService = companyService;
        _archiveService = archiveService;
        _folderService = folderService;
        _settings = settings;
        _audit = audit;
        _attachments = attachments;
    }

    [ObservableProperty] private string _archiveRoot = string.Empty;
    [ObservableProperty] private ArchiveFolderNode? _selectedFolder;
    [ObservableProperty] private ArchiveFolderNode? _selectedYearNode;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private ArchiveFileItem? _selectedFile;
    [ObservableProperty] private bool _searchAllFolders;
    [ObservableProperty] private Guid _selectedCompanyId;
    [ObservableProperty] private string _selectedCompanyCode = string.Empty;
    [ObservableProperty] private string _companySearchText = string.Empty;

    public ObservableCollection<ArchiveFolderNode> TreeNodes { get; } = new();
    public ObservableCollection<ArchiveFileItem> Files { get; } = new();
    public ObservableCollection<ArchiveFileItem> SelectedFiles { get; } = new();
    public bool CanNavigateBack => _backHistory.Count > 0 ||
        (SelectedFolder is { IsYear: false } current && Path.GetDirectoryName(current.FullPath) is { } parentPath && FindFolder(parentPath) is not null);
    public bool CanNavigateForward => _forwardHistory.Count > 0;
    public bool CanOpenCompanyArchiveOnDisk => SelectedFolder is not null;

    public async Task LoadAsync()
    {
        var previousPath = SelectedFolder?.FullPath;
        var previousCompanyId = SelectedCompanyId;
        var initialLoad = TreeNodes.Count == 0;
        _expandedFolderPaths = CollectExpandedPaths();

        ArchiveRoot = await _settings.GetDocumentRootAsync() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ArchiveRoot))
        {
            TreeNodes.Clear();
            Files.Clear();
            return;
        }

        _folderService.EnsureDocumentRoot(ArchiveRoot);
        await _archiveService.EnsureCurrentYearStructureAsync();

        var companies = await _companyService.GetCompaniesAsync(new CompanyFilter());
        var currentYear = _folderService.CurrentYearLabel;
        var yearLabels = Directory.EnumerateDirectories(ArchiveRoot)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(IsYearFolder)
            .Concat(companies.Select(company =>
                (company.ArchiveStartYear ?? int.Parse(currentYear, System.Globalization.CultureInfo.InvariantCulture))
                .ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Append(currentYear)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(year => year, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TreeNodes.Clear();

        foreach (var yearName in yearLabels)
        {
            var yearDirectory = _folderService.EnsureYearFolder(ArchiveRoot, yearName);
            var yearNode = new ArchiveFolderNode
            {
                Name = yearName,
                FullPath = yearDirectory,
                IsYear = true,
                IsExpanded = initialLoad || _expandedFolderPaths.Contains(yearDirectory)
            };

            foreach (var companyDirectory in Directory.EnumerateDirectories(yearDirectory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var folderName = Path.GetFileName(companyDirectory);
                var company = MatchCompany(folderName, companies);
                var companyNode = new ArchiveFolderNode
                {
                    Name = company?.DisplayName ?? folderName,
                    Code = company?.Code ?? string.Empty,
                    FullPath = companyDirectory,
                    CompanyRootPath = companyDirectory,
                    CompanyId = company?.Id ?? Guid.Empty,
                    CompanyCode = company?.Code ?? string.Empty,
                    IsRoot = true,
                    IsExpanded = initialLoad || _expandedFolderPaths.Contains(companyDirectory)
                };
                BuildTree(companyNode.Children, companyDirectory, companyNode.CompanyId, companyNode.CompanyCode, companyDirectory);
                yearNode.Children.Add(companyNode);
            }

            TreeNodes.Add(yearNode);
        }

        SelectedYearNode = previousPath is null
            ? null
            : TreeNodes.FirstOrDefault(node => IsWithinYear(previousPath, node.FullPath));
        SelectedYearNode ??= TreeNodes.FirstOrDefault(node =>
            string.Equals(node.Name, currentYear, StringComparison.OrdinalIgnoreCase))
            ?? TreeNodes.FirstOrDefault();

        if (previousPath is not null)
        {
            SelectedFolder = FindFolder(previousPath)
                ?? FindCompanyNode(previousCompanyId)
                ?? SelectedYearNode;
        }
        else
        {
            SelectedFolder = SelectedYearNode;
        }

        await LoadFilesAsync();
    }

    /// <summary>Selects the company's archive start-year folder, creating it when necessary.</summary>
    public async Task SelectCompanyByIdAsync(Guid companyId)
    {
        var companyNode = FindCompanyNode(companyId);
        if (companyNode is null)
        {
            var company = await _companyService.GetCompanyAsync(companyId);
            if (company is not null)
            {
                var yearLabel = company.ArchiveStartYear?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? _folderService.CurrentYearLabel;
                await _archiveService.EnsureCompanyFolderAsync(companyId, company.Code, yearLabel);
                await LoadAsync();
                companyNode = FindCompanyNode(companyId);
            }
        }

        if (companyNode is not null)
        {
            SelectedFolder = companyNode;
        }
    }

    partial void OnSelectedFolderChanged(ArchiveFolderNode? oldValue, ArchiveFolderNode? newValue)
    {
        if (newValue is not null)
        {
            var yearNode = TreeNodes.FirstOrDefault(node => IsWithinYear(newValue.FullPath, node.FullPath));
            if (yearNode is not null && !ReferenceEquals(SelectedYearNode, yearNode))
            {
                SelectedYearNode = yearNode;
            }

            SelectedCompanyId = newValue.CompanyId;
            SelectedCompanyCode = newValue.CompanyCode;
        }
        if (!_applyingHistory && oldValue is not null && newValue is not null && !string.Equals(oldValue.FullPath, newValue.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _backHistory.Add(oldValue.FullPath);
            _forwardHistory.Clear();
        }
        NotifyNavigationCommands();
        OpenCompanyArchiveOnDiskCommand.NotifyCanExecuteChanged();
        _ = LoadFilesAsync();
    }

    partial void OnSelectedYearNodeChanged(ArchiveFolderNode? value)
    {
        if (value is not null &&
            (SelectedFolder is null || !IsWithinYear(SelectedFolder.FullPath, value.FullPath)))
        {
            SelectedFolder = value;
        }
    }

    partial void OnSelectedCompanyIdChanged(Guid value)
        => OpenCompanyArchiveOnDiskCommand.NotifyCanExecuteChanged();

    partial void OnSearchTextChanged(string value) => _ = LoadFilesAsync();
    partial void OnSearchAllFoldersChanged(bool value) => _ = LoadFilesAsync();

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    public void NavigateBack()
    {
        if (SelectedFolder is null) return;
        var currentPath = SelectedFolder.FullPath;
        while (_backHistory.Count > 0)
        {
            var index = _backHistory.Count - 1;
            var path = _backHistory[index];
            _backHistory.RemoveAt(index);
            var target = FindFolder(path);
            if (target is null) continue;
            _forwardHistory.Add(currentPath);
            _applyingHistory = true;
            SelectedFolder = target;
            _applyingHistory = false;
            NotifyNavigationCommands();
            return;
        }
        if (SelectedFolder is { IsYear: false } current && Path.GetDirectoryName(current.FullPath) is { } parentPath && FindFolder(parentPath) is { } parent)
        {
            _forwardHistory.Add(currentPath);
            _applyingHistory = true;
            SelectedFolder = parent;
            _applyingHistory = false;
        }
        NotifyNavigationCommands();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    public void NavigateForward()
    {
        if (SelectedFolder is null) return;
        var currentPath = SelectedFolder.FullPath;
        while (_forwardHistory.Count > 0)
        {
            var index = _forwardHistory.Count - 1;
            var path = _forwardHistory[index];
            _forwardHistory.RemoveAt(index);
            var target = FindFolder(path);
            if (target is null) continue;
            _backHistory.Add(currentPath);
            _applyingHistory = true;
            SelectedFolder = target;
            _applyingHistory = false;
            NotifyNavigationCommands();
            return;
        }
        NotifyNavigationCommands();
    }

    private HashSet<string> CollectExpandedPaths()
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var yearNode in TreeNodes)
        {
            CollectExpanded(yearNode, expanded);
        }
        return expanded;
    }

    private static void CollectExpanded(ArchiveFolderNode node, HashSet<string> expanded)
    {
        if (node.IsExpanded) expanded.Add(node.FullPath);
        foreach (var child in node.Children) CollectExpanded(child, expanded);
    }

    private ArchiveFolderNode? FindFolder(string path)
    {
        foreach (var yearNode in TreeNodes)
        {
            if (string.Equals(yearNode.FullPath, path, StringComparison.OrdinalIgnoreCase)) return yearNode;
            var match = FindFolder(yearNode.Children, path);
            if (match is not null) return match;
        }
        return null;
    }

    private static ArchiveFolderNode? FindFolder(IEnumerable<ArchiveFolderNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase)) return node;
            var match = FindFolder(node.Children, path);
            if (match is not null) return match;
        }
        return null;
    }

    private ArchiveFolderNode? FindCompanyNode(Guid companyId)
    {
        if (companyId == Guid.Empty) return null;
        foreach (var yearNode in TreeNodes)
        {
            var match = yearNode.Children.FirstOrDefault(node => node.CompanyId == companyId);
            if (match is not null) return match;
        }
        return null;
    }

    private void NotifyNavigationCommands()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        OnPropertyChanged(nameof(CanOpenCompanyArchiveOnDisk));
        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
    }

    private void BuildTree(ObservableCollection<ArchiveFolderNode> parent, string path, Guid companyId, string companyCode, string companyRootPath)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(path).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                var child = new ArchiveFolderNode
                {
                    Name = Path.GetFileName(directory),
                    FullPath = directory,
                    CompanyId = companyId,
                    CompanyCode = companyCode,
                    CompanyRootPath = companyRootPath,
                    IsExpanded = _expandedFolderPaths.Contains(directory)
                };
                BuildTree(child.Children, directory, companyId, companyCode, companyRootPath);
                parent.Add(child);
            }
        }
        catch
        {
            // Ignore folders that cannot be read (permissions, disconnected drive).
        }
    }

    private async Task LoadFilesAsync()
    {
        Files.Clear();
        if (SelectedFolder is null) return;
        try
        {
            foreach (var folder in SelectedFolder.Children)
            {
                if (string.IsNullOrWhiteSpace(SearchText) || folder.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                {
                    Files.Add(new ArchiveFileItem
                    {
                        Name = folder.Name,
                        FullPath = folder.FullPath,
                        CreatedAt = Directory.GetCreationTime(folder.FullPath),
                        ModifiedAt = Directory.GetLastWriteTime(folder.FullPath),
                        FolderName = Path.GetFileName(SelectedFolder.FullPath),
                        IsFolder = true,
                        FolderNode = folder
                    });
                }
            }

            IEnumerable<string> files;
            if (SearchAllFolders && SelectedCompanyId != Guid.Empty)
            {
                var root = GetCompanyRoot(SelectedCompanyId, SelectedCompanyCode);
                files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            }
            else
            {
                files = Directory.EnumerateFiles(SelectedFolder.FullPath);
            }

            foreach (var file in files)
            {
                if (file.EndsWith(".al-restore.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(SearchText) &&
                    !Path.GetFileName(file).Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
                    !file.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) continue;

                var info = new FileInfo(file);
                var item = new ArchiveFileItem
                {
                    Name = info.Name,
                    FullPath = file,
                    SizeBytes = info.Length,
                    CreatedAt = info.CreationTime,
                    ModifiedAt = info.LastWriteTime,
                    Extension = info.Extension.ToLowerInvariant(),
                    FolderName = Path.GetFileName(Path.GetDirectoryName(file)!)
                };
                if (!string.IsNullOrWhiteSpace(SearchText) &&
                    !item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
                    !item.FullPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    continue;
                Files.Add(item);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }

        await Task.CompletedTask;
    }

    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        if (SelectedFolder is null) return;
        foreach (var source in paths)
        {
            if (!File.Exists(source)) continue;
            var destination = GetUniquePath(Path.Combine(SelectedFolder.FullPath, Path.GetFileName(source)));
            try
            {
                File.Copy(source, destination, false);
                await _audit.WriteAsync("ArchiveAdd", "ArchiveFile", Path.GetFileName(destination), $"{source} -> {destination}");
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
        await LoadFilesAsync();
    }

    [RelayCommand]
    public async Task AddFilesAsync()
    {
        if (SelectedFolder is null) return;
        var dialog = new OpenFileDialog { Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        await ImportFilesAsync(dialog.FileNames);
    }

    [RelayCommand]
    public async Task CreateFolderAsync()
    {
        if (SelectedFolder is null) return;
        if (SelectedFolder.IsYear)
        {
            StatusMessage = "اختر شركة أولاً، ثم أنشئ المجلد داخل مجلد الشركة.";
            return;
        }
        if (SelectedFolder.IsRoot)
        {
            await CreateCodedMainFolderAsync(SelectedFolder.CompanyId, SelectedFolder.CompanyCode);
            return;
        }

        var dialog = new RenameDialog(LocalizationService.Instance.GetString("ArchiveNewFolder")) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || !TryGetSafeName(dialog.NewName, out var name)) return;
        var path = Path.Combine(SelectedFolder.FullPath, name);
        if (File.Exists(path) || Directory.Exists(path))
        {
            StatusMessage = "يوجد عنصر بهذا الاسم بالفعل.";
            return;
        }
        try
        {
            Directory.CreateDirectory(path);
            await _audit.WriteAsync("ArchiveCreateFolder", "ArchiveFolder", name, path);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        await LoadAsync();
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        if (SelectedFiles.Count == 0 || SelectedCompanyId == Guid.Empty) return;
        var trash = GetTrashFolder(SelectedCompanyId, SelectedCompanyCode);
        if (SelectedFolder is not null && (string.Equals(SelectedFolder.FullPath, trash, StringComparison.OrdinalIgnoreCase) ||
            SelectedFolder.FullPath.StartsWith(trash + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "استخدم الحذف النهائي أو الاستعادة للعناصر الموجودة في المحذوفات.";
            return;
        }

        Directory.CreateDirectory(trash);
        foreach (var item in SelectedFiles.ToList())
        {
            try
            {
                if (item.IsFolder)
                {
                    await MoveFolderToTrashAsync(item.FolderNode ?? new ArchiveFolderNode
                    {
                        Name = item.Name,
                        FullPath = item.FullPath,
                        CompanyId = SelectedCompanyId,
                        CompanyCode = SelectedCompanyCode,
                        CompanyRootPath = GetCompanyRoot(SelectedCompanyId, SelectedCompanyCode)
                    });
                }
                else
                {
                    var destination = GetUniquePath(Path.Combine(trash, Path.GetFileName(item.FullPath)));
                    File.Move(item.FullPath, destination);
                    WriteRestorePath(destination, item.FullPath);
                    await _attachments.UpdatePathsAsync(item.FullPath, destination);
                    await _audit.WriteAsync("ArchiveDelete", "ArchiveFile", item.Name, $"{item.FullPath} -> {destination}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
        SelectedFiles.Clear();
        await LoadFilesAsync();
    }

    [RelayCommand]
    public void OpenFile(ArchiveFileItem? item)
    {
        if (item is null) return;
        if (item.IsFolder)
        {
            SelectedFolder = item.FolderNode;
            return;
        }

        var extension = item.Extension.ToLowerInvariant();
        if (extension == ".pdf" || extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp")
        {
            try
            {
                new ArchivePreviewWindow(item.FullPath) { Owner = System.Windows.Application.Current.MainWindow }.Show();
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
        else
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
    }

    [RelayCommand]
    public async Task RenameAsync(ArchiveFileItem? item)
    {
        if (item is null) return;
        if (item.IsFolder)
        {
            await RenameFolderAsync(item.FolderNode);
            return;
        }

        var dialog = new RenameDialog(Path.GetFileNameWithoutExtension(item.Name)) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || !TryGetSafeName(dialog.NewName, out var baseName)) return;
        var directory = Path.GetDirectoryName(item.FullPath)!;
        var extension = Path.GetExtension(item.Name);
        var newName = baseName + extension;
        var destination = Path.Combine(directory, newName);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            StatusMessage = "يوجد ملف بهذا الاسم بالفعل.";
            return;
        }
        try
        {
            var sidecar = GetRestoreMetadataPath(item.FullPath);
            File.Move(item.FullPath, destination);
            await _attachments.UpdatePathsAsync(item.FullPath, destination);
            if (File.Exists(sidecar)) File.Move(sidecar, GetRestoreMetadataPath(destination));
            await _audit.WriteAsync("ArchiveRename", "ArchiveFile", item.Name, $"{item.FullPath} -> {destination}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        await LoadFilesAsync();
    }

    [RelayCommand]
    public async Task CopySelectedAsync()
    {
        if (SelectedFiles.Count == 0 || SelectedFolder is null) return;
        foreach (var item in SelectedFiles.ToList())
        {
            var destination = GetUniquePath(Path.Combine(SelectedFolder.FullPath, Path.GetFileName(item.FullPath)));
            try
            {
                if (item.IsFolder) CopyDirectory(item.FullPath, destination);
                else File.Copy(item.FullPath, destination, false);
                await _audit.WriteAsync("ArchiveCopy", item.IsFolder ? "ArchiveFolder" : "ArchiveFile", item.Name, $"{item.FullPath} -> {destination}");
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
        await LoadFilesAsync();
    }

    [RelayCommand]
    public async Task MoveSelectedDialogAsync()
    {
        if (SelectedFiles.Count == 0) return;
        var allFolders = new List<ArchiveFolderNode>();
        foreach (var yearNode in TreeNodes)
        {
            CollectFolders(yearNode.Children, allFolders);
        }

        var dialog = new MoveFolderDialog(new ObservableCollection<ArchiveFolderNode>(allFolders));
        if (dialog.ShowDialog() != true || dialog.SelectedFolder is null) return;

        foreach (var item in SelectedFiles.ToList())
        {
            var destination = GetUniquePath(Path.Combine(dialog.SelectedFolder.FullPath, Path.GetFileName(item.FullPath)));
            try
            {
                if (item.IsFolder &&
                    (string.Equals(destination, item.FullPath, StringComparison.OrdinalIgnoreCase) ||
                     destination.StartsWith(item.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    StatusMessage = "لا يمكن نقل مجلد إلى داخله.";
                    continue;
                }

                var sidecar = GetRestoreMetadataPath(item.FullPath);
                if (item.IsFolder) Directory.Move(item.FullPath, destination);
                else File.Move(item.FullPath, destination);
                await _attachments.UpdatePathsAsync(item.FullPath, destination, includeDescendants: item.IsFolder);
                if (File.Exists(sidecar)) File.Move(sidecar, GetRestoreMetadataPath(destination));
                await _audit.WriteAsync("ArchiveMove", item.IsFolder ? "ArchiveFolder" : "ArchiveFile", item.Name, $"{item.FullPath} -> {destination}");
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
        SelectedFiles.Clear();
        await LoadFilesAsync();
    }

    private void CollectFolders(IEnumerable<ArchiveFolderNode> nodes, List<ArchiveFolderNode> accumulator)
    {
        foreach (var node in nodes)
        {
            accumulator.Add(node);
            CollectFolders(node.Children, accumulator);
        }
    }

    [RelayCommand]
    public void Preview(ArchiveFileItem? item)
    {
        if (item is null) return;
        if (item.IsFolder)
        {
            SelectedFolder = item.FolderNode;
            return;
        }

        var extension = item.Extension.ToLowerInvariant();
        if (extension == ".pdf" || extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif")
        {
            new ArchivePreviewWindow(item.FullPath).Show();
        }
        else
        {
            OpenFile(item);
        }
    }

    [RelayCommand]
    public async Task ShowTrashAsync()
    {
        if (SelectedCompanyId == Guid.Empty) return;
        var trash = GetTrashFolder(SelectedCompanyId, SelectedCompanyCode);
        Directory.CreateDirectory(trash);
        var node = new ArchiveFolderNode
        {
            Name = "Trash",
            FullPath = trash,
            CompanyId = SelectedCompanyId,
            CompanyCode = SelectedCompanyCode,
            CompanyRootPath = GetCompanyRoot(SelectedCompanyId, SelectedCompanyCode)
        };
        BuildTree(node.Children, trash, SelectedCompanyId, SelectedCompanyCode, node.CompanyRootPath);
        SelectedFolder = node;
        await LoadFilesAsync();
    }

    [RelayCommand]
    public async Task ShowFilesAsync()
    {
        var company = FindCompanyNode(SelectedCompanyId);
        if (company is null) return;
        SelectedFolder = company;
        await LoadFilesAsync();
    }

    [RelayCommand]
    public async Task RestoreSelectedAsync()
    {
        if (SelectedFiles.Count == 0 || SelectedCompanyId == Guid.Empty) return;
        foreach (var item in SelectedFiles.ToList())
        {
            try
            {
                if (item.IsFolder)
                {
                    await RestoreFolderCoreAsync(item.FolderNode ?? new ArchiveFolderNode
                    {
                        Name = item.Name,
                        FullPath = item.FullPath,
                        CompanyId = SelectedCompanyId,
                        CompanyCode = SelectedCompanyCode,
                        CompanyRootPath = GetCompanyRoot(SelectedCompanyId, SelectedCompanyCode)
                    });
                    continue;
                }

                var metadata = GetRestoreMetadataPath(item.FullPath);
                var original = File.Exists(metadata)
                    ? File.ReadAllText(metadata)
                    : Path.Combine(GetCompanyRoot(SelectedCompanyId, SelectedCompanyCode), Path.GetFileName(item.FullPath));
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                var destination = GetUniquePath(original);
                File.Move(item.FullPath, destination);
                await _attachments.UpdatePathsAsync(item.FullPath, destination);
                if (File.Exists(metadata)) File.Delete(metadata);
                await _audit.WriteAsync("ArchiveRestore", "ArchiveFile", item.Name, $"{item.FullPath} -> {destination}");
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
        SelectedFiles.Clear();
        await LoadFilesAsync();
    }

    [RelayCommand]
    public async Task PermanentDeleteAsync()
    {
        if (SelectedFiles.Count == 0) return;
        var result = System.Windows.MessageBox.Show(
            "Permanently delete selected files?",
            "Confirm",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        foreach (var item in SelectedFiles.ToList())
        {
            try
            {
                if (item.IsFolder) Directory.Delete(item.FullPath, true);
                else File.Delete(item.FullPath);
                var metadata = GetRestoreMetadataPath(item.FullPath);
                if (File.Exists(metadata)) File.Delete(metadata);
                await _audit.WriteAsync("ArchivePermanentDelete", item.IsFolder ? "ArchiveFolder" : "ArchiveFile", item.Name, item.FullPath);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
        SelectedFiles.Clear();
        await LoadFilesAsync();
    }

    [RelayCommand]
    public async Task CreateSubfolderAsync(ArchiveFolderNode? parent)
    {
        parent ??= SelectedFolder;
        if (parent is null) return;
        if (parent.IsYear)
        {
            StatusMessage = "اختر شركة أولاً، ثم أنشئ المجلد داخل مجلد الشركة.";
            return;
        }
        if (parent.IsRoot)
        {
            await CreateCodedMainFolderAsync(parent.CompanyId, parent.CompanyCode);
            return;
        }

        var dialog = new RenameDialog(LocalizationService.Instance.GetString("ArchiveNewFolder")) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || !TryGetSafeName(dialog.NewName, out var name)) return;
        var path = Path.Combine(parent.FullPath, name);
        if (File.Exists(path) || Directory.Exists(path))
        {
            StatusMessage = "يوجد مجلد بهذا الاسم بالفعل.";
            return;
        }
        try
        {
            Directory.CreateDirectory(path);
            await _audit.WriteAsync("ArchiveCreateFolder", "ArchiveFolder", name, path);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        await LoadAsync();
    }

    [RelayCommand]
    public async Task RenameFolderAsync(ArchiveFolderNode? folder)
    {
        if (folder is null) return;
        if (folder.IsYear)
        {
            StatusMessage = "لا يمكن إعادة تسمية مجلد السنة.";
            return;
        }
        if (folder.IsRoot)
        {
            StatusMessage = "لا يمكن إعادة تسمية جذر الشركة.";
            return;
        }

        var dialog = new RenameDialog(folder.Name) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || !TryGetSafeName(dialog.NewName, out var enteredName)) return;
        var parent = Path.GetDirectoryName(folder.FullPath)!;
        var newName = enteredName;
        if (IsSystemCategory(folder.Name))
        {
            var prefix = folder.Name[..(folder.Name.IndexOf(" - ", StringComparison.Ordinal) + 3)];
            if (!newName.StartsWith(prefix, StringComparison.Ordinal)) newName = prefix + newName;
        }
        var destination = Path.Combine(parent, newName);
        if (string.Equals(destination, folder.FullPath, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            StatusMessage = "يوجد مجلد بهذا الاسم بالفعل.";
            return;
        }
        try
        {
            Directory.Move(folder.FullPath, destination);
            var sidecar = GetRestoreMetadataPath(folder.FullPath);
            if (File.Exists(sidecar)) File.Move(sidecar, GetRestoreMetadataPath(destination));
            await _attachments.UpdatePathsAsync(folder.FullPath, destination, includeDescendants: true);
            await _audit.WriteAsync("ArchiveRenameFolder", "ArchiveFolder", folder.Name, $"{folder.FullPath} -> {destination}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        await LoadAsync();
    }

    [RelayCommand]
    public async Task DeleteFolderAsync(ArchiveFolderNode? folder)
    {
        if (folder is null) return;
        await MoveFolderToTrashAsync(folder);
        await LoadAsync();
    }

    private async Task MoveFolderToTrashAsync(ArchiveFolderNode folder)
    {
        if (folder.IsRoot || folder.IsYear)
        {
            StatusMessage = "لا يمكن حذف جذر الشركة أو مجلد السنة.";
            return;
        }
        if (folder.CompanyId == Guid.Empty) return;
        var trash = GetTrashFolder(folder.CompanyId, folder.CompanyCode);
        if (string.Equals(folder.FullPath, trash, StringComparison.OrdinalIgnoreCase) ||
            folder.FullPath.StartsWith(trash + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "هذا العنصر موجود بالفعل في المحذوفات.";
            return;
        }
        Directory.CreateDirectory(trash);
        var destination = GetUniquePath(Path.Combine(trash, Path.GetFileName(folder.FullPath)));
        Directory.Move(folder.FullPath, destination);
        WriteRestorePath(destination, folder.FullPath);
        await _attachments.UpdatePathsAsync(folder.FullPath, destination, includeDescendants: true);
        await _audit.WriteAsync("ArchiveDeleteFolder", "ArchiveFolder", folder.Name, $"{folder.FullPath} -> {destination}");
    }

    [RelayCommand]
    public async Task RenameFileAsync(ArchiveFileItem? item)
    {
        if (item is null) return;
        var dialog = new RenameDialog(Path.GetFileNameWithoutExtension(item.Name)) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || !TryGetSafeName(dialog.NewName, out var baseName)) return;
        var directory = Path.GetDirectoryName(item.FullPath)!;
        var extension = Path.GetExtension(item.Name);
        var newName = baseName + extension;
        var destination = Path.Combine(directory, newName);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            StatusMessage = "يوجد ملف بهذا الاسم بالفعل.";
            return;
        }
        try
        {
            var sidecar = GetRestoreMetadataPath(item.FullPath);
            File.Move(item.FullPath, destination);
            await _attachments.UpdatePathsAsync(item.FullPath, destination);
            if (File.Exists(sidecar)) File.Move(sidecar, GetRestoreMetadataPath(destination));
            await _audit.WriteAsync("ArchiveRename", "ArchiveFile", item.Name, $"{item.FullPath} -> {destination}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        await LoadFilesAsync();
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            index++;
        }
        while (File.Exists(candidate) || Directory.Exists(candidate));
        return candidate;
    }

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    public async Task CopyCompaniesToYearAsync()
    {
        if (SelectedYearNode is not { IsYear: true } sourceYear)
        {
            return;
        }

        var localization = LocalizationService.Instance;
        var dialog = new OpenFolderDialog
        {
            Title = localization.GetString("ArchiveCopyYearDialogTitle"),
            InitialDirectory = ArchiveRoot
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true)
        {
            return;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ArchiveRoot));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dialog.FolderName));
        var destinationYear = Path.GetFileName(destination);
        if (!string.Equals(Path.GetDirectoryName(destination), root, StringComparison.OrdinalIgnoreCase) ||
            !IsYearFolder(destinationYear))
        {
            StatusMessage = localization.GetString("ArchiveCopyYearInvalidTarget");
            return;
        }

        if (string.Equals(sourceYear.Name, destinationYear, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = localization.GetString("ArchiveCopyYearSameTarget");
            return;
        }

        var sourceCompanyCount = Directory.EnumerateDirectories(sourceYear.FullPath).Count();
        if (sourceCompanyCount == 0)
        {
            StatusMessage = localization.GetString("ArchiveCopyYearNoCompanies");
            return;
        }

        var confirmation = string.Format(
            localization.CurrentCulture,
            localization.GetString("ArchiveCopyYearConfirm"),
            sourceYear.Name,
            destinationYear,
            sourceCompanyCount);
        if (MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                confirmation,
                localization.GetString("ArchiveCopyCompaniesToYear"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var copiedCompanyCount = await _archiveService.CopyCompanyFolderStructureToYearAsync(
                sourceYear.Name,
                destinationYear);
            await _audit.WriteAsync(
                "ArchiveCopyCompanyFoldersToYear",
                "ArchiveYear",
                destinationYear,
                $"{sourceYear.Name} -> {destinationYear}; companies={copiedCompanyCount}; folders only");

            await LoadAsync();
            SelectedYearNode = TreeNodes.FirstOrDefault(node =>
                string.Equals(node.Name, destinationYear, StringComparison.OrdinalIgnoreCase));
            StatusMessage = string.Format(
                localization.CurrentCulture,
                localization.GetString("ArchiveCopyYearCompleted"),
                copiedCompanyCount,
                sourceYear.Name,
                destinationYear);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                ex.Message,
                localization.GetString("ArchiveCopyYearErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task CreateMainFolderAsync()
    {
        if (SelectedCompanyId == Guid.Empty) return;
        await CreateCodedMainFolderAsync(SelectedCompanyId, SelectedCompanyCode);
    }

    [RelayCommand]
    public void NavigateIntoFolder(ArchiveFolderNode? folder)
    {
        if (folder is not null) SelectedFolder = folder;
    }

    private async Task CreateCodedMainFolderAsync(Guid companyId, string companyCode)
    {
        if (companyId == Guid.Empty) return;
        var root = GetCompanyRoot(companyId, companyCode);
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            return;
        }

        var dialog = new RenameDialog(LocalizationService.Instance.GetString("ArchiveNewFolder")) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || !TryGetSafeName(dialog.NewName, out var name)) return;
        var usedCodes = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path))
            .Select(GetLeadingFolderCode)
            .Where(value => value >= 13)
            .ToHashSet();
        var code = 13;
        while (code == 99 || usedCodes.Contains(code)) code++;
        var newFolderName = $"{code:D2} - {name}";
        var path = Path.Combine(root, newFolderName);
        try
        {
            Directory.CreateDirectory(path);
            await _audit.WriteAsync("ArchiveCreateMainFolder", "ArchiveFolder", code.ToString(), path);
            await LoadAsync();
            var company = FindCompanyNode(companyId);
            if (company is not null)
            {
                company.IsExpanded = true;
                SelectedFolder = company;
                await LoadFilesAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    public void OpenFolder(ArchiveFolderNode? folder)
    {
        if (folder is null) return;
        SelectedFolder = folder;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder.FullPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenCompanyArchiveOnDisk))]
    public void OpenCompanyArchiveOnDisk()
    {
        if (SelectedFolder is null) return;
        try
        {
            ShellService.OpenFolder(SelectedFolder.FullPath);
            StatusMessage = string.Format(LocalizationService.Instance.GetString("ArchiveOpenedOnDisk"), SelectedFolder.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task RestoreFolderAsync(ArchiveFolderNode? folder)
    {
        if (folder is null) return;
        await RestoreFolderCoreAsync(folder);
        await LoadAsync();
    }

    private async Task RestoreFolderCoreAsync(ArchiveFolderNode folder)
    {
        var sidecar = GetRestoreMetadataPath(folder.FullPath);
        if (!File.Exists(sidecar))
        {
            StatusMessage = "تعذر تحديد موضع المجلد الأصلي للاستعادة.";
            return;
        }
        try
        {
            var original = File.ReadAllText(sidecar);
            Directory.CreateDirectory(Path.GetDirectoryName(original)!);
            var destination = original;
            if (Directory.Exists(destination))
            {
                if (!Directory.EnumerateFileSystemEntries(destination).Any()) Directory.Delete(destination);
                else destination = GetUniquePath(original);
            }
            Directory.Move(folder.FullPath, destination);
            File.Delete(sidecar);
            await _attachments.UpdatePathsAsync(folder.FullPath, destination, includeDescendants: true);
            await _audit.WriteAsync("ArchiveRestoreFolder", "ArchiveFolder", folder.Name, $"{folder.FullPath} -> {destination}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static bool TryGetSafeName(string value, out string name)
    {
        name = value.Trim();
        return name.Length > 0 && name is not "." and not ".." &&
               name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
               !name.Contains(Path.DirectorySeparatorChar) && !name.Contains(Path.AltDirectorySeparatorChar);
    }

    private static int GetLeadingFolderCode(string folderName)
    {
        var separator = folderName.IndexOf(" - ", StringComparison.Ordinal);
        return separator > 0 && int.TryParse(folderName[..separator], out var code) ? code : -1;
    }

    private static bool IsSystemCategory(string name)
        => name.Length >= 5 && char.IsDigit(name[0]) && char.IsDigit(name[1]) && name[2..].StartsWith(" - ", StringComparison.Ordinal);

    private string GetCompanyRoot(Guid companyId, string companyCode)
    {
        if (SelectedFolder is { CompanyRootPath.Length: > 0 } selected)
        {
            return selected.CompanyRootPath;
        }

        var company = FindCompanyNode(companyId);
        if (company is not null) return company.FullPath;

        return Path.Combine(
            ArchiveRoot,
            _folderService.CurrentYearLabel,
            companyCode);
    }

    private static string GetTrashFolder(string companyRoot) => Path.Combine(companyRoot, "99 - المحذوفات");

    private string GetTrashFolder(Guid companyId, string companyCode)
    {
        var root = GetCompanyRoot(companyId, companyCode);
        var existing = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).FirstOrDefault(directory => Path.GetFileName(directory).StartsWith("99 - ", StringComparison.Ordinal))
            : null;
        return existing ?? GetTrashFolder(root);
    }

    private static string GetRestoreMetadataPath(string itemPath) => itemPath + ".al-restore.json";

    private static void WriteRestorePath(string movedPath, string originalPath)
        => File.WriteAllText(GetRestoreMetadataPath(movedPath), originalPath);

    private static CompanyListItem? MatchCompany(string folderName, IReadOnlyList<CompanyListItem> companies)
    {
        foreach (var company in companies)
        {
            if (folderName.EndsWith($"_{company.Code}", StringComparison.OrdinalIgnoreCase))
            {
                return company;
            }
        }

        return companies.FirstOrDefault(company =>
            string.Equals(folderName, $"{company.DisplayName}_{company.Code}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsYearFolder(string name)
        => name.Length == 4 && int.TryParse(name, out var year) && year is >= 1900 and <= 2999;

    private static bool IsWithinYear(string path, string yearPath)
        => string.Equals(path, yearPath, StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith(
               Path.TrimEndingDirectorySeparator(yearPath) + Path.DirectorySeparatorChar,
               StringComparison.OrdinalIgnoreCase);
}

public partial class ArchiveFolderNode : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string FolderCode
    {
        get
        {
            var separator = Name.IndexOf(" - ", StringComparison.Ordinal);
            return separator > 0 ? Name[..separator] : string.Empty;
        }
    }
    public string FullPath { get; set; } = string.Empty;
    public string CompanyRootPath { get; set; } = string.Empty;
    public bool IsRoot { get; set; }
    public bool IsYear { get; set; }
    [ObservableProperty] private bool _isExpanded;
    public Guid CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public ObservableCollection<ArchiveFolderNode> Children { get; } = new();
}

public class ArchiveFileItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string Extension { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public ArchiveFolderNode? FolderNode { get; set; }
    public string SizeText => SizeBytes < 1024 ? $"{SizeBytes} B" : SizeBytes < 1024 * 1024 ? $"{SizeBytes / 1024.0:F1} KB" : $"{SizeBytes / (1024.0 * 1024.0):F2} MB";
}
