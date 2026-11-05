using System;
using Microsoft.Win32;

namespace Velox.Core
{
    public sealed class RegState
    {
        public bool Existed { get; set; }
        public RegistryValueKind Kind { get; set; }
        public object Value { get; set; }
    }

    /// <summary>
    /// Valor de registro declarativo de una optimización.
    /// Default = valor de Windows al desactivarla sin copia (null = borrar el valor).
    /// Effective = valor que Windows usa si el valor no existe (para detectar bien el estado).
    /// </summary>
    public sealed class RegItem
    {
        public string Path { get; }
        public string Name { get; }
        public object Value { get; }
        public RegistryValueKind Kind { get; }
        public object Default { get; }
        public bool HasEffective { get; private set; }
        public object Effective { get; private set; }

        public RegItem(string path, string name, object value, RegistryValueKind kind, object @default)
        {
            Path = path; Name = name; Value = value; Kind = kind; Default = @default;
        }

        public RegItem WhenMissing(object effective)
        {
            HasEffective = true;
            Effective = effective;
            return this;
        }

        public bool IsSet()
        {
            var st = Reg.Get(Path, Name);
            if (st.Existed) return Reg.ValuesEqual(st.Value, Value, Kind);
            return HasEffective && Reg.ValuesEqual(Effective, Value, Kind);
        }
    }

    /// <summary>Acceso al registro con rutas "HKCU\..." / "HKLM\..." y vista de 64 bits siempre.</summary>
    public static class Reg
    {
        static RegistryKey OpenBase(string path, out string sub)
        {
            int i = path.IndexOf('\\');
            if (i < 0) throw new ArgumentException("Invalid registry path: " + path);
            string hive = path.Substring(0, i).ToUpperInvariant().TrimEnd(':');
            sub = path.Substring(i + 1);
            RegistryHive h;
            switch (hive)
            {
                case "HKCU": case "HKEY_CURRENT_USER": h = RegistryHive.CurrentUser; break;
                case "HKLM": case "HKEY_LOCAL_MACHINE": h = RegistryHive.LocalMachine; break;
                default: throw new ArgumentException("Unsupported hive: " + hive);
            }
            return RegistryKey.OpenBaseKey(h, RegistryView.Registry64);
        }

        public static RegistryKey OpenKey(string path, bool writable)
        {
            using (var b = OpenBase(path, out string sub)) return b.OpenSubKey(sub, writable);
        }

        public static bool KeyExists(string path)
        {
            try { using (var k = OpenKey(path, false)) return k != null; }
            catch { return false; }
        }

        public static RegState Get(string path, string name)
        {
            var st = new RegState();
            try
            {
                using (var k = OpenKey(path, false))
                {
                    if (k == null) return st;
                    foreach (var n in k.GetValueNames())
                    {
                        if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) continue;
                        st.Existed = true;
                        st.Kind = k.GetValueKind(name);
                        st.Value = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        break;
                    }
                }
            }
            catch { }
            return st;
        }

        public static object Normalize(object value, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    long l = Convert.ToInt64(value);
                    return unchecked((int)(uint)(l & 0xFFFFFFFFL));
                case RegistryValueKind.QWord: return Convert.ToInt64(value);
                case RegistryValueKind.Binary: return (byte[])value;
                case RegistryValueKind.MultiString: return value as string[] ?? new[] { Convert.ToString(value) };
                default: return Convert.ToString(value);
            }
        }

        public static void Set(string path, string name, object value, RegistryValueKind kind)
        {
            using (var b = OpenBase(path, out string sub))
            using (var k = b.CreateSubKey(sub, true))
            {
                if (k == null) throw new InvalidOperationException("Cannot open " + path);
                k.SetValue(name, Normalize(value, kind), kind);
            }
        }

        public static void Remove(string path, string name)
        {
            using (var k = OpenKey(path, true))
                k?.DeleteValue(name, false);
        }

        public static bool ValuesEqual(object current, object expected, RegistryValueKind kind)
        {
            if (current == null || expected == null) return current == null && expected == null;
            try
            {
                switch (kind)
                {
                    case RegistryValueKind.DWord:
                        return (Convert.ToInt64(current) & 0xFFFFFFFFL) == (Convert.ToInt64(expected) & 0xFFFFFFFFL);
                    case RegistryValueKind.QWord:
                        return Convert.ToInt64(current) == Convert.ToInt64(expected);
                    case RegistryValueKind.MultiString:
                        return string.Join("\n", current as string[] ?? new string[0]) == string.Join("\n", expected as string[] ?? new string[0]);
                    case RegistryValueKind.Binary:
                        var a = current as byte[]; var b = expected as byte[];
                        if (a == null || b == null || a.Length != b.Length) return false;
                        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                        return true;
                    default:
                        return string.Equals(Convert.ToString(current), Convert.ToString(expected), StringComparison.Ordinal);
                }
            }
            catch { return false; }
        }

        /// <summary>Lee un DWORD (o una cadena numérica). Devuelve null si no existe o no es un número.</summary>
        public static int? GetInt(string path, string name)
        {
            var st = Get(path, name);
            if (!st.Existed || st.Value == null) return null;
            try { return Convert.ToInt32(st.Value); } catch { return null; }
        }
    }
}
