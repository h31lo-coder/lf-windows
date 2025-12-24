using LfWindows.Models;
using System;

namespace LfWindows.Services;

public interface IConfigService
{
    AppConfig Current { get; }
    void Load();
    void Save();
    event EventHandler ConfigChanged;
}
