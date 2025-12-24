using System;
using System.IO;
using System.Text;
using LfWindows.Interop;

namespace LfWindows.Services;

public static class ShortcutResolver
{
    public static string? Resolve(string linkPath)
    {
        try
        {
            if (!File.Exists(linkPath)) return null;

            IShellLink link = (IShellLink)new ShellLink();
            IPersistFile file = (IPersistFile)link;
            
            // STGM_READ = 0
            file.Load(linkPath, 0);

            var sb = new StringBuilder(260);
            // SLGP_UNCPRIORITY = 0x2
            // SLGP_SHORTPATH = 0x1
            // SLGP_RAWPATH = 0x4
            // We use 0 for standard path
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            
            var target = sb.ToString();
            if (string.IsNullOrWhiteSpace(target)) return null;
            
            return target;
        }
        catch
        {
            // If resolution fails (e.g. COM error), return null to fallback to default behavior
            return null;
        }
    }
}
