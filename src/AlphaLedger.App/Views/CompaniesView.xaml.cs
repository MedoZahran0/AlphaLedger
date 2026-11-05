using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class CompaniesView : UserControl
{
    public CompaniesView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is CompaniesViewModel vm)
            {
                await vm.LoadAsync();
            }
        };
    }

    private void CompaniesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CompaniesViewModel vm && vm.Selected != null)
        {
            vm.OpenProfile();
        }
    }

    private void CompaniesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid ||
            ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) is not DataGridRow row)
        {
            return;
        }

        grid.SelectedItem = row.Item;
        row.IsSelected = true;
        row.Focus();
    }
}
