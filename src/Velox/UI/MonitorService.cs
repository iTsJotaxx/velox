using System;
using System.Threading;
using Velox.Native;

namespace Velox.UI
{
    public sealed class MonitorSample
    {
        public double Cpu = -1, RamUsed, RamTotal, Gpu = -1, Vram = -1, VramTotal;
    }

    /// <summary>
    /// Mide CPU, RAM, GPU y VRAM cada segundo en un hilo aparte. No depende de los contadores de rendimiento
    /// de Windows (pueden estar desactivados): CPU con GetSystemTimes, GPU/VRAM con NVML (NVIDIA) o PDH (resto).
    /// </summary>
    public sealed class MonitorService
    {
        Thread _thread;
        volatile bool _stop;
        public event Action<MonitorSample> Sampled;

        public void Start()
        {
            if (_thread != null) return;
            _stop = false;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Velox monitor", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public void Stop() => _stop = true;

        void Loop()
        {
            bool nvml = false;
            try { nvml = Nvml.Available; } catch { }
            PdhSet pdh = null;
            if (!nvml)
            {
                try
                {
                    pdh = new PdhSet();
                    pdh.Add("gpu", @"\GPU Engine(*engtype_3D)\Utilization Percentage");
                    pdh.Add("vram", @"\GPU Adapter Memory(*)\Dedicated Usage");
                    if (!pdh.Collect()) pdh = null;
                }
                catch { pdh = null; }
            }
            try { CpuMeter.Sample(); } catch { }

            while (!_stop)
            {
                Thread.Sleep(1000);
                if (_stop) break;
                var s = new MonitorSample();
                try { s.Cpu = CpuMeter.Sample(); } catch { }
                try
                {
                    if (MemoryStatus.Read(out double total, out double avail)) { s.RamTotal = total; s.RamUsed = total - avail; }
                }
                catch { }
                if (nvml)
                {
                    try
                    {
                        if (Nvml.Read(out double g, out double used, out double vt)) { s.Gpu = g; s.Vram = used; s.VramTotal = vt; }
                    }
                    catch { }
                }
                else if (pdh != null)
                {
                    try
                    {
                        if (pdh.Collect())
                        {
                            if (pdh.Has("gpu")) { double v = pdh.MaxByAdapter("gpu"); if (v >= 0) s.Gpu = Math.Min(100, v); }
                            if (pdh.Has("vram")) { double v = pdh.Max("vram"); if (v >= 0) s.Vram = v; }
                        }
                    }
                    catch { }
                }
                try { Sampled?.Invoke(s); } catch { }
            }
        }
    }
}
