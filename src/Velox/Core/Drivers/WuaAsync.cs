using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Velox.Core.Drivers
{
    // Interfaces de retorno de llamada de la API de Windows Update (wuapi.idl). Los IID están comprobados en el registro
    // (HKCR\Interface). Los parámetros son punteros a interfaz: se reciben como IUnknown y no se usan.

    [ComImport, Guid("88AEE058-D4B0-4725-A2F1-814A67AE964C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWuSearchCompletedCallback
    {
        void Invoke([MarshalAs(UnmanagedType.IUnknown)] object searchJob, [MarshalAs(UnmanagedType.IUnknown)] object callbackArgs);
    }

    [ComImport, Guid("8C3F1CDD-6173-4591-AEBD-A56A53CA77C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWuDownloadProgressChangedCallback
    {
        void Invoke([MarshalAs(UnmanagedType.IUnknown)] object downloadJob, [MarshalAs(UnmanagedType.IUnknown)] object callbackArgs);
    }

    [ComImport, Guid("77254866-9F5B-4C8E-B9E2-C77A8530D64B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWuDownloadCompletedCallback
    {
        void Invoke([MarshalAs(UnmanagedType.IUnknown)] object downloadJob, [MarshalAs(UnmanagedType.IUnknown)] object callbackArgs);
    }

    [ComImport, Guid("E01402D5-F8DA-43BA-A012-38894BD048F1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWuInstallationProgressChangedCallback
    {
        void Invoke([MarshalAs(UnmanagedType.IUnknown)] object installationJob, [MarshalAs(UnmanagedType.IUnknown)] object callbackArgs);
    }

    [ComImport, Guid("45F4F6F3-D602-4F98-9A8A-3EFA152AD2D3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWuInstallationCompletedCallback
    {
        void Invoke([MarshalAs(UnmanagedType.IUnknown)] object installationJob, [MarshalAs(UnmanagedType.IUnknown)] object callbackArgs);
    }

    /// <summary>Objeto que Windows Update avisa al terminar un trabajo asíncrono (búsqueda, descarga o instalación).</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class WuCallbacks : IWuSearchCompletedCallback, IWuDownloadProgressChangedCallback, IWuDownloadCompletedCallback,
                                      IWuInstallationProgressChangedCallback, IWuInstallationCompletedCallback
    {
        internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);

        void IWuSearchCompletedCallback.Invoke(object job, object args) => Done.Set();
        void IWuDownloadCompletedCallback.Invoke(object job, object args) => Done.Set();
        void IWuInstallationCompletedCallback.Invoke(object job, object args) => Done.Set();
        void IWuDownloadProgressChangedCallback.Invoke(object job, object args) { }
        void IWuInstallationProgressChangedCallback.Invoke(object job, object args) { }
    }

    /// <summary>
    /// Trabajos asíncronos de Windows Update con tiempo límite. Las llamadas síncronas (Search/Download/Install) pueden
    /// quedarse colgadas para siempre si el servicio no responde; así Velox nunca se queda «ocupado» sin fin.
    /// </summary>
    internal static class WuaAsync
    {
        internal static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(45);
        internal static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(45);

        internal static dynamic Search(dynamic searcher, string criteria)
        {
            var cb = new WuCallbacks();
            dynamic job = searcher.BeginSearch(criteria, cb, "Velox");
            try
            {
                Wait(job, cb, SearchTimeout, CancellationToken.None, "search");
                return searcher.EndSearch(job);
            }
            finally { CleanUp(job); }
        }

        /// <summary>Descarga cancelable (al cancelar se pide a Windows Update que pare y se lanza OperationCanceledException).</summary>
        internal static dynamic Download(dynamic downloader, CancellationToken ct)
        {
            var cb = new WuCallbacks();
            dynamic job = downloader.BeginDownload(cb, cb, "Velox");
            try
            {
                Wait(job, cb, DownloadTimeout, ct, "download");
                return downloader.EndDownload(job);
            }
            finally { CleanUp(job); }
        }

        /// <summary>Instalación: no se cancela a mitad (un driver a medias es peor), solo se corta si Windows Update no responde.</summary>
        internal static dynamic Install(dynamic installer)
        {
            var cb = new WuCallbacks();
            dynamic job = installer.BeginInstall(cb, cb, "Velox");
            try
            {
                Wait(job, cb, InstallTimeout, CancellationToken.None, "install");
                return installer.EndInstall(job);
            }
            finally { CleanUp(job); }
        }

        static void Wait(dynamic job, WuCallbacks cb, TimeSpan timeout, CancellationToken ct, string what)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                if (cb.Done.Wait(500)) return;
                bool completed = false;
                try { completed = (bool)job.IsCompleted; } catch { }
                if (completed) return;

                bool cancel = ct.IsCancellationRequested;
                if (cancel || sw.Elapsed > timeout)
                {
                    Log.Warn("Windows Update: " + what + (cancel ? " cancelled by the user" : " timed out after " + (int)timeout.TotalMinutes + " min") + "; asking it to stop.");
                    try { job.RequestAbort(); } catch (Exception ex) { Log.Warn("Windows Update: RequestAbort: " + ex.Message); }
                    cb.Done.Wait(TimeSpan.FromSeconds(60)); // que se detenga de verdad antes de seguir
                    if (cancel) throw new OperationCanceledException(ct);
                    throw new TimeoutException(Localization.Loc.F("drv.reason.wuTimeout", (int)timeout.TotalMinutes));
                }
            }
        }

        static void CleanUp(dynamic job)
        {
            try { job.CleanUp(); } catch { }
        }
    }
}
