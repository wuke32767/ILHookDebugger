using System;
using System.Diagnostics;

namespace Celeste.Mod.ILHookDebugger;

public class ILHookDebuggerModule : EverestModule
{
    public static ILHookDebuggerModule Instance { get; private set; }

    public override Type SettingsType => typeof(ILHookDebuggerModuleSettings);
    public static ILHookDebuggerModuleSettings Settings => (ILHookDebuggerModuleSettings)Instance._Settings;

    public override Type SessionType => typeof(ILHookDebuggerModuleSession);
    public static ILHookDebuggerModuleSession Session => (ILHookDebuggerModuleSession)Instance._Session;

    public override Type SaveDataType => typeof(ILHookDebuggerModuleSaveData);
    public static ILHookDebuggerModuleSaveData SaveData => (ILHookDebuggerModuleSaveData)Instance._SaveData;

    public ILHookDebuggerModule()
    {
        Instance = this;
#if DEBUG
        // debug builds use verbose logging
        Logger.SetLogLevel(nameof(ILHookDebuggerModule), LogLevel.Verbose);
#else
        // release builds use info logging to reduce spam in log files
        Logger.SetLogLevel(nameof(ILHookDebuggerModule), LogLevel.Info);
#endif
    }

    public override void Load()
    {

        AutoRefresh(Settings?.AutoRefresh ?? false);
        HookMonoModInternal(Settings?.HookMonoModInternal ?? false);
        UnloadWhenDetached(Settings?.UnloadWhenDetached ?? false);

        // TODO: apply any hooks that should always be active
    }

    public override void Unload()
    {
        PrintingPod.Clear();
        AutoRefresh(false);
        HookMonoModInternal(false);
        UnloadWhenDetached(false);
        // TODO: unapply any hooks applied in Load()
    }
    bool freshing = false;
    internal void AutoRefresh(bool should)
    {
        if (freshing == should)
        {
            return;
        }
        freshing = should;
        if (freshing)
        {

        }
        else
        {

        }
    }

    bool criming = false;
    internal void HookMonoModInternal(bool should)
    {
        if (criming == should)
        {
            return;
        }
        criming = should;
        if (criming)
        {
            Logger.Error(nameof(ILHookDebugger), "Hooking MonoMod Internal as requested.");
            try
            {

            }
            catch (Exception e)
            {
                Logger.Error(nameof(ILHookDebugger), "Failed! Reset Settings.");
                Settings.HookMonoModInternal = false;
                throw new InvalidOperationException($"""
                {nameof(ILHookDebugger)} was failed when hooking MonoMod internal. Settings was reset.
                """, e);
            }
        }
        else
        {

        }
    }
    bool lazying = false;
    internal void UnloadWhenDetached(bool should)
    {
        if (lazying == should)
        {
            return;
        }
        lazying = should;
        if (lazying)
        {
            On.Monocle.Engine.Update += Engine_Update;
        }
        else
        {
            On.Monocle.Engine.Update -= Engine_Update;
        }
    }
    static bool attached;
    private static void Engine_Update(On.Monocle.Engine.orig_Update orig, Monocle.Engine self, Microsoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);
        if (attached && !Debugger.IsAttached)
        {
            if (Settings?.UnloadWhenDetached ?? false)
            {
                PrintingPod.Clear();
            }
        }
        attached = Debugger.IsAttached;
    }
}