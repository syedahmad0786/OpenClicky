using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OpenClicky.Core;

namespace OpenClicky.App;

/// <summary>
/// Secret store: Windows Credential Manager (GENERIC) when the blob fits,
/// otherwise DPAPI-protected files under %AppData%\OpenClicky\secrets\.
/// ChatGPT OAuth JSON (access + refresh + id tokens) exceeds CredWrite's
/// 2560-byte blob limit and previously failed with win32=1783.
/// </summary>
internal sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const string TargetPrefix = "OpenClicky/";

    /// <summary>CRED_MAX_CREDENTIAL_BLOB_SIZE — CredWrite returns RPC_X_BAD_STUB_DATA (1783) above this.</summary>
    private const int CredMaxCredentialBlobSize = 2560;

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("OpenClicky.SecretStore.v1");

    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var fromCred = TryReadCredential(name);
        if (fromCred is not null)
        {
            return fromCred;
        }

        return TryReadDpapiFile(name);
    }

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        var unicodeBytes = Encoding.Unicode.GetBytes(value);
        if (unicodeBytes.Length <= CredMaxCredentialBlobSize
            && TryWriteCredential(name, unicodeBytes, out _))
        {
            // Prefer Credential Manager for small secrets; drop any stale DPAPI copy.
            DeleteDpapiFile(name);
            return;
        }

        // OAuth token JSON and other oversized blobs cannot use CredWrite.
        WriteDpapiFile(name, value);
        // Avoid leaving a stale smaller CredMan entry for the same name.
        TryDeleteCredential(name);
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TryDeleteCredential(name);
        DeleteDpapiFile(name);
    }

    private static string? TryReadCredential(string name)
    {
        var target = TargetPrefix + name;
        if (!CredRead(target, CredTypeGeneric, 0, out var ptr))
        {
            return null;
        }

        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            // Blobs are written as UTF-16LE (Windows password convention).
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            CredFree(ptr);
        }
    }

    private static bool TryWriteCredential(string name, byte[] unicodeBytes, out int win32Error)
    {
        var target = TargetPrefix + name;
        var blob = Marshal.AllocHGlobal(unicodeBytes.Length);
        var targetPtr = Marshal.StringToCoTaskMemUni(target);
        // Stable username label; empty/null UserName can confuse some CredMan backends.
        var userPtr = Marshal.StringToCoTaskMemUni("OpenClicky");
        try
        {
            Marshal.Copy(unicodeBytes, 0, blob, unicodeBytes.Length);
            var cred = new NativeCredentialPtr
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = targetPtr,
                Comment = IntPtr.Zero,
                CredentialBlobSize = (uint)unicodeBytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = IntPtr.Zero,
                UserName = userPtr,
            };

            if (CredWrite(ref cred, 0))
            {
                win32Error = 0;
                return true;
            }

            win32Error = Marshal.GetLastWin32Error();
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
            Marshal.FreeCoTaskMem(targetPtr);
            Marshal.FreeCoTaskMem(userPtr);
        }
    }

    private static void TryDeleteCredential(string name)
    {
        var target = TargetPrefix + name;
        if (CredDelete(target, CredTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error is not ErrorNotFound and not 0)
        {
            // Best-effort cleanup when migrating to DPAPI; do not fail Delete/Set.
            DebugLog.Append($"CredDelete skipped for {target} (win32={error}).");
        }
    }

    private static string SecretsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenClicky",
            "secrets");

    private static string DpapiPath(string name)
    {
        // SecretNames are kebab-case; keep the file name predictable and path-safe.
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                throw new ArgumentException($"Secret name contains unsupported character '{c}'.", nameof(name));
            }
        }

        return Path.Combine(SecretsDirectory, name + ".dpapi");
    }

    private static void WriteDpapiFile(string name, string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plain, DpapiEntropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(SecretsDirectory);
        var path = DpapiPath(name);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, protectedBytes);
        File.Move(tmp, path, overwrite: true);
        DebugLog.Append(
            $"secret_store_dpapi name={name} bytes={plain.Length} (CredWrite limit={CredMaxCredentialBlobSize} unicode bytes).");
    }

    private static string? TryReadDpapiFile(string name)
    {
        var path = DpapiPath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(protectedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException ex)
        {
            DebugLog.Append($"secret_store_dpapi_read_failed name={name} err={ex.Message}");
            return null;
        }
    }

    private static void DeleteDpapiFile(string name)
    {
        var path = DpapiPath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var tmp = path + ".tmp";
        if (File.Exists(tmp))
        {
            File.Delete(tmp);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredentialPtr userCredential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    /// <summary>CREDENTIAL layout for CredRead (managed strings).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    /// <summary>CREDENTIAL layout for CredWrite with explicit LPWSTR pointers (avoids stub marshaling issues).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredentialPtr
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
