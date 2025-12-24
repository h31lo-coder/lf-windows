using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace LfWindows.Services;

public class RecycleBinService
{
    [SupportedOSPlatform("windows")]
    public async Task<bool> RestoreFileAsync(string fileName, string originalDirectory)
    {
        return await Task.Run(() =>
        {
            try
            {
                Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null) return false;

                object? shellObj = Activator.CreateInstance(shellAppType);
                if (shellObj == null) return false;

                dynamic shell = shellObj;
                dynamic recycleBin = shell.NameSpace(10); // BITBUCKET

                if (recycleBin == null) 
                {
                    return false;
                }

                // Find the index for "Original Location"
                int pathColumnIndex = -1;
                for (int i = 0; i < 20; i++)
                {
                    string header = recycleBin.GetDetailsOf(null, i);
                    if (!string.IsNullOrEmpty(header) && 
                       (header.Contains("Original Location") || header.Contains("原位置") || header.Contains("来源") || header.Contains("In folder")))
                    {
                        pathColumnIndex = i;
                        break;
                    }
                }

                foreach (dynamic item in recycleBin.Items())
                {
                    string name = item.Name;
                    if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) continue;

                    // Check path if we found the column
                    if (pathColumnIndex != -1)
                    {
                        string origin = recycleBin.GetDetailsOf(item, pathColumnIndex);
                        
                        if (!string.IsNullOrEmpty(origin))
                        {
                             // Normalize paths for comparison
                             string normOrigin = origin.Replace("\\", "/").TrimEnd('/');
                             string normTarget = originalDirectory.Replace("\\", "/").TrimEnd('/');
                             
                             // Note: Origin might contain special invisible characters in some Windows versions (LTR marks), 
                             // but let's try simple comparison first.
                             if (!string.Equals(normOrigin, normTarget, StringComparison.OrdinalIgnoreCase))
                             {
                                 continue;
                             }
                        }
                    }

                    // Found the item. Try to restore using Verbs.
                    bool verbExecuted = false;
                    foreach (dynamic verb in item.Verbs())
                    {
                        string vName = verb.Name;
                        string lowerName = vName.ToLower();
                        // Remove accelerator key markers (&)
                        lowerName = lowerName.Replace("&", "");
                        
                        if (lowerName.Contains("restore") || lowerName.Contains("还原") || lowerName.Contains("放回"))
                        {
                            verb.DoIt();
                            verbExecuted = true;
                            return true;
                        }
                    }
                    
                    if (!verbExecuted)
                    {
                        // Fallback to InvokeVerb("restore") if loop failed
                        try {
                            item.InvokeVerb("restore");
                            return true;
                        } catch { }
                        
                        try {
                            item.InvokeVerb("undelete");
                            return true;
                        } catch { }
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        });
    }
}
