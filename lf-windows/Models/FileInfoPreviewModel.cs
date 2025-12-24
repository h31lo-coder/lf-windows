using System;

namespace LfWindows.Models;

public class FileInfoPreviewModel
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileSize { get; set; } = string.Empty;
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
    public string Accessed { get; set; } = string.Empty;
    public string Attributes { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
}
