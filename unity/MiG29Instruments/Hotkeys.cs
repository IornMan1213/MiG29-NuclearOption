using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MiG29Instruments
{
    // The plugin's own keys (Cobra, jettison, mirrors, cockpit setups, flares / chaff) are BepInEx shortcuts, outside the game's control settings.
    // They are ignored while the player types in a text field (multiplayer chat, the mission editor: Ctrl+C is copy there) and while
    // the game is paused.
    static class Hotkeys
    {
        public static bool Down(ConfigEntry<KeyboardShortcut> key) => key != null && key.Value.IsDown() && !Blocked();
        public static bool Held(ConfigEntry<KeyboardShortcut> key) => key != null && key.Value.MainKey != KeyCode.None && key.Value.IsPressed() && !Blocked();

        static bool Blocked()
        {
            if (Time.timeScale == 0f) return true;
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == null || !sel.activeInHierarchy) return false;
            // uGUI InputField or TextMeshPro TMP_InputField, matched by name so the plugin needs no TextMeshPro reference
            foreach (var c in sel.GetComponents<Component>())
                if (c != null && c.GetType().Name.Contains("InputField")) return true;
            return false;
        }

        // shared help text for the key options
        public const string JoystickHelp = "Can be a joystick button: JoystickButton5 listens to every controller, Joystick1Button5 only to the first one " +
                                           "(controllers are numbered in the order Windows found them)";
    }
}
