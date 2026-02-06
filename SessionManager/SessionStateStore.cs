using System;
using System.IO;
using System.Text.Json;

namespace ZenPlatform.SessionManager
{
    public static class SessionStateStore
    {
        public static string GetPath(int index)
        {
            var fileName = $"strategy{index + 1}.json";
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

        public static void Save(SessionManager manager)
        {
            try
            {
                var path = GetPath(manager.Index);
                var json = JsonSerializer.Serialize(manager.ExportState(), new JsonSerializerOptions
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

        public static void SaveAll(SessionManager[] managers)
        {
            foreach (var manager in managers)
            {
                Save(manager);
            }
        }

        public static void Load(SessionManager manager)
        {
            try
            {
                var path = GetPath(manager.Index);
                if (!File.Exists(path))
                {
                    return;
                }

                var json = File.ReadAllText(path);
                var state = JsonSerializer.Deserialize<SessionManagerState>(json);
                if (state != null)
                {
                    manager.ImportState(state);
                }
            }
            catch
            {
                // Ignore load failures.
            }
        }

        public static void LoadAll(SessionManager[] managers)
        {
            foreach (var manager in managers)
            {
                Load(manager);
            }
        }
    }
}
