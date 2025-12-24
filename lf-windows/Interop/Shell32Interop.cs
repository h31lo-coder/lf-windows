using System;
using System.Runtime.InteropServices;

namespace LfWindows.Interop;

public static class Shell32Interop
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted; // Changed from bool to int (BOOL is 4 bytes)
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    public const uint FO_MOVE = 0x0001;
    public const uint FO_COPY = 0x0002;
    public const uint FO_DELETE = 0x0003;
    public const uint FO_RENAME = 0x0004;

    public const ushort FOF_MULTIDESTFILES = 0x0001;
    public const ushort FOF_CONFIRMMOUSE = 0x0002;
    public const ushort FOF_SILENT = 0x0004;
    public const ushort FOF_RENAMEONCOLLISION = 0x0008;
    public const ushort FOF_NOCONFIRMATION = 0x0010;
    public const ushort FOF_WANTMAPPINGHANDLE = 0x0020;
    public const ushort FOF_ALLOWUNDO = 0x0040;
    public const ushort FOF_FILESONLY = 0x0080;
    public const ushort FOF_SIMPLEPROGRESS = 0x0100;
    public const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    public const ushort FOF_NOERRORUI = 0x0400;
    public const ushort FOF_NOCOPYSECURITYATTRIBS = 0x0800;
    public const ushort FOF_NORECURSION = 0x1000;
    public const ushort FOF_NO_CONNECTED_ELEMENTS = 0x2000;
    public const ushort FOF_WANTNUKEWARNING = 0x4000;
    public const ushort FOF_NORECURSEREPARSE = 0x8000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);
}
