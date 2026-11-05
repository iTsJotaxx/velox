using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Velox.Core.Drivers
{
    /// <summary>Verificación de firma Authenticode (WinVerifyTrust) y del firmante de un archivo.</summary>
    internal static class Authenticode
    {
        const uint WTD_UI_NONE = 2;
        const uint WTD_REVOKE_WHOLECHAIN = 1;
        const uint WTD_CHOICE_FILE = 1;
        const uint WTD_STATEACTION_VERIFY = 1;
        const uint WTD_STATEACTION_CLOSE = 2;
        const uint WTD_REVOCATION_CHECK_CHAIN = 0x40;
        static readonly IntPtr InvalidHandle = new IntPtr(-1); // hwnd = INVALID_HANDLE_VALUE: sin interfaz

        // WINTRUST_ACTION_GENERIC_VERIFY_V2
        static Guid ActionGenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        /// <summary>0 si la firma es válida y la cadena (con revocación) es de confianza; si no, el código de error.</summary>
        internal static int VerifyTrust(string path)
        {
            IntPtr pPath = Marshal.StringToHGlobalUni(path);
            IntPtr pFile = IntPtr.Zero;
            try
            {
                var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = pPath };
                pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fi, pFile, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_REVOCATION_CHECK_CHAIN,
                };
                int rc = WinVerifyTrust(InvalidHandle, ref ActionGenericVerifyV2, ref data);
                // Libera el estado que reservó la verificación
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(InvalidHandle, ref ActionGenericVerifyV2, ref data);
                return rc;
            }
            finally
            {
                if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile);
                Marshal.FreeHGlobal(pPath);
            }
        }

        /// <summary>Subject del certificado firmante incrustado; null si no hay firma incrustada.</summary>
        internal static string SignerSubject(string path)
        {
            try
            {
                using (var cert = X509Certificate.CreateFromSignedFile(path))
                    return cert.Subject;
            }
            catch { return null; }
        }

        /// <summary>¿Algún componente O= del Subject es exactamente esa organización? (no vale una subcadena como «NVIDIA Corporation Fake»).</summary>
        internal static bool SubjectOrganizationIs(string subject, string organization)
        {
            if (string.IsNullOrEmpty(subject)) return false;
            foreach (var kv in ParseDistinguishedName(subject))
                if (kv.Key.Equals("O", StringComparison.OrdinalIgnoreCase) && kv.Value.Trim().Equals(organization, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // Divide "CN=a, O=\"b, c\", C=US" respetando las comillas
        static List<KeyValuePair<string, string>> ParseDistinguishedName(string dn)
        {
            var res = new List<KeyValuePair<string, string>>();
            var cur = new StringBuilder();
            bool quoted = false;
            var parts = new List<string>();
            foreach (char c in dn)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if ((c == ',' || c == ';') && !quoted) { parts.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(c);
            }
            parts.Add(cur.ToString());
            foreach (var p in parts)
            {
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                res.Add(new KeyValuePair<string, string>(p.Substring(0, eq).Trim(), p.Substring(eq + 1).Trim()));
            }
            return res;
        }
    }
}
