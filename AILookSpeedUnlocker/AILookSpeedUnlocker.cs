using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace AILookSpeedUnlocker
{
    [BepInPlugin(GUID, PluginName, PluginVersion)]
    public class AILookSpeedUnlocker : BaseUnityPlugin
    {
        public const string GUID = "com.getraid.ai.lookspeedunlocker";
        public const string PluginName = "Look Speed Unlocker";
        public const string PluginVersion = "1.0.0";
        
        private readonly ConfigDefinition SensitivityDefinition = new ConfigDefinition("", "Look Sensitivity Factor");
        private ConfigEntry<float> SensitivityEntry;

        private static AILookSpeedUnlocker Instance;

        private void Awake()
        {
            var harmony = new Harmony(GUID);
            harmony.PatchAll();

            SensitivityEntry = Config.AddSetting(SensitivityDefinition, 1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0.01f, 10.0f)));
            Instance = this;
        }

        public static float Sensitivity => Instance.SensitivityEntry.Value;
    }
}
