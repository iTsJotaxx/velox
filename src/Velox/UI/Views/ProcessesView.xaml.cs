using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Velox.Core;
using Velox.Core.Tools;
using Velox.Localization;

namespace Velox.UI.Views
{
    public partial class ProcessesView : UserControl, IPage
    {
        List<ProcEntry> _items = new List<ProcEntry>();
        bool _loaded, _loading;

        public ProcessesView()
        {
            InitializeComponent();
            UpdateButtons();
            LoadGames();
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);
        ProcEntry Selected => List.SelectedItem as ProcEntry;

        public async void OnShown() { if (!_loaded) await LoadAsync(); LoadGames(); }
        public void RefreshTexts() => UpdateSummary();

        async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            TxtSummary.Text = Loc.T("ui.proc.measuring");
            try { _items = await ProcessManager.GetGroupsAsync(); _loaded = true; }
            catch (Exception ex) { Log.Error("Process list", ex); }
            finally { _loading = false; }
            List.ItemsSource = _items;
            UpdateSummary();
            UpdateButtons();
        }

        void UpdateSummary()
        {
            if (!_loaded) return;
            double gb = _items.Sum(i => i.RamMB) / 1024.0;
            TxtSummary.Text = Loc.F("ui.proc.summary", _items.Count, gb.ToString("0.0"));
        }

        void UpdateButtons()
        {
            var s = Selected;
            bool any = s != null, safe = any && !s.IsProtected;
            BtnHigh.IsEnabled = BtnNormal.IsEnabled = BtnKill.IsEnabled = safe;
            BtnGame.IsEnabled = safe && !string.IsNullOrEmpty(s.Path);
        }

        void LoadGames()
        {
            var games = GameOptimizer.List();
            Games.ItemsSource = games;
            Games.Visibility = games.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtNoGames.Visibility = games.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            BtnRemoveGame.IsEnabled = games.Count > 0;
        }

        void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
        async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        async void SetPriority(ProcessPriorityClass p)
        {
            var s = Selected;
            if (s == null) return;
            try
            {
                int n = ProcessManager.SetPriority(s.Name, p);
                Win.ShowToast(Loc.F("ui.proc.priorityDone", s.Name, n), ToastKind.Success);
            }
            catch (Exception ex) { Win.ShowToast(ex.Message, ToastKind.Error); }
            await LoadAsync();
        }

        void High_Click(object sender, RoutedEventArgs e) => SetPriority(ProcessPriorityClass.High);
        void Normal_Click(object sender, RoutedEventArgs e) => SetPriority(ProcessPriorityClass.Normal);

        async void Game_Click(object sender, RoutedEventArgs e)
        {
            var s = Selected;
            if (s == null || string.IsNullOrEmpty(s.Path)) return;
            string exe = Path.GetFileName(s.Path);
            if (!await Win.ConfirmAsync(Loc.F("ui.proc.gameConfirmTitle", exe), Loc.T("ui.proc.gameConfirmText"), null, Loc.T("ui.proc.gameConfirmOk"), Loc.T("ui.btn.cancel"))) return;
            try
            {
                GameOptimizer.Optimize(s.Path);
                Win.ShowToast(Loc.F("ui.proc.gameDone", exe), ToastKind.Success);
            }
            catch (Exception ex) { Win.ShowToast(ex.Message, ToastKind.Error); }
            LoadGames();
        }

        async void Kill_Click(object sender, RoutedEventArgs e)
        {
            var s = Selected;
            if (s == null) return;
            if (!await Win.ConfirmAsync(Loc.F("ui.proc.killTitle", s.Name, s.Count), Loc.T("ui.proc.killText"), null, Loc.T("ui.proc.kill"), Loc.T("ui.btn.cancel"), true)) return;
            try
            {
                int n = ProcessManager.Kill(s.Name);
                Win.ShowToast(Loc.F("ui.proc.killDone", s.Name, n), ToastKind.Success);
            }
            catch (Exception ex) { Win.ShowToast(ex.Message, ToastKind.Error); }
            await LoadAsync();
        }

        void RemoveGame_Click(object sender, RoutedEventArgs e)
        {
            if (!(Games.SelectedItem is string exe)) { Win.ShowToast(Loc.T("ui.proc.selectGame"), ToastKind.Warning); return; }
            try
            {
                GameOptimizer.Remove(exe);
                Win.ShowToast(Loc.F("ui.proc.gameRemoved", exe), ToastKind.Success);
            }
            catch (Exception ex) { Win.ShowToast(ex.Message, ToastKind.Error); }
            LoadGames();
        }
    }
}
