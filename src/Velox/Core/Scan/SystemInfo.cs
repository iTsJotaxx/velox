using System;
using System.Collections.Generic;
using Velox.Native;

namespace Velox.Core.Scan
{
    public sealed class GpuInfo
    {
        public string Name { get; set; }
        public double VramGB { get; set; }
        public string Driver { get; set; }
        public DateTime? DriverDate { get; set; }
        public bool Dedicated { get; set; }
        /// <summary>Compatible con programación de GPU por hardware (GTX 10xx+, RX 5000+, Arc).</summary>
        public bool Modern { get; set; }
    }

    public sealed class DiskInfo
    {
        public string Name { get; set; }
        public string MediaType { get; set; }   // "SSD", "HDD" o "Unknown"
        public string Bus { get; set; }         // "NVMe", "SATA", "USB"...
        public double SizeGB { get; set; }
        public string Health { get; set; }      // "Healthy", "Warning", "Unhealthy", "Unknown"
        public bool IsSystem { get; set; }
    }

    public sealed class TopApp
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public double MB { get; set; }
    }

    /// <summary>Resultado del escaneo del equipo. Lo rellena SystemScanner y lo usan las recomendaciones.</summary>
    public sealed class SystemInfo
    {
        public string OS { get; set; }
        public string Version { get; set; }
        public int Build { get; set; }
        public double UptimeDays { get; set; }

        public string Cpu { get; set; }
        public int Cores { get; set; }
        public int Threads { get; set; }

        public double RamTotalGB { get; set; }
        public double RamFreeGB { get; set; }
        public int RamUsedPct { get; set; }
        public int RamModules { get; set; }
        public int RamSpeed { get; set; }        // MT/s configurados
        public string RamType { get; set; }      // DDR4, DDR5...

        public List<GpuInfo> Gpus { get; set; } = new List<GpuInfo>();
        public bool HasDedicatedGpu { get; set; }
        public bool ModernGpu { get; set; }
        public double VramGB { get; set; }
        public List<DisplayInfo> Displays { get; set; } = new List<DisplayInfo>();

        public bool IsLaptop { get; set; }
        /// <summary>Lo que detectó Velox (IsLaptop puede corregirlo la persona en el asistente).</summary>
        public bool IsLaptopDetected { get; set; }
        public bool OnBattery { get; set; }
        /// <summary>Asistente: juega a juegos competitivos (shooters, MOBA...).</summary>
        public bool Competitive { get; set; }
        /// <summary>Asistente: graba o retransmite con OBS mientras juega.</summary>
        public bool RecordsObs { get; set; }

        /// <summary>
        /// Aplica las respuestas del asistente sobre lo detectado. Se llama tras cada escaneo (también en el modo sin
        /// ventana), así que la recomendación siempre las tiene en cuenta.
        /// </summary>
        public void ApplyAnswers(bool? laptop, bool? competitive, bool? obs)
        {
            IsLaptopDetected = IsLaptopDetected || IsLaptop;
            IsLaptop = laptop ?? IsLaptopDetected;
            Competitive = competitive == true;
            RecordsObs = obs == true;
        }

        public string DiskType { get; set; } = "Unknown";   // del disco del sistema: "SSD", "HDD", "Unknown"
        public string DiskModel { get; set; }
        public string DiskBus { get; set; }
        public double DiskFreeGB { get; set; }
        public double DiskSizeGB { get; set; }
        public int DiskFreePct { get; set; } = 100;
        public List<DiskInfo> Disks { get; set; } = new List<DiskInfo>();

        public string PowerScheme { get; set; }
        public string Overlay { get; set; }
        public bool Hvci { get; set; }
        public bool PerfCountersOff { get; set; }
        public bool EdgeInstalled { get; set; }
        /// <summary>Edge es el navegador predeterminado (entonces su precarga sí es útil).</summary>
        public bool EdgeIsDefaultBrowser { get; set; }
        public bool OtherAccount { get; set; }
        public string ShellUser { get; set; }

        public int ProcCount { get; set; }
        public List<TopApp> TopApps { get; set; } = new List<TopApp>();
        public int StartupEnabled { get; set; }
        public bool HibernationEnabled { get; set; }
        /// <summary>Windows (UCPD) impide a los programas cambiar el botón de Widgets.</summary>
        public bool WidgetsLocked { get; set; }

        public DateTime ScanTime { get; set; }
    }
}
