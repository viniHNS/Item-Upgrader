using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.Utilities;
using SPT.Reflection.Patching;
using UnityEngine;

namespace viniHNS.itemUpgrader
{
    [BepInPlugin(PluginGuid, "itemUpgrader", "1.0.0")]
    [BepInDependency("com.tyfon.uifixes", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "viniHNS.itemUpgrader";

        public static ManualLogSource LogSource;

        public static ConfigEntry<bool> EnableMod;
        public static ConfigEntry<bool> ConfirmBeforeUpgrade;
        public static ConfigEntry<float> SpinSeconds;
        public static ConfigEntry<bool> PlaySounds;
        public static ConfigEntry<bool> DebugLog;

        /// <summary>Icon shown next to the context menu entry.</summary>
        public static Sprite MenuIcon;

        /// <summary>True once the server data loaded and the patches are on.</summary>
        public static bool Ready;

        private void Awake()
        {
            LogSource = Logger;
            InitConfiguration();

            if (!UpgraderData.Load())
            {
                LogSource.LogError("itemUpgrader disabled: the server did not answer /itemUpgrader/data. Is the server part of the mod installed?");
                return;
            }

            MenuIcon = LoadMenuIcon();

            var patched = EnablePatch(new ContextMenuPatch())
                          & EnablePatch(new ContextButtonOrderPatch())
                          & EnablePatch(new EscapePatch());

            Ready = patched;
            LogSource.LogInfo($"itemUpgrader loaded with {UpgraderData.TargetCount} result items (patches {(patched ? "on" : "FAILED")})");
        }

        private void InitConfiguration()
        {
            EnableMod = Config.Bind(
                "General",
                "Enable",
                true,
                new ConfigDescription("Show the Upgrade item entry in the item context menu.", null, new ConfigurationManagerAttributes { Order = 10 })
            );

            ConfirmBeforeUpgrade = Config.Bind(
                "General",
                "Confirm before upgrading",
                true,
                new ConfigDescription("Ask for confirmation before the item is consumed.", null, new ConfigurationManagerAttributes { Order = 9 })
            );

            SpinSeconds = Config.Bind(
                "General",
                "Spin duration",
                3.5f,
                new ConfigDescription("How long the needle spins before revealing the result, in seconds.", new AcceptableValueRange<float>(1f, 10f), new ConfigurationManagerAttributes { Order = 8 })
            );

            PlaySounds = Config.Bind(
                "General",
                "Sounds",
                true,
                new ConfigDescription("Play the game UI sounds while spinning and on the result.", null, new ConfigurationManagerAttributes { Order = 7 })
            );

            DebugLog = Config.Bind(
                "Debug",
                "Log menu decisions",
                true,
                new ConfigDescription("Write to LogOutput.log why the Upgrade item entry was shown or skipped for each right-click.", null, new ConfigurationManagerAttributes { Order = 1 })
            );
        }

        private static bool EnablePatch(ModulePatch patch)
        {
            try
            {
                patch.Enable();
                return true;
            }
            catch (Exception ex)
            {
                LogSource.LogError($"itemUpgrader: could not enable {patch.GetType().Name}: {ex}");
                return false;
            }
        }

        /// <summary>White double chevron embedded in the DLL; falls back to a vanilla context menu icon.</summary>
        private static Sprite LoadMenuIcon()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("itemUpgrader.upgrade_icon.png"))
                {
                    if (stream != null)
                    {
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                            {
                                filterMode = FilterMode.Bilinear,
                                wrapMode = TextureWrapMode.Clamp,
                            };
                            if (ImageConversion.LoadImage(texture, memory.ToArray()))
                            {
                                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSource.LogWarning($"itemUpgrader: could not load the embedded icon: {ex.Message}");
            }

            try
            {
                return ResourcesCache.Pop<Sprite>("Characteristics/Icons/Modding");
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Subset of the ConfigurationManager attributes used to order the F12 entries.</summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
    }
}
