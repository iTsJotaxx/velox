using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velox.Core;
using Velox.Localization;

namespace Velox.UI
{
    /// <summary>
    /// Asistente de primer uso: tres preguntas que afinan la recomendación. No cambia nada de Windows: solo guarda las
    /// respuestas (Ajustes) y recalcula qué se recomienda. Se puede repetir desde Ajustes.
    ///   ¿Portátil?          corrige la detección (planes de energía, modo portátil)
    ///   ¿Juego competitivo? recomienda la pantalla completa exclusiva
    ///   ¿Grabas con OBS?    recomienda que Windows no frene los programas en segundo plano
    /// </summary>
    public static class Wizard
    {
        sealed class Question
        {
            public RadioButton Yes, No;
            public bool? Value => Yes.IsChecked == true ? true : No.IsChecked == true ? false : (bool?)null;
        }

        public static async Task<bool> RunAsync(MainWindow w)
        {
            var st = Settings.Current;
            var sys = AppState.Current.Sys;
            bool detectedLaptop = sys?.IsLaptopDetected ?? false;

            var panel = new StackPanel();
            var qLaptop = AddQuestion(w, panel, Loc.T("wiz.q.laptop"),
                Loc.F("wiz.q.laptopHint", detectedLaptop ? Loc.T("ui.sum.laptop") : Loc.T("ui.sum.desktop")), st.UserLaptop ?? detectedLaptop);
            var qComp = AddQuestion(w, panel, Loc.T("wiz.q.competitive"), Loc.T("wiz.q.competitiveHint"), st.UserCompetitive);
            var qObs = AddQuestion(w, panel, Loc.T("wiz.q.obs"), Loc.T("wiz.q.obsHint"), st.UserObs);

            bool ok = await w.ConfirmAsync(Loc.T("wiz.title"), Loc.T("wiz.text"), null, Loc.T("wiz.save"), Loc.T("wiz.later"), false, panel);
            // «Más tarde» también cuenta como visto: no se vuelve a abrir solo (se puede repetir desde Ajustes)
            st.WizardDone = true;
            if (ok)
            {
                // Portátil: solo se guarda si corrige la detección; si coincide, se sigue lo que detecte Velox
                bool? laptop = qLaptop.Value;
                st.UserLaptop = laptop == detectedLaptop ? (bool?)null : laptop;
                st.UserCompetitive = qComp.Value;
                st.UserObs = qObs.Value;
                Log.Info("Setup questions: laptop=" + (laptop?.ToString() ?? "-") + " (detected " + detectedLaptop + "), competitive="
                         + (st.UserCompetitive?.ToString() ?? "-") + ", OBS=" + (st.UserObs?.ToString() ?? "-") + ".");
            }
            Settings.Save();
            if (ok && sys != null)
            {
                sys.ApplyAnswers(st.UserLaptop, st.UserCompetitive, st.UserObs);
                await AppState.Current.RefreshOptimizationsAsync();
                AppState.Current.RaiseChangesApplied();
                w.ShowToast(Loc.T("wiz.saved"), ToastKind.Success);
            }
            return ok;
        }

        static Question AddQuestion(MainWindow w, Panel parent, string text, string hint, bool? value)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            sp.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = hint, Style = (Style)w.FindResource("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            g.Children.Add(sp);

            string group = "wiz" + Guid.NewGuid().ToString("N");
            var q = new Question
            {
                Yes = new RadioButton { Content = Loc.T("wiz.yes"), Style = (Style)w.FindResource("Segment"), GroupName = group, IsChecked = value == true },
                No = new RadioButton { Content = Loc.T("wiz.no"), Style = (Style)w.FindResource("Segment"), GroupName = group, IsChecked = value == false },
            };
            System.Windows.Automation.AutomationProperties.SetName(q.Yes, text + " " + Loc.T("wiz.yes"));
            System.Windows.Automation.AutomationProperties.SetName(q.No, text + " " + Loc.T("wiz.no"));
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            seg.Children.Add(q.Yes);
            seg.Children.Add(q.No);
            var box = new Border { Background = (Brush)w.FindResource("CardInner"), CornerRadius = new CornerRadius(7), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Center, Child = seg };
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            parent.Children.Add(g);
            return q;
        }
    }
}
