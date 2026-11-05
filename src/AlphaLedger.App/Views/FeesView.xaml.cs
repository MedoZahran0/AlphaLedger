using System.Windows.Controls;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class FeesView : UserControl
{
    public FeesView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is FeesViewModel viewModel)
                await viewModel.InitializeAsync();
        };
    }
}