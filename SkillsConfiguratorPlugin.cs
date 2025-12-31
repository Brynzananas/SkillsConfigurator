using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HG.Coroutines;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Skills;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Security.Permissions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static RoR2.Skills.SkillFamily;

[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
[assembly: HG.Reflection.SearchableAttribute.OptIn]
[assembly: HG.Reflection.SearchableAttribute.OptInAttribute]
[module: UnverifiableCode]
#pragma warning disable CS0618
#pragma warning restore CS0618
namespace SkillsConfigurator
{
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [BepInDependency(ModCompatabilities.RiskOfOptionsCompatability.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [System.Serializable]
    public class SkillsConfiguratorPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.brynzananas.skillsconfigurator";
        public const string ModName = "Skills Configurator";
        public const string ModVer = "1.1.0";
        public static bool riskOfOptionsEnabled { get; private set; }
        public static ConfigFile configFile { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static Dictionary<SkillDef, List<ConfigEntryBase>> skillConfigs = [];
        public static Dictionary<string, int> names = [];
        public static Dictionary<SkillDef, string> skillToNewName = [];
        private static bool _debug;
        public static bool debug
        {
            get => _debug;
            set
            {
                if (value == _debug) return;
                _debug = value;
                if (value)
                {
                    On.RoR2.Loadout.BodyLoadoutManager.SetSkillVariant += BodyLoadoutManager_SetSkillVariant;
                }
                else
                {
                    On.RoR2.Loadout.BodyLoadoutManager.SetSkillVariant -= BodyLoadoutManager_SetSkillVariant;
                }
            }
        }
        private static Stopwatch stopwatch;
        public void Awake()
        {
            configFile = Config;
            Log = Logger;
            riskOfOptionsEnabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(ModCompatabilities.RiskOfOptionsCompatability.GUID);
        }
        public void OnDestroy()
        {
            debug = false;
        }

        private static void BodyLoadoutManager_SetSkillVariant(On.RoR2.Loadout.BodyLoadoutManager.orig_SetSkillVariant orig, Loadout.BodyLoadoutManager self, BodyIndex bodyIndex, int skillSlot, uint skillVariant)
        {
            orig(self, bodyIndex, skillSlot, skillVariant);
            if (skillSlot < 0 || skillVariant < 0) return;
            int index = (int)bodyIndex;
            if (index < 0 || index >= Loadout.BodyLoadoutManager.allBodyInfos.Length) return;
            GenericSkill[] genericSkills = Loadout.BodyLoadoutManager.allBodyInfos[index].prefabSkillSlots;
            if (genericSkills == null || skillSlot >= genericSkills.Length) return;
            GenericSkill genericSkill = genericSkills[skillSlot];
            if (!genericSkill) return;
            SkillFamily skillFamily = genericSkill.skillFamily;
            if (!skillFamily || skillVariant >= skillFamily.variants.Length) return;
            SkillDef skillDef = skillFamily.variants[skillVariant].skillDef;
            if (!skillDef) return;
            string skillName;
            if (skillToNewName.ContainsKey(skillDef))
            {
                skillName = skillToNewName[skillDef];
            }
            else
            {
                skillName = (skillDef as ScriptableObject).name;
            }
            Log.LogMessage("Selected skill: \"" + skillName + "\" AKA \"" + skillDef.skillName + "\" and \"" + Language.GetString(skillDef.skillNameToken) + "\"");
        }
        [ConCommand(commandName = "skillconfigurator_debug", flags = ConVarFlags.None)]
        public static void CCReset(ConCommandArgs args)
        {
            debug = args.GetArgBool(0);
            if (debug)
            {
                Log.LogMessage("Enabled debug");
            }
            else
            {
                Log.LogMessage("Disabled debug");
            }
        }

        /*[ConCommand(commandName = "regenerate_skill_configs", flags = ConVarFlags.None)]
public static void CCReset(ConCommandArgs args)
{
   Reset();
}
public static void Reset()
{
   skillConfigs.Clear();
   names.Clear();
   ConfigureSkillsStart();
}*/
        [SystemInitializer(typeof(SkillCatalog))]
        private static void ConfigureSkillsStart()
        {
            Log.LogMessage("Begin configuring skills");
            ParallelCoroutine loadCoroutine = new ParallelCoroutine();
            stopwatch = Stopwatch.StartNew();
            stopwatch.Start();
            int i = 0;
            foreach (SkillDef skillDef in SkillCatalog.allSkillDefs)
            {
                i++;
                loadCoroutine.Add(ConfigureSkillThread(skillDef, i));
            }
            IEnumerator runLoadCoroutine()
            {
                yield return loadCoroutine;
                Log.LogMessage("Finished configuring skills. Time took: " + stopwatch.ElapsedMilliseconds + "ms");
                stopwatch.Stop();
            }
            RoR2Application.instance.StartCoroutine(runLoadCoroutine());
        }
        private static string HandleString(string @string)
        {
            if (@string.IsNullOrWhiteSpace()) return @string;
            char[] forbiddenCharacters = { '\n', '\t', '\"', '\'', '[', ']' };
            foreach (char forbiddenCharacter in forbiddenCharacters)
            {
                while (@string.Contains(forbiddenCharacter))
                {
                    @string = @string.Replace(forbiddenCharacter, ' ');
                }
            }
            @string.Trim();
            int namesCount = 0;
            while (names.ContainsKey(@string + (namesCount == 0 ? "" : namesCount)))
            {
                namesCount++;
            }
            @string += (namesCount == 0 ? "" : namesCount);
            names.Add(@string, namesCount);
            return @string;
        }
        private static IEnumerator ConfigureSkillThread(SkillDef skillDef, int loc)
        {
            string sectionName = (skillDef as ScriptableObject).name;
            sectionName = HandleString(sectionName);
            if (sectionName.IsNullOrWhiteSpace())
            {
                sectionName = HandleString(skillDef.skillName);
            }
            if (sectionName.IsNullOrWhiteSpace()) yield break;
            if (skillConfigs.ContainsKey(skillDef)) yield break;
            skillToNewName.Add(skillDef, sectionName);
            List<ConfigEntryBase> configEntryBases = [];
            skillConfigs.Add(skillDef, configEntryBases);
            yield return null;
            ConfigEntry<bool> enable = CreateConfig(sectionName, "Enable Config", false, "Enable configuration for this skill? AKA \"" + skillDef.skillName + "\" and \"" + Language.GetString(skillDef.skillNameToken) + "\"", null, false);
            if (enable.Value)
            {
                yield return null;
                ConfigEntry<float> baseRechargeInterval = CreateConfig(sectionName, "Base Recharge Interval", skillDef.baseRechargeInterval, "How long it takes for this skill to recharge after being used.", configEntryBases, true);
                yield return null;
                ConfigEntry<int> baseMaxStock = CreateConfig(sectionName, "Base Max Stock", skillDef.baseMaxStock, "Maximum number of charges this skill can carry.", configEntryBases, true);
                yield return null;
                ConfigEntry<int> rechargeStock = CreateConfig(sectionName, "Recharge Stock", skillDef.rechargeStock, "How much stock to restore on a recharge.", configEntryBases, true);
                yield return null;
                ConfigEntry<int> requiredStock = CreateConfig(sectionName, "Required Stock", skillDef.requiredStock, "How much stock is required to activate this skill.", configEntryBases, true);
                yield return null;
                ConfigEntry<int> stockToConsume = CreateConfig(sectionName, "Stock To Consume", skillDef.stockToConsume, "How much stock to deduct when the skill is activated.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> attackSpeedBuffsRestockSpeed = CreateConfig(sectionName, "Attack Speed Buffs Restock Speed", skillDef.attackSpeedBuffsRestockSpeed, "Makes the skill restock interval divided by attack speed if checked.", configEntryBases, true);
                yield return null;
                ConfigEntry<float> attackSpeedBuffsRestockSpeed_Multiplier = CreateConfig(sectionName, "Attack Speed Buffs Restock Speed Multiplier", skillDef.attackSpeedBuffsRestockSpeed_Multiplier, "Increases the efficacy of attack speed on restock time.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> resetCooldownTimerOnUse = CreateConfig(sectionName, "Reset Cooldown Timer On Use", skillDef.resetCooldownTimerOnUse, "Whether or not it resets any progress on cooldowns.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> fullRestockOnAssign = CreateConfig(sectionName, "Full Restock On Assign", skillDef.fullRestockOnAssign, "Whether or not to fully restock this skill when it's assigned.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> dontAllowPastMaxStocks = CreateConfig(sectionName, "Dont Allow Past Max Stocks", skillDef.dontAllowPastMaxStocks, "Whether or not this skill can hold past it's maximum stock.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> beginSkillCooldownOnSkillEnd = CreateConfig(sectionName, "Begin Skill Colldown On Skill End", skillDef.beginSkillCooldownOnSkillEnd, "Whether or not the cooldown waits until it leaves the set state.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> isCooldownBlockedUntilManuallyReset = CreateConfig(sectionName, "Is Cooldown Blocked Until Manually Reset", skillDef.isCooldownBlockedUntilManuallyReset, "Whether or not the skill is blocked from being used until it is manually reset", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> cancelSprintingOnActivation = CreateConfig(sectionName, "Cancel Sprinting On Activation", skillDef.cancelSprintingOnActivation, "Whether or not activating the skill forces off sprinting.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> forceSprintDuringState = CreateConfig(sectionName, "Force Sprint During State", skillDef.forceSprintDuringState, "Whether or not this skill is considered 'mobility'. Currently just forces sprint.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> canceledFromSprinting = CreateConfig(sectionName, "Canceled From Sprinting", skillDef.canceledFromSprinting, "Whether or not sprinting sets the skill's state to be reset.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> isCombatSkill = CreateConfig(sectionName, "Is Combat Skill", skillDef.isCombatSkill, "Whether or not this is considered a combat skill.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> mustKeyPress = CreateConfig(sectionName, "Must Key Press", skillDef.mustKeyPress, "The skill can't be activated if the key is held.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> triggeredByPressRelease = CreateConfig(sectionName, "Triggered By Press Release", skillDef.triggeredByPressRelease, "Can this skill be triggered by an key release event?", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> autoHandleLuminousShot = CreateConfig(sectionName, "Auto Handle Luminous Shot", skillDef.autoHandleLuminousShot, "If true, CharacterBody handles LuminiousShot buffs. If false, the skill must handle it.", configEntryBases, true);
                yield return null;
                ConfigEntry<bool> suppressSkillActivation = CreateConfig(sectionName, "Suppress Skill Activation", skillDef.suppressSkillActivation, "If true, CharacterBody.OnSkillActivated will not be called.", configEntryBases, true);
                yield return null;
                foreach (ConfigEntryBase configEntryBase in configEntryBases)
                {
                    ConfigEntry<float> configEntry = configEntryBase as ConfigEntry<float>;
                    if (configEntry == null)
                    {
                        ConfigEntry<int> configEntry2 = configEntryBase as ConfigEntry<int>;
                        if (configEntry2 == null)
                        {
                            ConfigEntry<bool> configEntry1 = configEntryBase as ConfigEntry<bool>;
                            if (configEntry1 == null) continue;
                            configEntry1.SettingChanged += ConfigEntry_SettingChanged;
                        }
                        else
                        {
                            configEntry2.SettingChanged += ConfigEntry_SettingChanged;
                        }
                    }
                    else
                    {
                        configEntry.SettingChanged += ConfigEntry_SettingChanged;
                    }
                }
                void UpdateSkillDef()
                {
                    skillDef.baseRechargeInterval = baseRechargeInterval.Value;
                    skillDef.baseMaxStock = baseMaxStock.Value;
                    skillDef.rechargeStock = rechargeStock.Value;
                    skillDef.requiredStock = requiredStock.Value;
                    skillDef.stockToConsume = stockToConsume.Value;
                    skillDef.attackSpeedBuffsRestockSpeed = attackSpeedBuffsRestockSpeed.Value;
                    skillDef.attackSpeedBuffsRestockSpeed_Multiplier = attackSpeedBuffsRestockSpeed_Multiplier.Value;
                    skillDef.resetCooldownTimerOnUse = resetCooldownTimerOnUse.Value;
                    skillDef.fullRestockOnAssign = fullRestockOnAssign.Value;
                    skillDef.dontAllowPastMaxStocks = dontAllowPastMaxStocks.Value;
                    skillDef.beginSkillCooldownOnSkillEnd = beginSkillCooldownOnSkillEnd.Value;
                    skillDef.isCooldownBlockedUntilManuallyReset = isCooldownBlockedUntilManuallyReset.Value;
                    skillDef.cancelSprintingOnActivation = cancelSprintingOnActivation.Value;
                    skillDef.forceSprintDuringState = forceSprintDuringState.Value;
                    skillDef.canceledFromSprinting = canceledFromSprinting.Value;
                    skillDef.isCombatSkill = isCombatSkill.Value;
                    skillDef.mustKeyPress = mustKeyPress.Value;
                    skillDef.triggeredByPressRelease = triggeredByPressRelease.Value;
                    skillDef.autoHandleLuminousShot = autoHandleLuminousShot.Value;
                    skillDef.suppressSkillActivation = suppressSkillActivation.Value;
                }
                void ConfigEntry_SettingChanged(object sender, EventArgs e) => UpdateSkillDef();
                UpdateSkillDef();
            }
        }
        private static ConfigEntry<T> CreateConfig<T>(string section, string key, T defaultValue, string description, List<ConfigEntryBase> configEntryBases, bool enableRiskOfOptions)
        {
            ConfigDefinition configDefinition = new ConfigDefinition(section, key);
            ConfigDescription configDescription = new ConfigDescription(description);
            ConfigEntry<T> entry = configFile.Bind(configDefinition, defaultValue, configDescription);
            configEntryBases?.Add(entry);
            if (enableRiskOfOptions && riskOfOptionsEnabled) ModCompatabilities.RiskOfOptionsCompatability.AddConfig(entry);
            return entry;
        }
    }
}
