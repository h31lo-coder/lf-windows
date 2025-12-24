using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LfWindows.Services;

public class NaturalStringComparer : IComparer<string>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        return StrCmpLogicalW(x, y);
    }
}
