using UnityEngine;

namespace pip_cam
{
    // Entry point. The game discovers this type through GetExportedTypes, so it is the only public type in the assembly.
    public class pip_cam_mod : AbstractMod, IModSettings
    {
        internal const string identifier = "xupisco@PipCam";
        internal const string open_hotkey_id = identifier + "/open";

        private KeyGroup key_group;
        private KeyMapping[] key_mappings;
        private GameObject behaviour_object;

        public override string getName()
        {
            return "PiP Cam";
        }

        public override string getDescription()
        {
            return texts.mod_description;
        }

        public override string getVersionNumber()
        {
            return "0.4.3";
        }

        public override string getIdentifier()
        {
            return identifier;
        }

        // The windows are each player's own, so nothing here reaches the other players.
        public override bool isMultiplayerModeCompatible()
        {
            return true;
        }

        public override bool isRequiredByAllPlayersInMultiplayerMode()
        {
            return false;
        }

        public override void onEnabled()
        {
            pip_cam_settings.load();
            register_hotkeys();
            behaviour_object = new GameObject("pip_cam");
            Object.DontDestroyOnLoad(behaviour_object);
            behaviour_object.AddComponent<pip_windows>();
        }

        public override void onDisabled()
        {
            if (behaviour_object != null)
            {
                // The windows close and their cameras go away with it.
                Object.Destroy(behaviour_object);
                behaviour_object = null;
            }
            unregister_hotkeys();
        }

        public void onSettingsOpened()
        {
            pip_cam_settings.load();
        }

        public void onSettingsClosed()
        {
            pip_cam_settings.save();
        }

        public void onDrawSettingsUI()
        {
            GUILayout.BeginVertical();
            pip_cam_settings.resolution = slider(texts.resolution_label, pip_cam_settings.resolution, 0.25f, 1f, 0.05f, (pip_cam_settings.resolution * 100f).ToString("0") + "%");
            pip_cam_settings.frames_per_second = slider(texts.frames_per_second_label, pip_cam_settings.frames_per_second, 5f, 60f, 1f, pip_cam_settings.frames_per_second.ToString("0"));
            pip_cam_settings.max_windows = Mathf.RoundToInt(slider(texts.max_windows_label, pip_cam_settings.max_windows, 1f, 6f, 1f, pip_cam_settings.max_windows.ToString()));
            GUILayout.Space(8f);
            GUILayout.Label(texts.controls_hint);            GUILayout.EndVertical();
        }

        private static float slider(string label, float value, float min, float max, float step, string shown)
        {
            GUILayout.Label(label + ": " + shown);
            float moved = GUILayout.HorizontalSlider(value, min, max);
            return Mathf.Round(moved / step) * step;
        }

        // The group is registered explicitly, because registering one that already exists makes the game throw, which
        // the implicit per-mod group would hit when the mod is enabled again.
        private void register_hotkeys()
        {
            InputManager input = ScriptableSingleton<InputManager>.Instance;

            key_group = new KeyGroup(identifier);
            key_group.keyGroupName = getName();
            input.registerKeyGroup(key_group);

            // F4, since every letter is taken by the game or by common mods, and the game's own function keys stop at F3
            // before F10.
            key_mappings = new[]
            {
                key_mapping(open_hotkey_id, KeyCode.F4, texts.open_key_name, texts.open_key_description),
            };
            foreach (KeyMapping mapping in key_mappings)
            {
                input.registerKeyMapping(mapping);
            }
        }

        private static KeyMapping key_mapping(string id, KeyCode key, string name, string description)
        {
            KeyMapping mapping = new KeyMapping(id, key, KeyCode.None);
            mapping.keyName = name;
            mapping.keyDescription = description;
            mapping.keyGroupIdentifier = identifier;
            return mapping;
        }

        private void unregister_hotkeys()
        {
            InputManager input = ScriptableSingleton<InputManager>.Instance;
            foreach (KeyMapping mapping in key_mappings)
            {
                input.unregisterKeyMapping(mapping);
            }
            input.unregisterKeyGroup(key_group);
            key_mappings = null;
            key_group = null;
        }
    }
}
