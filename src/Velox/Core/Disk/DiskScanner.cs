using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Velox.Native;

namespace Velox.Core.Disk
{
    /// <summary>Tipo de archivo para los colores del explorador de disco. Folder solo se usa para carpetas.</summary>
    public enum FileKind { Video, Image, Audio, Archive, Program, Document, Code, System, DiskImage, Other, Folder }

    /// <summary>Carpeta o archivo del árbol analizado. Los tamaños de las carpetas incluyen todo su contenido.</summary>
    public sealed class DiskNode
    {
        public const int KindCount = 10; // tipos de archivo (sin Folder)

        public string Name { get; internal set; }
        public DiskNode Parent { get; internal set; }
        public bool IsDir { get; internal set; }
        /// <summary>Resto de archivos pequeños de una carpeta agrupados en un solo elemento.</summary>
        public bool IsAggregate { get; internal set; }
        /// <summary>Enlace o unión: no se entra (su contenido está en otro sitio).</summary>
        public bool IsLink { get; internal set; }
        /// <summary>No se pudo leer (sin permiso).</summary>
        public bool Denied { get; internal set; }
        public long Size { get; internal set; }
        /// <summary>Archivos que contiene (en un elemento agrupado: cuántos archivos agrupa).</summary>
        public long FileCount { get; internal set; }
        public long DirCount { get; internal set; }
        public FileKind Kind { get; internal set; }
        public DateTime Modified { get; internal set; }
        public List<DiskNode> Children { get; internal set; }
        /// <summary>Bytes por tipo de archivo (solo carpetas), índice = (int)FileKind.</summary>
        public long[] KindSizes { get; internal set; }

        public bool IsRoot => Parent == null;
        public bool CanEnter => IsDir && !IsLink && Children != null && Children.Count > 0;

        public string FullPath
        {
            get
            {
                if (Parent == null) return Name;
                if (IsAggregate) return Parent.FullPath;
                string p = Parent.FullPath;
                return p.EndsWith("\\", StringComparison.Ordinal) ? p + Name : p + "\\" + Name;
            }
        }

        /// <summary>Tipo que más ocupa (carpetas) o el tipo del archivo.</summary>
        public FileKind Dominant
        {
            get
            {
                if (!IsDir) return Kind;
                if (KindSizes == null) return FileKind.Folder;
                int best = -1; long max = 0;
                for (int i = 0; i < KindSizes.Length; i++) if (KindSizes[i] > max) { max = KindSizes[i]; best = i; }
                return best < 0 ? FileKind.Folder : (FileKind)best;
            }
        }

        /// <summary>Profundidad desde la raíz analizada (0 = raíz).</summary>
        public int Depth { get { int d = 0; for (var p = Parent; p != null; p = p.Parent) d++; return d; } }
    }

    /// <summary>Contadores en vivo del análisis (la interfaz los lee con un temporizador).</summary>
    public sealed class DiskScanCounters
    {
        public long Files;
        public long Dirs;
        public long Bytes;
        public long Denied;
    }

    public sealed class DriveEntry
    {
        public string Root { get; set; }
        public string Label { get; set; }
        public string Format { get; set; }
        public long Total { get; set; }
        public long Free { get; set; }
        public long Used => Total - Free;
        public double UsedPct => Total > 0 ? Used * 100.0 / Total : 0;
    }

    public static class DiskScanner
    {
        /// <summary>Archivos que se guardan por carpeta (los más grandes); el resto se agrupa en un solo elemento.</summary>
        const int MaxFilesPerDir = 200;
        /// <summary>Hasta qué profundidad se reparten las subcarpetas entre varios hilos.</summary>
        const int ParallelDepth = 3;
        const int MaxDepth = 200;

        public static List<DriveEntry> GetDrives()
        {
            var list = new List<DriveEntry>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable && d.DriveType != DriveType.Network) continue;
                    list.Add(new DriveEntry
                    {
                        Root = d.RootDirectory.FullName,
                        Label = d.VolumeLabel,
                        Format = d.DriveFormat,
                        Total = d.TotalSize,
                        Free = d.TotalFreeSpace,
                    });
                }
                catch { }
            }
            return list;
        }

        /// <summary>Analiza una carpeta completa. Lanza OperationCanceledException si se cancela.</summary>
        public static DiskNode Scan(string path, DiskScanCounters counters, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            path = Path.GetFullPath(path);
            if (path.Length > 3) path = path.TrimEnd('\\');
            var root = new DiskNode { Name = path, IsDir = true };
            try { root.Modified = Directory.GetLastWriteTime(path); } catch { }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { ScanDir(root, path, 0, counters ?? new DiskScanCounters(), ct); }
            catch (AggregateException ae)
            {
                var flat = ae.Flatten();
                if (flat.InnerExceptions.Any(e => e is OperationCanceledException)) throw new OperationCanceledException(ct);
                throw flat.InnerExceptions[0];
            }
            Log.Info("Disk scan " + path + ": " + root.FileCount.ToString("N0") + " files, " + root.DirCount.ToString("N0") + " folders, "
                     + Format.Bytes(root.Size) + " in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s.");
            return root;
        }

        static void ScanDir(DiskNode node, string path, int depth, DiskScanCounters c, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var dirs = new List<DiskNode>();
            var files = new List<DiskNode>();
            var kinds = new long[DiskNode.KindCount];
            long fileBytes = 0;

            bool ok = FileFind.Enumerate(path, (ref FileFind.WIN32_FIND_DATA d) =>
            {
                bool isDir = (d.dwFileAttributes & FileFind.AttrDirectory) != 0;
                bool link = (d.dwFileAttributes & FileFind.AttrReparsePoint) != 0;
                DateTime mod = DateTime.MinValue;
                long ft = FileFind.LastWrite(ref d);
                try { if (ft > 0) mod = DateTime.FromFileTime(ft); } catch { }
                if (isDir)
                {
                    dirs.Add(new DiskNode { Name = d.cFileName, Parent = node, IsDir = true, IsLink = link, Modified = mod, KindSizes = new long[DiskNode.KindCount] });
                }
                else
                {
                    long size = ((long)d.nFileSizeHigh << 32) | d.nFileSizeLow;
                    var kind = FileKinds.Classify(d.cFileName);
                    kinds[(int)kind] += size;
                    fileBytes += size;
                    files.Add(new DiskNode { Name = d.cFileName, Parent = node, Size = size, Kind = kind, Modified = mod });
                }
            });

            if (!ok) { node.Denied = true; Interlocked.Increment(ref c.Denied); }
            Interlocked.Add(ref c.Files, files.Count);
            Interlocked.Add(ref c.Dirs, dirs.Count);
            Interlocked.Add(ref c.Bytes, fileBytes);

            // Límite de seguridad: ninguna carpeta real anida tanto (evita desbordar la pila con estructuras anómalas)
            var toScan = depth < MaxDepth ? dirs.Where(x => !x.IsLink).ToList() : new List<DiskNode>();
            if (depth < ParallelDepth && toScan.Count > 1)
            {
                Parallel.ForEach(toScan, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount), CancellationToken = ct },
                    child => ScanDir(child, Join(path, child.Name), depth + 1, c, ct));
            }
            else
            {
                foreach (var child in toScan) ScanDir(child, Join(path, child.Name), depth + 1, c, ct);
            }

            long size2 = fileBytes, fileCount = files.Count, dirCount = dirs.Count;
            foreach (var dch in dirs)
            {
                size2 += dch.Size;
                fileCount += dch.FileCount;
                dirCount += dch.DirCount;
                if (dch.KindSizes != null) for (int i = 0; i < kinds.Length; i++) kinds[i] += dch.KindSizes[i];
            }

            // Solo los archivos más grandes de cada carpeta; el resto, agrupado (ahorra mucha memoria en discos enteros)
            if (files.Count > MaxFilesPerDir)
            {
                files.Sort((a, b) => b.Size.CompareTo(a.Size));
                long rest = 0; int restCount = files.Count - MaxFilesPerDir;
                for (int i = MaxFilesPerDir; i < files.Count; i++) rest += files[i].Size;
                files.RemoveRange(MaxFilesPerDir, restCount);
                files.Add(new DiskNode { Name = "", Parent = node, IsAggregate = true, Size = rest, FileCount = restCount, Kind = FileKind.Other });
            }

            var children = new List<DiskNode>(dirs.Count + files.Count);
            children.AddRange(dirs);
            children.AddRange(files);
            children.Sort((a, b) => b.Size.CompareTo(a.Size));

            node.Children = children;
            node.Size = size2;
            node.FileCount = fileCount;
            node.DirCount = dirCount;
            node.KindSizes = kinds;
        }

        static string Join(string dir, string name) => dir.EndsWith("\\", StringComparison.Ordinal) ? dir + name : dir + "\\" + name;

        /// <summary>Quita un elemento del árbol (tras borrarlo) y descuenta su tamaño de todas las carpetas superiores.</summary>
        public static void Remove(DiskNode node)
        {
            var parent = node.Parent;
            if (parent == null) return;
            parent.Children?.Remove(node);
            long files = node.IsDir ? node.FileCount : (node.IsAggregate ? node.FileCount : 1);
            long dirs = node.IsDir ? node.DirCount + 1 : 0;
            for (var p = parent; p != null; p = p.Parent)
            {
                p.Size -= node.Size;
                p.FileCount -= files;
                p.DirCount -= dirs;
                if (p.KindSizes == null) continue;
                if (node.IsDir && node.KindSizes != null)
                    for (int i = 0; i < p.KindSizes.Length; i++) p.KindSizes[i] -= node.KindSizes[i];
                else if (!node.IsDir) p.KindSizes[(int)node.Kind] -= node.Size;
            }
        }
    }

    public static class FileKinds
    {
        static readonly Dictionary<string, FileKind> Map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);

        static FileKinds()
        {
            Add(FileKind.Video, "mp4 mkv avi mov wmv flv webm m4v mpg mpeg ts m2ts mts 3gp vob ogv");
            Add(FileKind.Image, "jpg jpeg png gif bmp tif tiff webp heic heif raw cr2 cr3 nef arw dng psd svg ico dds tga exr avif jxl");
            Add(FileKind.Audio, "mp3 wav flac aac ogg m4a wma opus aiff aif mid midi ape");
            Add(FileKind.Archive, "zip rar 7z tar gz tgz bz2 xz zst lz4 cab lzma");
            Add(FileKind.Program, "exe dll sys msi msp msix msixbundle appx appxbundle ocx com bat cmd ps1 jar drv efi mui cpl scr");
            Add(FileKind.Document, "pdf doc docx xls xlsx ppt pptx odt ods odp txt rtf md csv epub mobi xps one pst ost");
            Add(FileKind.Code, "cs cpp cc c h hpp js mjs ts tsx jsx py java go rs rb php css scss less vue sln csproj vcxproj kt swift lua sh ipynb json xml yml yaml html htm sql");
            Add(FileKind.System, "etl evtx dmp mdmp tmp temp cache db sqlite sqlite3 ldb log pdb ttf otf fon nls cat mum manifest dat bin blf regtrans-ms edb jrs chk pf");
            Add(FileKind.DiskImage, "iso img vhd vhdx vmdk vdi qcow2 wim esd swm avhdx vmsn vmem");
        }

        static void Add(FileKind kind, string list)
        {
            foreach (var e in list.Split(' ')) Map[e] = kind;
        }

        public static FileKind Classify(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return FileKind.Other;
            if (fileName.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase)) return FileKind.System;
            int dot = fileName.LastIndexOf('.');
            if (dot < 0 || dot == fileName.Length - 1) return FileKind.Other;
            return Map.TryGetValue(fileName.Substring(dot + 1), out var k) ? k : FileKind.Other;
        }
    }

    public static class Format
    {
        static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

        /// <summary>Tamaño legible con la cultura actual (1,5 GB / 1.5 GB).</summary>
        public static string Bytes(long bytes)
        {
            double v = Math.Max(0, bytes);
            int u = 0;
            while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
            string num = u == 0 ? v.ToString("N0") : v < 10 ? v.ToString("0.0#") : v < 100 ? v.ToString("0.0") : v.ToString("N0");
            return num + " " + Units[u];
        }
    }
}
