using System;
using System.IO;
using System.Text.Json;

namespace RivalsCounter
{
    public class AppConfig
    {
        public string openRouterApiKey { get; set; }
        public string geminiApiKey { get; set; }
        public string visionModel { get; set; } = "google/gemini-2.5-pro";
        public string hotkeyCapture { get; set; } = "Ctrl+Shift+S";
    }

    public static class Config
    {
        public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RivalsCounter");
        public static string PathFile = Path.Combine(Dir, "config.json");
        public static AppConfig Current = new AppConfig();

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(PathFile))
                    Current = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(PathFile)) ?? new AppConfig();
            }
            catch { Current = new AppConfig(); }
            if (string.IsNullOrWhiteSpace(Current.visionModel)) Current.visionModel = "google/gemini-2.5-pro";
        }

        public static void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static bool HasVisionKey =>
            !string.IsNullOrWhiteSpace(Current.openRouterApiKey) || !string.IsNullOrWhiteSpace(Current.geminiApiKey);
    }
}
