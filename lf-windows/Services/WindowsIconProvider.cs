using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;

namespace LfWindows.Services;

[SupportedOSPlatform("windows")]
public class WindowsIconProvider : IIconProvider
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    public static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImportAttribute()]
    [GuidAttribute("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IImageList
    {
        [PreserveSig]
        int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig]
        int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig]
        int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig]
        int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig]
        int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig]
        int Draw(IntPtr pimldp);
        [PreserveSig]
        int Remove(int i);
        [PreserveSig]
        int GetIcon(int i, int flags, out IntPtr picon);
        [PreserveSig]
        int GetImageInfo(int i, out IntPtr pImageInfo);
        [PreserveSig]
        int Copy(int iDst, object punkSrc, int iSrc, int uFlags);
        [PreserveSig]
        int Merge(int i1, object punk2, int i2, int dx, int dy, ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int Write(object pstm);
        [PreserveSig]
        int GetDragImage(out IntPtr ppt, out IntPtr pptHotspot, ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int GetItemFlags(int i, out int dwFlags);
        [PreserveSig]
        int GetOverlayImage(int iOverlay, out int piIndex);
    }

    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_SMALLICON = 0x000000001;
    public const uint SHGFI_SYSICONINDEX = 0x000004000;

    public const int SHIL_LARGE = 0x0;
    public const int SHIL_SMALL = 0x1;
    public const int SHIL_EXTRALARGE = 0x2;
    public const int SHIL_SYSSMALL = 0x3;
    public const int SHIL_JUMBO = 0x4;

    public const int ILD_TRANSPARENT = 0x00000001;

    public async Task<Bitmap?> GetFileIconAsync(string filePath, IconSize size)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Always use SHGetImageList for better quality (32-bit alpha)
                int imageListType = SHIL_SMALL;
                switch (size)
                {
                    case IconSize.Small: imageListType = SHIL_SMALL; break;
                    case IconSize.Large: imageListType = SHIL_LARGE; break;
                    case IconSize.ExtraLarge: imageListType = SHIL_EXTRALARGE; break;
                    case IconSize.Jumbo: imageListType = SHIL_JUMBO; break;
                }

                var shfiIndex = new SHFILEINFO();
                uint indexFlags = SHGFI_SYSICONINDEX;
                
                // Get the index of the icon in the system image list
                uint attributes = 0;
                
                // Check if file exists. If not (e.g. virtual path in archive), use attributes to get icon based on extension
                if (!File.Exists(filePath) && !Directory.Exists(filePath))
                {
                    indexFlags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = 0x80; // FILE_ATTRIBUTE_NORMAL
                    
                    // Simple heuristic: if no extension, assume directory
                    if (string.IsNullOrEmpty(Path.GetExtension(filePath)))
                    {
                        attributes = 0x10; // FILE_ATTRIBUTE_DIRECTORY
                    }
                }

                SHGetFileInfo(filePath, attributes, ref shfiIndex, (uint)Marshal.SizeOf(shfiIndex), indexFlags);
                
                var iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                IImageList? iml = null;
                int hres = SHGetImageList(imageListType, ref iidImageList, out iml);

                if (hres == 0 && iml != null)
                {
                    IntPtr hIcon = IntPtr.Zero;
                    hres = iml.GetIcon(shfiIndex.iIcon, ILD_TRANSPARENT, out hIcon);
                    
                    if (hres == 0 && hIcon != IntPtr.Zero)
                    {
                        using (var icon = System.Drawing.Icon.FromHandle(hIcon))
                        {
                            var bitmap = IconToBitmap(icon);
                            DestroyIcon(hIcon);
                            return bitmap;
                        }
                    }
                }

                // Fallback to SHGetFileInfo if SHGetImageList fails
                var shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON;
                if (size == IconSize.Small) flags |= SHGFI_SMALLICON;
                else flags |= SHGFI_LARGEICON; // Others map to Large for fallback

                SHGetFileInfo(filePath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                if (shfi.hIcon != IntPtr.Zero)
                {
                    using (var icon = System.Drawing.Icon.FromHandle(shfi.hIcon))
                    {
                        var bmp = IconToBitmap(icon);
                        DestroyIcon(shfi.hIcon);
                        return bmp;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        });
    }

    private Bitmap? IconToBitmap(System.Drawing.Icon icon)
    {
        try 
        {
            using var bmp = icon.ToBitmap();
            using var stream = new MemoryStream();
            bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Seek(0, SeekOrigin.Begin);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
