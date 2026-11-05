using CommunityToolkit.Mvvm.ComponentModel;

namespace AlphaLedger.App.ViewModels;

public partial class WorkspaceTab : ObservableObject
{
    public WorkspaceTab(string key, string title, object content, bool canClose = true)
    {
        Key = key;
        Title = title;
        Content = content;
        CanClose = canClose;
    }

    public string Key { get; }
    [ObservableProperty] private string _title;
    public object Content { get; }
    public bool CanClose { get; }
}
