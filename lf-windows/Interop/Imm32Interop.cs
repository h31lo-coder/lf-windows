using System;
using System.Runtime.InteropServices;

namespace LfWindows.Interop;

public static class Imm32
{
    [DllImport("imm32.dll")]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    public static extern bool ImmGetOpenStatus(IntPtr hIMC);

    [DllImport("imm32.dll")]
    public static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);

    [DllImport("imm32.dll")]
    public static extern bool ImmGetConversionStatus(IntPtr hIMC, out int lpfdwConversion, out int lpfdwSentence);

    [DllImport("imm32.dll")]
    public static extern bool ImmSetConversionStatus(IntPtr hIMC, int fdwConversion, int fdwSentence);

    public const int IME_CMODE_ALPHANUMERIC = 0x0000;
    public const int IME_CMODE_NATIVE = 0x0001;
    public const int IME_CMODE_CHINESE = IME_CMODE_NATIVE;
    
    public static void SwitchToEnglish(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        IntPtr hImc = ImmGetContext(hWnd);
        if (hImc != IntPtr.Zero)
        {
            // Method 1: Set Open Status to false (Close IME)
            // This is effective for Microsoft Pinyin to switch to "English" mode
            ImmSetOpenStatus(hImc, false);

            // Method 2: Set Conversion Mode to Alphanumeric
            // Some IMEs might need this if OpenStatus doesn't fully disable it
            // ImmSetConversionStatus(hImc, IME_CMODE_ALPHANUMERIC, 0);

            ImmReleaseContext(hWnd, hImc);
        }
    }
}
