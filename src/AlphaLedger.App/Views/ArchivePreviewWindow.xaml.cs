using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace AlphaLedger.App.Views;

public partial class ArchivePreviewWindow : Window
{
    private readonly string _path;
    private Image? _image;
    private ScaleTransform? _scale;

    public ArchivePreviewWindow(string path)
    {
        InitializeComponent();
        _path = path;
        Title = System.IO.Path.GetFileName(path);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var ext = System.IO.Path.GetExtension(_path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp")
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(_path);
            bitmap.EndInit();
            _image = new Image { Source = bitmap };
            _scale = new ScaleTransform(1, 1);
            _image.RenderTransform = _scale;
            var scroll = new ScrollViewer { Content = _image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            ContentHost.Children.Add(scroll);
        }
        else if (ext == ".pdf")
        {
            var wv = new WebView2();
            ContentHost.Children.Add(wv);
            try
            {
                await wv.EnsureCoreWebView2Async(null);
                wv.CoreWebView2.Navigate(new Uri(_path).AbsoluteUri);
            }
            catch (Exception ex)
            {
                ContentHost.Children.Clear();
                ContentHost.Children.Add(new TextBlock { Text = $"تعذر فتح معاينة PDF: {ex.Message}", Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap });
            }
        }
        else
        {
            ContentHost.Children.Add(new TextBlock { Text = "Preview not supported for this file type.", Margin = new Thickness(20) });
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null || _scale is null) return;
        // reset
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (_scale is null) return;
        _scale.ScaleX *= 1.25;
        _scale.ScaleY *= 1.25;
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (_scale is null) return;
        _scale.ScaleX /= 1.25;
        _scale.ScaleY /= 1.25;
    }
}
