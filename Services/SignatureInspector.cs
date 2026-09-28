using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace DGKNInspector.Services;

/// <summary>
/// Prüft die Authenticode-Signatur einer EXE/DLL: Ist sie gültig signiert?
/// Von welcher Firma (Zertifikat-Feld O=) und aus welchem Land (C=)?
/// Das ist die verlässlichste Quelle für "woher stammt dieses Programm".
/// </summary>
public static class SignatureInspector
{
    public enum SignState { Unknown, Valid, Invalid, Unsigned }

    public sealed record SigInfo(SignState State, string Company, string CountryCode);

    public static readonly SigInfo None = new(SignState.Unknown, "", "");

    // ---- WinVerifyTrust ----
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
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
        [MarshalAs(UnmanagedType.LPWStr)] public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_SAFER_FLAG = 0x100;

    public static SigInfo Inspect(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return None;

        // 1) Vertrauensprüfung (erfasst eingebettete UND Katalog-Signaturen,
        //    z. B. bei Windows-Komponenten).
        bool trusted = VerifyTrust(exePath);

        // 2) Firma/Land nur aus dem eingebetteten Zertifikat auslesbar.
        string company = "", country = "";
        bool hasCert = false;
        try
        {
#pragma warning disable SYSLIB0057 // kein schlanker Ersatz zum Auslesen des PE-Signierers
            var cert = X509Certificate.CreateFromSignedFile(exePath);
#pragma warning restore SYSLIB0057
            hasCert = true;
            ParseSubject(cert.Subject, out company, out country);
        }
        catch
        {
            hasCert = false;
        }

        if (hasCert)
            return new SigInfo(trusted ? SignState.Valid : SignState.Invalid, company, country);

        // Kein eingebettetes Zertifikat: über Katalog signiert (gültig) oder gar nicht.
        return new SigInfo(trusted ? SignState.Valid : SignState.Unsigned, "", "");
    }

    private static bool VerifyTrust(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path
        };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        Marshal.StructureToPtr(fileInfo, pFile, false);

        var data = new WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            dwUIChoice = WTD_UI_NONE,
            fdwRevocationChecks = WTD_REVOKE_NONE,
            dwUnionChoice = WTD_CHOICE_FILE,
            pFile = pFile,
            dwStateAction = WTD_STATEACTION_VERIFY,
            dwProvFlags = WTD_SAFER_FLAG
        };

        var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
        try
        {
            int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            // 0 = vertrauenswürdig/gültig
            return result == 0;
        }
        finally
        {
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            Marshal.FreeHGlobal(pFile);
        }
    }

    /// <summary>Zerlegt "CN=..., O=Firma, L=..., C=US" in Firma (O oder CN) und Land (C).</summary>
    private static void ParseSubject(string subject, out string company, out string country)
    {
        company = ""; country = "";
        string cn = "";
        foreach (var part in SplitDn(subject))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = part[..eq].Trim().ToUpperInvariant();
            string val = part[(eq + 1)..].Trim().Trim('"');
            switch (key)
            {
                case "O":  if (company == "") company = val; break;
                case "CN": cn = val; break;
                case "C":  country = val; break;
            }
        }
        if (string.IsNullOrEmpty(company)) company = cn;
    }

    // DN-Teile trennen (Kommas in Anführungszeichen respektieren).
    private static IEnumerable<string> SplitDn(string dn)
    {
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char c in dn)
        {
            if (c == '"') inQuotes = !inQuotes;
            if (c == ',' && !inQuotes) { yield return cur.ToString(); cur.Clear(); }
            else cur.Append(c);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }
}
