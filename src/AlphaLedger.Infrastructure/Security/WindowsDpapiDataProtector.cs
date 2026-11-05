using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AlphaLedger.Core.Abstractions;

namespace AlphaLedger.Infrastructure.Security;

public sealed class WindowsDpapiDataProtector : IWindowsDataProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        EnsureWindows();

        var input = CreateBlob(plaintext);
        try
        {
            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return CopyAndFree(ref output);
        }
        finally
        {
            FreeInput(ref input);
        }
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        EnsureWindows();

        var input = CreateBlob(protectedData);
        IntPtr description = IntPtr.Zero;
        try
        {
            if (!CryptUnprotectData(ref input, out description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return CopyAndFree(ref output);
        }
        finally
        {
            FreeInput(ref input);
            if (description != IntPtr.Zero)
                _ = LocalFree(description);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is available only on Windows.");
    }

    private static DataBlob CreateBlob(byte[] data)
    {
        var pointer = Marshal.AllocHGlobal(Math.Max(data.Length, 1));
        if (data.Length > 0)
            Marshal.Copy(data, 0, pointer, data.Length);
        return new DataBlob { Length = data.Length, Data = pointer };
    }

    private static byte[] CopyAndFree(ref DataBlob blob)
    {
        try
        {
            var result = new byte[blob.Length];
            if (blob.Length > 0)
                Marshal.Copy(blob.Data, result, 0, blob.Length);
            return result;
        }
        finally
        {
            if (blob.Data != IntPtr.Zero)
                _ = LocalFree(blob.Data);
            blob = default;
        }
    }

    private static void FreeInput(ref DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            for (var index = 0; index < blob.Length; index++)
                Marshal.WriteByte(blob.Data, index, 0);
            Marshal.FreeHGlobal(blob.Data);
            blob = default;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        out IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}