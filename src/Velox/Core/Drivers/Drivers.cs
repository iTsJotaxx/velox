using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Velox.Core.Drivers
{
    // Modelos y firmas públicas del módulo de drivers. La interfaz depende de ellas: no cambiarlas.

    /// <summary>Driver instalado de un dispositivo (Win32_PnPSignedDriver).</summary>
    public sealed class DeviceDriver
    {
        public string DeviceName { get; set; }
        public string DeviceClass { get; set; }
        /// <summary>gpu, network, audio, bluetooth, storage, chipset, input, usb, other</summary>
        public string Category { get; set; }
        public string Manufacturer { get; set; }
        public string Provider { get; set; }
        public string DriverVersion { get; set; }
        public DateTime? DriverDate { get; set; }
        public string DeviceId { get; set; }
        public string HardwareId { get; set; }
        public string InfName { get; set; }
        public string DateText => DriverDate.HasValue ? DriverDate.Value.ToString("d") : "-";
    }

    public enum DriverSource { WindowsUpdate, Nvidia }

    /// <summary>Actualización disponible para un dispositivo concreto.</summary>
    public sealed class DriverUpdate : INotifyPropertyChanged
    {
        bool _selected = true;
        public string Key { get; set; }
        public DriverSource Source { get; set; }
        public string Title { get; set; }
        public string DeviceName { get; set; }
        public string DeviceId { get; set; }
        public string Category { get; set; }
        public string CurrentVersion { get; set; }
        public DateTime? CurrentDate { get; set; }
        public string NewVersion { get; set; }
        public DateTime? NewDate { get; set; }
        public double SizeMB { get; set; }
        public string ReleaseNotesUrl { get; set; }
        public bool Selected { get => _selected; set { _selected = value; OnChanged(); } }
        /// <summary>Datos internos de la fuente (objeto IUpdate de Windows Update o datos de NVIDIA).</summary>
        internal object Payload { get; set; }

        public string SourceText => Source == DriverSource.Nvidia ? "NVIDIA" : "Windows Update";
        public string NewDateText => NewDate.HasValue ? NewDate.Value.ToString("d") : "";
        public string CurrentDateText => CurrentDate.HasValue ? CurrentDate.Value.ToString("d") : "";
        public string SizeText => SizeMB >= 1024 ? (SizeMB / 1024).ToString("0.0") + " GB" : SizeMB.ToString("0") + " MB";

        public event PropertyChangedEventHandler PropertyChanged;
        void OnChanged([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public sealed class DriverInstallResult
    {
        public int Installed { get; set; }
        public int Failed { get; set; }
        public bool RebootRequired { get; set; }
        /// <summary>No se instaló nada porque no era seguro (reinicio pendiente, batería...). Los motivos van en Messages.</summary>
        public bool Blocked { get; set; }
        /// <summary>Una línea por driver, ya traducida.</summary>
        public List<string> Messages { get; } = new List<string>();
    }

    /// <summary>Gráfica AMD o Intel: Velox no instala sus drivers, abre la herramienta oficial.</summary>
    public sealed class GpuVendorTool
    {
        public string Vendor { get; set; }
        public string GpuName { get; set; }
        public string DriverVersion { get; set; }
        public string ToolUrl { get; set; }
    }

    public static class DriverInventory
    {
        /// <summary>Dispositivos con driver, sin los puramente de software. Llamar en segundo plano.</summary>
        public static List<DeviceDriver> GetInstalled()
        {
            try
            {
                var list = PnpReader.GetInstalled();
                Log.Info("Driver inventory: " + list.Count + " device(s).");
                return list;
            }
            catch (Exception ex)
            {
                Log.Error("Driver inventory", ex);
                return new List<DeviceDriver>();
            }
        }
    }

    public static class DriverUpdater
    {
        /// <summary>Avisos de la última búsqueda (fuente que no respondió...), ya traducidos.</summary>
        public static List<string> LastSearchWarnings { get; } = new List<string>();

        /// <summary>Busca en Windows Update y en NVIDIA. Si una fuente falla, sigue con la otra y lo anota en LastSearchWarnings.</summary>
        public static Task<List<DriverUpdate>> SearchAsync(IProgress<string> progress)
        {
            LastSearchWarnings.Clear();
            return Task.Run(() => DriverEngine.SearchAsync(progress));
        }

        /// <summary>Descarga, verifica e instala. Nunca lanza por un driver concreto: lo anota en el resultado.</summary>
        public static Task<DriverInstallResult> InstallAsync(IList<DriverUpdate> updates, IProgress<string> progress, CancellationToken ct) =>
            Task.Run(() => DriverEngine.InstallAsync(updates, progress, ct));

        /// <summary>Gráficas AMD/Intel detectadas con el enlace a su herramienta oficial.</summary>
        public static List<GpuVendorTool> OtherGpuVendors() => DriverEngine.OtherGpuVendors();
    }

    public static class DriverRollback
    {
        /// <summary>Vuelve al driver anterior del dispositivo. Devuelve el mensaje traducido; lanza con mensaje traducido si no se puede.</summary>
        public static Task<string> RollbackAsync(DeviceDriver device) => Task.Run(() => RollbackNative.Rollback(device));
    }
}
