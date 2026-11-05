using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AlphaLedger.App.Localization;
using AlphaLedger.App.ViewModels;
using AlphaLedger.App.Views;
using AlphaLedger.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AlphaLedger.App;

public static class UiScreenCaptureHelper
{
    public static async Task CaptureScreensAsync(IServiceProvider services, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var mainWindow = services.GetRequiredService<MainWindow>();
        var companyService = services.GetRequiredService<ICompanyService>();
        var settingsService = services.GetRequiredService<ISettingsService>();
        var folderService = services.GetRequiredService<ICompanyFolderService>();
        var dialogService = services.GetRequiredService<Services.WindowsDialogService>();

        mainWindow.Width = 1240;
        mainWindow.Height = 780;
        mainWindow.Show();
        mainWindow.UpdateLayout();
        await Task.Delay(500);

        // 1. Capture Dashboard View in Main Window
        SaveVisual(mainWindow, 1240, 780, Path.Combine(outputDir, "01_dashboard.png"));

        // 2. Open Companies Tab and Capture
        mainWindow.OpenCompaniesScreen();
        mainWindow.UpdateLayout();
        await Task.Delay(500);
        SaveVisual(mainWindow, 1240, 780, Path.Combine(outputDir, "02_companies_list.png"));

        // 3. Open Company Profile Tab and Capture
        var companies = await companyService.GetCompaniesAsync(new Core.Models.CompanyFilter());
        if (companies.Count > 0)
        {
            var target = companies[0];
            mainWindow.OpenCompanyProfile(target.Id, target.DisplayName);
            mainWindow.UpdateLayout();
            await Task.Delay(500);
            SaveVisual(mainWindow, 1240, 780, Path.Combine(outputDir, "03_company_profile.png"));
        }

        // 4. Capture Company Edit Dialog
        var editVm = new CompanyEditViewModel(companyService, settingsService, dialogService);
        await editVm.InitializeAsync();
        var dialog = new CompanyEditDialog(editVm)
        {
            Width = 800,
            Height = 680,
            Owner = mainWindow
        };
        dialog.Show();
        dialog.UpdateLayout();
        await Task.Delay(500);
        SaveVisual(dialog, 800, 680, Path.Combine(outputDir, "04_company_create_dialog.png"));
        dialog.Close();

        // 5. Open Settings Tab and Capture
        mainWindow.OpenSettingsScreen();
        mainWindow.UpdateLayout();
        await Task.Delay(500);
        SaveVisual(mainWindow, 1240, 780, Path.Combine(outputDir, "05_settings.png"));

        // 6. Open Staff Tab and Capture
        mainWindow.OpenStaffScreen();
        mainWindow.UpdateLayout();
        await Task.Delay(500);
        SaveVisual(mainWindow, 1240, 780, Path.Combine(outputDir, "06_staff.png"));

        // 7. Open Reports Tab and Capture
        mainWindow.OpenReportsScreen();
        mainWindow.UpdateLayout();
        await Task.Delay(500);
        SaveVisual(mainWindow, 1240, 780, Path.Combine(outputDir, "07_reports.png"));

        mainWindow.Close();
    }

    private static void SaveVisual(Window win, int width, int height, string filePath)
    {
        var target = (win.Content as Visual) ?? win;

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        // Render with horizontal un-mirror to properly display Arabic RTL visuals in off-screen bitmap
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new MatrixTransform(-1, 0, 0, 1, width, 0));
            dc.DrawRectangle(new VisualBrush(target), null, new Rect(0, 0, width, height));
            dc.Pop();
        }
        rtb.Render(dv);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }
}
