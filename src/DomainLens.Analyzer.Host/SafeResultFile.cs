using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DomainLens.Analyzer.Host;

/// <summary>
/// Opens a worker result without following a Windows reparse point. Linux workers are outside the
/// Milestone 0 scope; the portable fallback rejects an observed reparse point but does not claim a
/// race-free no-follow open.
/// </summary>
internal static class SafeResultFile
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagSequentialScan = 0x08000000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;

    public static FileStream OpenRead(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new UnsafeResultFileException(
                    "The analyzer result must be a regular, non-reparse file.");
            }

            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        var handle = CreateFile(
            path,
            GenericRead,
            FileShareRead,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics | FileFlagSequentialScan,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new UnsafeResultFileException(
                $"The analyzer result could not be opened safely ({new Win32Exception(error).Message}).");
        }

        try
        {
            if (!GetFileInformationByHandle(handle, out var information))
            {
                throw new UnsafeResultFileException(
                    $"The analyzer result handle could not be inspected ({new Win32Exception(Marshal.GetLastPInvokeError()).Message}).");
            }

            if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
            {
                throw new UnsafeResultFileException(
                    "The analyzer result is a reparse point and was not followed.");
            }

            if ((information.FileAttributes & FileAttributeDirectory) != 0)
            {
                throw new UnsafeResultFileException(
                    "The analyzer result is a directory rather than a regular file.");
            }

            if (information.NumberOfLinks != 1)
            {
                throw new UnsafeResultFileException(
                    "The analyzer result has more than one hard link.");
            }

            return new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}

internal sealed class UnsafeResultFileException : IOException
{
    public UnsafeResultFileException(string message)
        : base(message)
    {
    }
}
