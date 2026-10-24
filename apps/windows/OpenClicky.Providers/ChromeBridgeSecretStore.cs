using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenClicky.Providers;

public static class ChromeBridgeSecretStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "chrome-bridge.key");

    public static byte[] LoadOrCreate(string? path = null)
    {
        path ??= DefaultPath;
        if (File.Exists(path)) return Unprotect(File.ReadAllBytes(path));
        var secret = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Protect(secret));
        return secret;
    }

    private static byte[] Protect(byte[] value) => Transform(value, protect: true);
    private static byte[] Unprotect(byte[] value) => Transform(value, protect: false);

    private static byte[] Transform(byte[] value, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Chrome bridge secret uses Windows DPAPI.");
        var input = Blob.From(value);
        try
        {
            var ok = protect
                ? CryptProtectData(ref input, null, nint.Zero, nint.Zero, nint.Zero, 1, out var output)
                : CryptUnprotectData(ref input, nint.Zero, nint.Zero, nint.Zero, nint.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { if (output.Data != nint.Zero) LocalFree(output.Data); }
        }
        finally { input.Free(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Blob
    {
        public int Size; public nint Data;
        public static Blob From(byte[] bytes) { var blob = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Marshal.Copy(bytes, 0, blob.Data, bytes.Length); return blob; }
        public void Free() { if (Data != nint.Zero) Marshal.FreeHGlobal(Data); }
    }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
