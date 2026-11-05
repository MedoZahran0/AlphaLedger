using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Globalization;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public class BoolToToggleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "-" : "+";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public partial class ArchiveView : UserControl
{
    public ArchiveView()
    {
        InitializeComponent();
        AllowDrop = true;
        Drop += ArchiveView_Drop;
    }

    private async void ArchiveView_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (DataContext is ArchiveViewModel vm)
                await vm.ImportFilesAsync(files);
        }
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ArchiveViewModel vm && vm.SelectedFile is not null)
            vm.OpenFile(vm.SelectedFile);
    }

    private void ArchiveView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Back or Key.BrowserBack) || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Keyboard.FocusedElement is TextBox textBox && !string.IsNullOrEmpty(textBox.Text)) return;
        if (DataContext is ArchiveViewModel vm && vm.CanNavigateBack)
        {
            e.Handled = true;
            vm.NavigateBack();
        }
    }

    private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dg)
        {
            var row = ItemsControl.ContainerFromElement(dg, e.OriginalSource as DependencyObject) as DataGridRow;
            if (row is not null && row.Item is ArchiveFileItem item)
            {
                dg.SelectedItem = item;
                if (DataContext is ArchiveViewModel vm)
                {
                    if (!dg.SelectedItems.Contains(item)) dg.SelectedItems.Clear();
                    dg.SelectedItem = item;
                    vm.SelectedFile = item;
                }
            }
        }
    }

    private void FolderItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem item && item.DataContext is ArchiveFolderNode folder && DataContext is ArchiveViewModel vm)
        {
            item.IsSelected = true;
            vm.SelectedFolder = folder;
        }
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ArchiveFolderNode folder && DataContext is ArchiveViewModel vm)
            vm.SelectedFolder = folder;
    }
    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ArchiveViewModel vm && sender is DataGrid dg)
        {
            vm.SelectedFiles.Clear();
            foreach (var item in dg.SelectedItems)
                if (item is ArchiveFileItem f) vm.SelectedFiles.Add(f);
        }
    }
}
