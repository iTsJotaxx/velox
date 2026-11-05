using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Velox.Core.Optimizations;
using Velox.Core.Scan;
using Velox.Localization;
using Velox.UI.Infra;

namespace Velox.UI.ViewModels
{
    /// <summary>Estado de una optimización en pantalla. IsChecked = lo que quiere el usuario; IsApplied = estado real.</summary>
    public sealed class OptItemVM : ObservableObject
    {
        readonly CategoryVM _parent;
        bool _isChecked, _isApplied, _isAvailable = true, _isRecommended, _scanned;

        public Optimization Opt { get; }
        public OptItemVM(Optimization opt, CategoryVM parent) { Opt = opt; _parent = parent; }

        public bool IsChecked
        {
            get => _isChecked;
            set { if (Set(ref _isChecked, value)) { RaiseState(); _parent.OnItemToggled(this); } }
        }

        /// <summary>Cambia el interruptor sin avisar al padre (cambios en bloque).</summary>
        internal void SetCheckedSilently(bool v) { if (Set(ref _isChecked, v, nameof(IsChecked))) RaiseState(); }

        bool _isVisible = true;
        /// <summary>Filtro del buscador.</summary>
        public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

        public bool IsApplied { get => _isApplied; set { if (Set(ref _isApplied, value)) RaiseState(); } }
        public bool IsAvailable { get => _isAvailable; set { if (Set(ref _isAvailable, value)) RaiseState(); } }
        public bool IsRecommended { get => _isRecommended; set { if (Set(ref _isRecommended, value)) RaiseState(); } }
        bool _isPreference;
        /// <summary>«A tu gusto»: el modo Recomendado no la toca.</summary>
        public bool IsPreference { get => _isPreference; set { if (Set(ref _isPreference, value)) RaiseState(); } }
        /// <summary>Cuenta para el modo Recomendado y la puntuación.</summary>
        public bool IsScored => IsAvailable && !IsPreference;
        public bool Scanned { get => _scanned; set { if (Set(ref _scanned, value)) RaiseState(); } }

        public string Name => Opt.Name;
        public string Description => Opt.Description;
        public string ImpactText => Loc.T("ui.impact." + Opt.Impact.ToString().ToLowerInvariant());
        public string ImpactKind => Opt.Impact.ToString();
        public bool HasRestart => Opt.Restart != RestartNeed.None;
        public string RestartText => Opt.Restart == RestartNeed.Reboot ? Loc.T("ui.restart.reboot") : Opt.Restart == RestartNeed.Logoff ? Loc.T("ui.restart.logoff") : "";

        public bool IsPending => IsAvailable && IsChecked != IsApplied;
        public bool ShowRecommended => Scanned && IsScored && IsRecommended;
        public bool ShowNotRecommended => Scanned && IsScored && !IsRecommended;
        public bool ShowPreference => Scanned && IsAvailable && IsPreference;

        /// <summary>na | pending | on | off</summary>
        public string StatusKind => !IsAvailable ? "na" : IsPending ? "pending" : IsApplied ? "on" : "off";
        public string StatusText =>
            !IsAvailable ? Loc.T("ui.status.na") :
            IsPending ? (IsChecked ? Loc.T("ui.status.willEnable") : Loc.T("ui.status.willDisable")) :
            IsApplied ? Loc.T("ui.status.on") : Loc.T("ui.status.off");

        void RaiseState()
        {
            Raise(nameof(IsPending)); Raise(nameof(StatusKind)); Raise(nameof(StatusText));
            Raise(nameof(ShowRecommended)); Raise(nameof(ShowNotRecommended)); Raise(nameof(ShowPreference));
            Raise(nameof(IsScored));
        }

        public void RefreshTexts() => RaiseAll();
    }

    public sealed class CategoryVM : ObservableObject
    {
        readonly OptimizationsVM _owner;
        bool _isExpanded;

        public Category Cat { get; }
        public ObservableCollection<OptItemVM> Items { get; } = new ObservableCollection<OptItemVM>();

        public CategoryVM(Category c, OptimizationsVM owner) { Cat = c; _owner = owner; }

        public string Name => Cat.Name;
        public string Description => Cat.Description;
        public string Icon => Cat.Icon;
        public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
        public bool HasVisible => Items.Any(i => i.IsVisible);
        internal void RaiseVisible() => Raise(nameof(HasVisible));

        public int Available => Items.Count(i => i.IsAvailable);
        public int Active => Items.Count(i => i.IsAvailable && i.IsApplied);
        public int Pending => Items.Count(i => i.IsPending);
        public bool HasPending => Pending > 0;
        public string CountText => Loc.F("ui.opts.catCount", Active, Available);
        public string PendingText => Loc.F("ui.opts.catPending", Pending);
        public double ActivePct => Available == 0 ? 0 : 100.0 * Active / Available;

        internal void OnItemToggled(OptItemVM item)
        {
            Recount();
            _owner.OnUserToggled();
        }

        public void Recount()
        {
            Raise(nameof(Available)); Raise(nameof(Active)); Raise(nameof(Pending)); Raise(nameof(HasPending));
            Raise(nameof(CountText)); Raise(nameof(PendingText)); Raise(nameof(ActivePct));
        }

        public void RefreshTexts()
        {
            RaiseAll();
            foreach (var i in Items) i.RefreshTexts();
        }
    }

    /// <summary>Lista de optimizaciones por categoría, modos Recomendado / Personalizado y cambios pendientes.</summary>
    public sealed class OptimizationsVM : ObservableObject
    {
        Dictionary<string, bool> _customSelection;
        bool _scanned;

        public ObservableCollection<CategoryVM> Categories { get; } = new ObservableCollection<CategoryVM>();
        public event Action Changed;

        public OptimizationsVM()
        {
            foreach (var c in Catalog.Categories)
            {
                var vm = new CategoryVM(c, this);
                foreach (var o in Catalog.All.Where(o => o.Category == c.Id)) vm.Items.Add(new OptItemVM(o, vm));
                if (vm.Items.Count > 0) Categories.Add(vm);
            }
            if (Categories.Count > 0) Categories[0].IsExpanded = true;
        }

        public IEnumerable<OptItemVM> AllItems => Categories.SelectMany(c => c.Items);

        public int PendingCount => AllItems.Count(i => i.IsPending);
        public bool HasPending => PendingCount > 0;
        public int AvailableCount => AllItems.Count(i => i.IsAvailable);
        public int ActiveCount => AllItems.Count(i => i.IsAvailable && i.IsApplied);
        /// <summary>Ajustes que cuentan para la puntuación (disponibles y no «a tu gusto»).</summary>
        public int ScoredCount => AllItems.Count(i => i.IsScored);
        /// <summary>Ajustes cuyo estado real ya es el recomendado.</summary>
        public int MatchCount => AllItems.Count(i => i.IsScored && i.IsApplied == i.IsRecommended);
        public int RecommendedDiff => AllItems.Count(i => i.IsScored && i.IsApplied != i.IsRecommended);
        public bool Scanned => _scanned;

        /// <summary>Los interruptores coinciden con la configuración recomendada (los «a tu gusto» no cuentan).</summary>
        public bool IsRecommendedMode => _scanned && AllItems.Where(i => i.IsScored).All(i => i.IsChecked == i.IsRecommended);
        public bool IsCustomMode => !IsRecommendedMode;

        public string ApplyText => PendingCount > 0 ? Loc.F("ui.opts.applyN", PendingCount) : Loc.T("ui.opts.apply");
        public string ModeHint =>
            !_scanned ? Loc.T("ui.opts.hintScanning") :
            IsRecommendedMode && PendingCount > 0 ? Loc.F("ui.opts.hintRecPending", PendingCount) :
            IsRecommendedMode ? Loc.T("ui.opts.hintRecDone") : Loc.T("ui.opts.hintCustom");

        /// <summary>Lee el estado real de todo en segundo plano (powercfg, registro...) y lo refleja.</summary>
        public async Task RefreshAsync(SystemInfo sys)
        {
            var states = await Task.Run(() => Catalog.All.ToDictionary(o => o.Id, o => new
            {
                Available = OptimizationEngine.IsAvailable(o, sys),
                Applied = OptimizationEngine.IsApplied(o),
                Recommended = OptimizationEngine.IsRecommended(o, sys),
                Preference = OptimizationEngine.IsPreference(o, sys),
            }));
            _scanned = sys != null;
            foreach (var i in AllItems)
            {
                var st = states[i.Opt.Id];
                i.IsAvailable = st.Available;
                i.IsApplied = st.Applied;
                i.IsRecommended = st.Available && st.Recommended;
                i.IsPreference = st.Available && st.Preference;
                i.Scanned = _scanned;
                i.SetCheckedSilently(st.Applied);
            }
            foreach (var c in Categories) c.Recount();
            NotifyAll();
        }

        internal void OnUserToggled()
        {
            SaveCustomSelection();
            NotifyAll();
        }

        void SaveCustomSelection()
        {
            if (IsRecommendedMode) return;
            _customSelection = AllItems.Where(i => i.IsAvailable).ToDictionary(i => i.Opt.Id, i => i.IsChecked);
        }

        /// <summary>
        /// Cada interruptor pasa a su estado recomendado (activar o desactivar). Los «a tu gusto» se quedan como
        /// estén ahora en Windows. Devuelve cuántos cambios quedan pendientes.
        /// </summary>
        public int SelectRecommended()
        {
            SaveCustomSelection();
            foreach (var i in AllItems)
            {
                if (i.IsScored) i.SetCheckedSilently(i.IsRecommended);
                else if (i.IsAvailable) i.SetCheckedSilently(i.IsApplied);
            }
            foreach (var c in Categories) c.Recount();
            NotifyAll();
            return PendingCount;
        }

        /// <summary>Recupera la última selección hecha a mano.</summary>
        public void SelectCustom()
        {
            if (_customSelection != null)
                foreach (var i in AllItems)
                    if (i.IsAvailable && _customSelection.TryGetValue(i.Opt.Id, out bool v)) i.SetCheckedSilently(v);
            foreach (var c in Categories) c.Recount();
            NotifyAll();
        }

        public void Discard()
        {
            foreach (var i in AllItems) i.SetCheckedSilently(i.IsApplied);
            foreach (var c in Categories) c.Recount();
            NotifyAll();
        }

        public List<OptItemVM> GetPending() => AllItems.Where(i => i.IsPending).ToList();

        public void SetAllExpanded(bool expanded) { foreach (var c in Categories) c.IsExpanded = expanded; }

        /// <summary>Filtra por texto en nombre o descripción y abre las categorías con resultados.</summary>
        public void ApplyFilter(string text)
        {
            text = (text ?? "").Trim();
            foreach (var c in Categories)
            {
                foreach (var i in c.Items)
                    i.IsVisible = text.Length == 0 ||
                        i.Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                        i.Description.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
                c.RaiseVisible();
                if (text.Length > 0) c.IsExpanded = c.HasVisible;
            }
        }

        public void RefreshTexts()
        {
            foreach (var c in Categories) c.RefreshTexts();
            NotifyAll();
        }

        void NotifyAll()
        {
            RaiseAll();
            Changed?.Invoke();
        }
    }
}
