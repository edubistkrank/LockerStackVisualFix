using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace LockerStackVisualFix;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
[BepInDependency("com.russjudge.visiblelockerinterior", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.snmodding.nautilus", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    private Harmony harmony;

    internal static ManualLogSource Log { get; private set; }

    private void Awake()
    {
        Log = Logger;
        harmony = new Harmony(PluginInfo.Guid);
        harmony.PatchAll();

        Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }
}
