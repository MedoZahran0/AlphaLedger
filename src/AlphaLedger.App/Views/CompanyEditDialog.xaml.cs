using System.Windows;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class CompanyEditDialog : Window
{
    public CompanyEditDialog(CompanyEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.CloseRequested += success =>
        {
            DialogResult = success;
            Close();
        };

        Loaded += async (s, e) =>
        {
            await viewModel.InitializeAsync();
        };
    }
}
