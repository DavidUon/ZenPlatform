using System;
using System.IO;
using System.Text.Json;

namespace ZenPlatform.Core
{
    public sealed class AppState
    {
        public string? TargetUrl { get; set; }
        public string? CurContract { get; set; }
        public int ContractYear { get; set; }
        public int ContractMonth { get; set; }
        public int ViewPeriod { get; set; }
        public int SelectedSessionIndex { get; set; }
        public int? PriceMode { get; set; }
    }

    public static class AppStateStore
    {
        private const string FileName = "app_state.json";

        public static string GetPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        }

        public static AppState? Load()
        {
            try
            {
                var path = GetPath();
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppState>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void Save(AppState state)
        {
            try
            {
                var path = GetPath();
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignore save failures.
            }
        }
    }
}
