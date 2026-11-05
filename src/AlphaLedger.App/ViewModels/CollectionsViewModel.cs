using System.Collections.ObjectModel;
using AlphaLedger.App.Services;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using AlphaLedger.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

public partial class CollectionsViewModel : ObservableObject
{
    private readonly ICollectionService _collectionService;
    private readonly IFeeService _feeService;
    private readonly ICompanyService _companyService;
    private readonly IArchiveAttachmentService _attachmentService;
    private readonly WindowsDialogService _dialogs;

    public CollectionsViewModel(ICollectionService collectionService, IFeeService feeService, ICompanyService companyService, IArchiveAttachmentService attachmentService, WindowsDialogService dialogs)
    {
        _collectionService = collectionService;
        _feeService = feeService;
        _companyService = companyService;
        _attachmentService = attachmentService;
        _dialogs = dialogs;

        Items = new ObservableCollection<CollectionListItem>();
        Fees = new ObservableCollection<FeeListItem>();
        Companies = new ObservableCollection<CompanyListItem>();

        FilterStatuses = new ObservableCollection<FeeStatus>
        {
            FeeStatus.Agreed,
            FeeStatus.PartiallyCollected,
            FeeStatus.Collected,
            FeeStatus.Cancelled
        };
    }

    public ObservableCollection<CollectionListItem> Items { get; }
    public ObservableCollection<FeeListItem> Fees { get; }
    public ObservableCollection<CompanyListItem> Companies { get; }

    [ObservableProperty] private FeeStatus _filterStatus = FeeStatus.Agreed;
    [ObservableProperty] private Guid _filterCompanyId;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _showOnlyUncollected;

    [ObservableProperty] private CollectionListItem? _selectedCollection;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<FeeStatus> FilterStatuses { get; }
    [RelayCommand]
    public async Task AttachDocumentAsync()
    {
        if (SelectedCollection is null) return;
        var company = Companies.FirstOrDefault(c => c.Id == SelectedCollection.CompanyId);
        if (company is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = false };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await _attachmentService.AttachAsync(company.Id, company.Code, "Collection", SelectedCollection.Id, 10, dlg.FileName);
            StatusMessage = "Attached";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

}
