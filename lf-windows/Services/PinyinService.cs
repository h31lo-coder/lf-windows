using System;
using System.Collections.Generic;
using System.Text;
using TinyPinyin;

namespace LfWindows.Services;

public interface IPinyinService
{
    bool Matches(string text, string pattern, bool anchor = false);
    char GetFirstPinyinChar(char c);
    string GetPinyinInitials(string text);
}

public class PinyinService : IPinyinService
{
    public bool Matches(string text, string pattern, bool anchor = false)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            return false;

        if (anchor)
        {
            // 1. Standard StartsWith
            if (text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            // 2. Pinyin StartsWith (Only if pattern is ASCII and text has non-ASCII)
            if (IsAsciiString(pattern))
            {
                string pinyinInitials = GetPinyinInitials(text);
                if (pinyinInitials.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // 1. Standard Case-Insensitive Substring Match
        if (text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // 2. Pinyin Match (Only if pattern is ASCII and text has non-ASCII)
        if (IsAsciiString(pattern))
        {
            // Convert text to Pinyin Initials
            string pinyinInitials = GetPinyinInitials(text);
            if (pinyinInitials.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private bool IsAsciiString(string text)
    {
        foreach (char c in text)
        {
            if (c > 127) return false;
        }
        return true;
    }

    private bool IsAscii(char c)
    {
        return c <= 127;
    }

    private bool IsChinese(char c)
    {
        return PinyinHelper.IsChinese(c);
    }

    public string GetPinyinInitials(string text)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in text)
        {
            if (IsChinese(c))
            {
                sb.Append(GetFirstPinyinChar(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public char GetFirstPinyinChar(char c)
    {
        try 
        {
            string pinyin = PinyinHelper.GetPinyin(c);
            if (!string.IsNullOrEmpty(pinyin))
            {
                return char.ToLowerInvariant(pinyin[0]);
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return char.ToLowerInvariant(c);
    }
}
