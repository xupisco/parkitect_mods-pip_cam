using System;
using System.Collections.Generic;
using System.IO;
using MiniJSON;
using UnityEngine;

namespace pip_cam
{
    // The player's choices for the mod. Kept next to the game's settings rather than in the mod folder, which Workshop
    // updates can replace.
    internal static class pip_cam_settings
    {
        private static string file_path => FilePaths.getFolderPath("pip_cam.settings.json");

        private static bool loaded;

        // Of the window's size on screen. Each window draws the park again, so a lower one costs less.
        public static float resolution = 0.75f;
        // How often each window draws a new picture.
        public static float frames_per_second = 30f;
        public static int max_windows = 4;
        public static void load()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;

            try
            {
                if (File.Exists(file_path) && Json.Deserialize(File.ReadAllText(file_path)) is Dictionary<string, object> values)
                {
                    resolution = Mathf.Clamp(read_float(values, "resolution", resolution), 0.25f, 1f);
                    frames_per_second = Mathf.Clamp(read_float(values, "frames_per_second", frames_per_second), 5f, 60f);
                    max_windows = Mathf.Clamp(Mathf.RoundToInt(read_float(values, "max_windows", max_windows)), 1, 6);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[pip_cam] Couldn't read " + file_path + ": " + exception);
            }
        }

        public static void save()
        {
            try
            {
                Dictionary<string, object> values = new Dictionary<string, object>
                {
                    { "resolution", resolution },
                    { "frames_per_second", frames_per_second },
                    { "max_windows", max_windows }
                };
                File.WriteAllText(file_path, Json.Serialize(values));
            }
            catch (Exception exception)
            {
                Debug.LogError("[pip_cam] Couldn't write " + file_path + ": " + exception);
            }
        }

        // MiniJSON reads whole numbers as long and the rest as double.
        private static float read_float(Dictionary<string, object> values, string key, float fallback)
        {
            if (!values.TryGetValue(key, out object value))
            {
                return fallback;
            }
            switch (value)
            {
                case double number:
                    return (float)number;
                case long whole:
                    return whole;
                default:
                    return fallback;
            }
        }
    }
}
