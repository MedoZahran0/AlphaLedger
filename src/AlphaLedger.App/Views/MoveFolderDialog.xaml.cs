using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class MoveFolderDialog : Window
{
    public ArchiveFolderNode? SelectedFolder { get; private set; }

    public MoveFolderDialog(ObservableCollection<ArchiveFolderNode> folders)
    {
        InitializeComponent();
        DataContext = new { Folders = folders };
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        SelectedFolder = e.NewValue as ArchiveFolderNode;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}