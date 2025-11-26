using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace SkillsConfigurator
{
    public static class ModCompatabilities
    {
        public static class RiskOfOptionsCompatability
        {
            public const string GUID = "com.rune580.riskofoptions";
            public static void AddConfig<T>(T config) where T : ConfigEntryBase
            {
                if (config is ConfigEntry<float>)
                {
                    ModSettingsManager.AddOption(new FloatFieldOption(config as ConfigEntry<float>));
                }
                if (config is ConfigEntry<bool>)
                {
                    ModSettingsManager.AddOption(new CheckBoxOption(config as ConfigEntry<bool>));
                }
                if (config is ConfigEntry<int>)
                {
                    ModSettingsManager.AddOption(new IntFieldOption(config as ConfigEntry<int>));
                }
                if (config is ConfigEntry<string>)
                {
                    ModSettingsManager.AddOption(new StringInputFieldOption(config as ConfigEntry<string>));
                }
            }
        }
    }
}
