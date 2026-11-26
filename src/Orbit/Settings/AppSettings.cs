using System.Text.Json;
using Microsoft.Win32;
using Orbit.Core;

namespace Orbit.Settings;

internal sealed class AppSettings
{
    public const string AutostartArgument = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Orbit";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Orbit",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool HoldEnabled { get; set; } = true;

    public bool ShakeEnabled { get; set; } = true;

    public bool HotkeyEnabled { get; set; } = true;

    public bool KeepOriginals { get; set; }

    public bool Paused { get; set; }

    public int? ToastLeft { get; set; }

    public int? ToastTop { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("Настройки не прочитались, беру стандартные", error);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error("Настройки не сохранились", error);
        }
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\" {AutostartArgument}");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
    }
}
