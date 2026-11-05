using System.Windows.Controls;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class OfficeOperationsView : UserControl
{
    public OfficeOperationsView(OfficeOperationsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}