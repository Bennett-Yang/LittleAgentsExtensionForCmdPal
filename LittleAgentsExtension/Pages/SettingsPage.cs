using LittleAgentsExtension.Common;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Globalization;
using System.IO;

namespace LittleAgentsExtension;

internal sealed class LittleAgentsSettingsManager : JsonSettingsManager
{
    private const string SystemPrefixKey = "systemPrefix";
    private const string TemperatureKey = "temperature";
    private const string DefaultTemperatureText = "1.0";

    private readonly TextSetting _systemPrefix;
    private readonly TextSetting _temperature;

    public LittleAgentsSettingsManager()
        : this(Path.Combine(PathHelper.LocalStateDir, "settings.json"))
    {
    }

    internal LittleAgentsSettingsManager(string filePath)
    {
        FilePath = filePath;
        _systemPrefix = new TextSetting(SystemPrefixKey, "System-prompt prefix", "Optional text prepended to every agent's system prompt", string.Empty);
        _temperature = new TextSetting(TemperatureKey, "Default temperature", "0.0 - 2.0", DefaultTemperatureText)
        {
            Placeholder = DefaultTemperatureText,
        };
        Settings.Add(_systemPrefix);
        Settings.Add(_temperature);
        LoadSettings();
    }

    public RuntimeSettings ReadRuntimeSettings()
    {
        return new RuntimeSettings(_systemPrefix.Value ?? string.Empty, ParseTemperature(_temperature.Value ?? string.Empty));
    }

    internal static double? ParseTemperature(string value)
    {
        string text = value.Trim();
        if (text.Length == 0)
        {
            return 1.0;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return 1.0;
        }

        return parsed is >= 0.0 and <= 2.0 ? parsed : 1.0;
    }
}
