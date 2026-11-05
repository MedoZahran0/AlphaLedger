using System.Windows.Controls;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is DashboardViewModel vm)
            {
                await vm.LoadAsync();
            }
        };
    }
}
