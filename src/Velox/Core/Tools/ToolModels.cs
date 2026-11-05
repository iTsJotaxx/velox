using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Velox.Core.Tools
{
    /// <summary>Programa que se abre al iniciar Windows.</summary>
    public sealed class StartupEntry : INotifyPropertyChanged
    {
        bool _enabled;
        public string Name { get; set; }
        public string Command { get; set; }
        /// <summary>Origen ya traducido (Usuario, Todos los usuarios, Carpeta Inicio...).</summary>
        public string Source { get; set; }
        public string ApprovedPath { get; set; }
        public string ValueName { get; set; }
        public bool Enabled { get => _enabled; set { _enabled = value; OnChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnChanged([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    /// <summary>Procesos agrupados por nombre.</summary>
    public sealed class ProcEntry
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public double RamMB { get; set; }
        /// <summary>% de CPU total del equipo; -1 si no se pudo medir.</summary>
        public double Cpu { get; set; }
        /// <summary>Prioridad ya traducida.</summary>
        public string Priority { get; set; }
        public string Description { get; set; }
        public string Path { get; set; }
        public bool IsProtected { get; set; }
        public string RamText => RamMB.ToString("N0") + " MB";
        public string CpuText => Cpu < 0 ? "-" : Cpu.ToString("0.0") + " %";
    }

    public sealed class MemoryLists
    {
        public double StandbyGB { get; set; }
        public double FreeGB { get; set; }
    }

    public enum RestorePointResult { Created, SkippedRecent, Failed }

    public sealed class RestorePointOutcome
    {
        public RestorePointResult Result { get; set; }
        /// <summary>Texto ya traducido para mostrar.</summary>
        public string Message { get; set; }
    }
}
