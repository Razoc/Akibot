using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkiBot.App.Models;

namespace AkiBot.App.Services
{
    public class SettingsManager
    {
        private readonly string _settingsPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private AppSettings? _cachedSettings;

        public SettingsManager(string rootDirectory)
        {
            _settingsPath = Path.Combine(rootDirectory, "Build", "akibot-settings.json");
            // RecompLauncher writes its settings (recomplauncher.json) as text: "upscale": "3", "fullscreen": "true".
            // Read them as numbers and booleans, or the whole file falls back to the defaults.
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            _jsonOptions.Converters.Add(new BoolFromTextConverter());
        }

        // true/false, or the same as text ("true", "1", "false", "0").
        private sealed class BoolFromTextConverter : JsonConverter<bool>
        {
            public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.True: return true;
                    case JsonTokenType.False: return false;
                    case JsonTokenType.Number: return reader.GetInt64() != 0;
                    case JsonTokenType.String:
                        var text = (reader.GetString() ?? "").Trim();
                        return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
                    default: throw new JsonException("Not a boolean");
                }
            }

            public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
        }

        public AppSettings Load()
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
                    // Older files only have the preset index ("rumble"); make the percent explicit.
                    _cachedSettings.NormalizeRumble();
                    return _cachedSettings;
                }
                catch
                {
                    _cachedSettings = new AppSettings();
                    return _cachedSettings;
                }
            }

            _cachedSettings = new AppSettings();
            return _cachedSettings;
        }

        public void Save(AppSettings settings)
        {
            _cachedSettings = settings;
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }

        public string GetSettingsPath() => _settingsPath;
    }
}