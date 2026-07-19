using System.IO;
using System.Windows.Controls;
using System.Windows;
using Microsoft.Win32;

namespace ApexTrace.App.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private void SettingsSection_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is Button { Tag: string section })
        {
            viewModel.SettingsSection = section;
        }
    }

    private void SelectInstallationFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        var currentPath = viewModel.Connection.InstallationPath;
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationManager.Translate("选择 Le Mans Ultimate 安装目录"),
            InitialDirectory = Directory.Exists(currentPath)
                ? currentPath
                : ApexTrace.Lmu.LmuInstallationProbe.DefaultInstallationPath,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            viewModel.SetLmuInstallationPath(dialog.FolderName);
        }
    }
}
