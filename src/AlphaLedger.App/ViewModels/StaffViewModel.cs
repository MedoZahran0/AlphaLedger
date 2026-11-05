using System.Collections.ObjectModel;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

/// <summary>Staff and employees screen: lists active employees and allows quick adding.</summary>
public partial class StaffViewModel : ObservableObject
{
    private readonly ICompanyService _companies;
    private readonly WindowsDialogService _dialogs;

    public StaffViewModel(ICompanyService companies, WindowsDialogService dialogs)
    {
        _companies = companies;
        _dialogs = dialogs;
    }

    public ObservableCollection<Employee> Employees { get; } = [];

    [ObservableProperty] private Employee? _selectedEmployee;
    [ObservableProperty] private string _newEmployeeName = string.Empty;
    [ObservableProperty] private string _newEmployeeJobTitle = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var employees = await _companies.GetActiveEmployeesAsync();
            Employees.Clear();
            foreach (var employee in employees)
            {
                Employees.Add(employee);
            }

            StatusMessage = Employees.Count == 0 ? Text("StaffNoEmployees") : string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task AddAsync()
    {
        var name = NewEmployeeName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _dialogs.Warning(Text("StaffNameRequired"));
            return;
        }

        try
        {
            var jobTitle = string.IsNullOrWhiteSpace(NewEmployeeJobTitle) ? null : NewEmployeeJobTitle.Trim();
            var employee = await _companies.QuickAddEmployeeAsync(name, jobTitle);
            NewEmployeeName = string.Empty;
            NewEmployeeJobTitle = string.Empty;
            await LoadAsync();
            SelectedEmployee = Employees.FirstOrDefault(item => item.Id == employee.Id);
            StatusMessage = Text("StaffAdded");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        var employee = SelectedEmployee;
        if (employee is null)
        {
            _dialogs.Warning(Text("StaffSelectEmployee"));
            return;
        }

        if (!_dialogs.Confirm(string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Text("StaffDeleteConfirm"),
                employee.FullName)))
        {
            return;
        }

        try
        {
            await _companies.DeleteEmployeeAsync(employee.Id);
            Employees.Remove(employee);
            SelectedEmployee = null;
            StatusMessage = Text("StaffDeleted");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);
}
