using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Velox.Core;
using Velox.Localization;

namespace Velox.UI.Views
{
    public partial class SettingsView : UserControl, IPage
    {
        public SettingsView()
        {
            InitializeComponent();
            TxtDataPath.Text = AppPaths.DataDir;
            TxtAbout.Text = "Velox " + AppInfo.Version;
            Load();
        }

        void Load()
        {
            RbEs.IsChecked = Loc.Language == "es";
            RbEn.IsChecked = Loc.Language == "en";
            ChkRestore.IsChecked = Settings.Current.CreateRestorePoint;
            ChkExpand.IsChecked = Settings.Current.ExpandAllCategories;
        }

        public void OnShown() => Load();
        public void RefreshTexts() => Load();

        void Lang_Click(object sender, RoutedEventArgs e)
        {
            string lang = (string)((RadioButton)sender).Tag;
            if (lang == Loc.Language) return;
            Settings.Current.Language = lang;
            Settings.Save();
            Loc.SetLanguage(lang);
        }

        void Prefs_Click(object sender, RoutedEventArgs e)
        {
            Settings.Current.CreateRestorePoint = ChkRestore.IsChecked == true;
            Settings.Current.ExpandAllCategories = ChkExpand.IsChecked == true;
            Settings.Save();
        }

        void OpenData_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", "\"" + AppPaths.DataDir + "\"");
        void GitHub_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(AppInfo.RepositoryUrl) { UseShellExecute = true });
    }
}
