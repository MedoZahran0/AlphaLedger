using System.Windows.Controls;
using System.Windows;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class CompanyProfileView : UserControl
{
    public CompanyProfileView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is CompanyProfileViewModel vm)
                await vm.LoadAsync();
        };
    }
}
