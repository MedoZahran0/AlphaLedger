using System.Windows;
using System.Windows.Markup;

namespace AlphaLedger.App.Localization;

/// <summary>
/// ملحق تحديد لتوجيه واجهة المستخدم حسب اللغة المختارة.
/// الاستخدام في XAML: FlowDirection="{loc:FlowDirection}"
/// يُعطي القيمة الأولية. للتحديث الديناميكي عند تغيير اللغة، استخدم loc:LocalizationExtensions.BindFlowDirection="True"
/// </summary>
public class FlowDirectionExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return LocalizationService.Instance.GetFlowDirection();
    }
}

/// <summary>
/// خاصية مرتبطة لتحديث اتجاه الواجهة تلقائياً عند تغيير اللغة.
/// الاستخدام في XAML: loc:LocalizationExtensions.BindFlowDirection="True"
/// </summary>
public static class LocalizationExtensions
{
    public static readonly DependencyProperty BindFlowDirectionProperty =
        DependencyProperty.RegisterAttached(
            "BindFlowDirection",
            typeof(bool),
            typeof(LocalizationExtensions),
            new PropertyMetadata(false, OnBindFlowDirectionChanged));

    public static bool GetBindFlowDirection(DependencyObject obj)
        => (bool)obj.GetValue(BindFlowDirectionProperty);

    public static void SetBindFlowDirection(DependencyObject obj, bool value)
        => obj.SetValue(BindFlowDirectionProperty, value);

    private static void OnBindFlowDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is FrameworkElement fe)) return;

        if ((bool)e.NewValue)
        {
            // Set initial value
            fe.FlowDirection = LocalizationService.Instance.GetFlowDirection();

            // Subscribe to culture changes for dynamic updates
            LocalizationService.Instance.CultureChanged += () =>
                fe.FlowDirection = LocalizationService.Instance.GetFlowDirection();
        }
    }
}