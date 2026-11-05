using CommunityToolkit.Mvvm.ComponentModel;

namespace AlphaLedger.App.ViewModels;

public partial class ComingSoonViewModel : ObservableObject
{
    public ComingSoonViewModel(string title, string details)
    {
        Title = title;
        Details = details;
    }

    public string Title { get; }
    public string Details { get; }
}
