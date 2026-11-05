using System.Windows.Controls;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class FiscalYearsView : UserControl
{
    public FiscalYearsView(FiscalYearsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}