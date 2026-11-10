using System;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Win32;

namespace Velox.Core.Backup
{
    /// <summary>
    /// Valor original guardado antes de un cambio. Campos según Kind:
    ///   Reg        Path, Name, Existed, ValueKind, Data
    ///   RegKey     Path = clave, Existed = ya existía (si no, al deshacer se borra entera)
    ///   Svc        Name, IntA = Start (null si no había), Existed/ValueKind/Data = DelayedAutostart original
    ///   Task       Path = ruta completa de la tarea, Flag = estaba activada
    ///   PowerVal   Path = plan, Name = subgrupo, Extra = ajuste, IntA = AC, IntB = DC
    ///   PowerScheme Path = plan activo anterior, Extra = plan creado por Velox (o null)
    ///   Overlay    Data = GUID del modo de energía anterior
    ///   Refresh    Path = pantalla, IntA = Hz anteriores
    ///   ClientAnim Flag = animaciones activas antes
    ///   Hibernate  Flag = hibernación activa antes
    ///   Dns        Path = adaptador ({GUID}), Name = su nombre, Data = DNS IPv4 fijados antes (vacío = los del router)
    /// </summary>
    [DataContract(Name = "Record", Namespace = "")]
    public sealed class BackupRecord
    {
        [DataMember(Order = 1)] public string Kind { get; set; }
        [DataMember(Order = 2)] public string Key { get; set; }
        [DataMember(Order = 3, EmitDefaultValue = false)] public string Path { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public string Name { get; set; }
        [DataMember(Order = 5)] public bool Existed { get; set; }
        [DataMember(Order = 6, EmitDefaultValue = false)] public string ValueKind { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public string Data { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public string Extra { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public int? IntA { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public int? IntB { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public bool? Flag { get; set; }
        [DataMember(Order = 12)] public DateTime Created { get; set; } = DateTime.Now;

        public RegistryValueKind RegKind
        {
            get { return Enum.TryParse(ValueKind, out RegistryValueKind k) ? k : RegistryValueKind.Unknown; }
        }

        /// <summary>Guarda un estado de registro (valor codificado de forma segura para XML).</summary>
        public void SetRegState(RegState st)
        {
            Existed = st.Existed;
            ValueKind = st.Existed ? st.Kind.ToString() : null;
            Data = st.Existed ? RegValueCodec.Encode(st.Kind, st.Value) : null;
        }

        public object GetRegValue() => RegValueCodec.Decode(RegKind, Data);
    }

    /// <summary>Convierte valores de registro a texto apto para XML (las cadenas van en Base64).</summary>
    public static class RegValueCodec
    {
        public static string Encode(RegistryValueKind kind, object value)
        {
            if (value == null) return null;
            switch (kind)
            {
                case RegistryValueKind.DWord: return ((uint)(Convert.ToInt64(value) & 0xFFFFFFFFL)).ToString();
                case RegistryValueKind.QWord: return Convert.ToInt64(value).ToString();
                case RegistryValueKind.Binary: return Convert.ToBase64String((byte[])value);
                case RegistryValueKind.MultiString:
                    return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\0", (string[])value)));
                default:
                    return Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToString(value)));
            }
        }

        public static object Decode(RegistryValueKind kind, string data)
        {
            if (data == null) return null;
            switch (kind)
            {
                case RegistryValueKind.DWord: return unchecked((int)uint.Parse(data));
                case RegistryValueKind.QWord: return long.Parse(data);
                case RegistryValueKind.Binary: return Convert.FromBase64String(data);
                case RegistryValueKind.MultiString:
                    var s = Encoding.UTF8.GetString(Convert.FromBase64String(data));
                    return s.Length == 0 ? new string[0] : s.Split('\0');
                default:
                    return Encoding.UTF8.GetString(Convert.FromBase64String(data));
            }
        }
    }
}
