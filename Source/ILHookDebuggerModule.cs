using Celeste.Mod.ILHookDebugger.MappingUtils;
using Celeste.Mod.MappingUtils.ImGuiHandlers;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using YamlDotNet.Core.Tokens;

namespace Celeste.Mod.ILHookDebugger;

public class ILHookDebuggerModule : EverestModule
{
    public static ILHookDebuggerModule Instance { get; private set; } = null!;

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
        OnMonoMod();
        //AutoRefresh.Value = Settings?.AutoRefresh ?? false;
        //HookMonoModInternal.Value = Settings?.HookMonoModInternal ?? false;
        //UnloadWhenDetached.Value = Settings?.UnloadWhenDetached ?? false;
        //MappingUtilsIntegration.Value = Settings?.MappingUtilsIntegration ?? false;

        // TODO: apply any hooks that should always be active
    }

    public override void Unload()
    {
        PrintingPod.Clear();
        IgnoreDebugger();
        UnIntegrate();
        // TODO: unapply any hooks applied in Load()
    }

    static ILHook? MonoModCriminal;

    public static void OnMonoMod()
    {
        DetourManager.ILHookApplied += info =>
        {
            if (info.ManipulatorMethod.Module != typeof(ILHookDebuggerModule).Module
                && PrintingPod.DuplicantLookup.ContainsKey(info.Method.Method))
            {
                if (Engine.Scene is not null)
                {
                    Engine.Scene.OnEndOfFrame += () =>
                {
                    PrintingPod.Refresh(info.Method.Method);
                };
                }
            }
        };
    }


    public static Swapping UnloadWhenDetached = new(() =>
    {
        On.Monocle.Engine.Update += Engine_Update;
    }, IgnoreDebugger);
    static void IgnoreDebugger()
    {
        On.Monocle.Engine.Update -= Engine_Update;
    }
    internal static Lazy<bool> CheckMappingUtils = new(() =>
    {
        if (Instance!.Metadata.OptionalDependencies.Any(i => i.Name == "MappingUtils"))
        {
            if (Everest.Loader.TryGetDependency(new() { Name = "MappingUtils", Version = new(1, 0, 0) }, out var result))
            {
                // <=
                if (Everest.Loader.VersionSatisfiesDependency(result.Metadata.Version, new Version(1, 9, 0)))
                {
                    return true;
                }
            }
        }
        return false;
    });
    // mappingutils can be not loaded
    static object? toremove;
    public static Swapping MappingUtilsIntegration = new(() =>
    {
        if (CheckMappingUtils.Value)
        {
            extract();
            static void extract()
            {
                MainMappingUtils.Tabs.Add((Tab)(toremove = new FrostyPrintingPod()));
            }
        }

    }, UnIntegrate);
    static void UnIntegrate()
    {
        if (toremove is not null)
        {
            extract();
            static void extract()
            {
                MainMappingUtils.Tabs.Remove((Tab)toremove!);
            }
        }
        toremove = null;
    }
    static Swapping DebuggerAttached = new(() =>
    {
    }, () =>
    {
        if (Settings?.UnloadWhenDetached ?? false)
        {
            PrintingPod.Clear();
        }
    });

    public static Swapping BreakOnce = new(i => i, i => PrintingPod.Refresh());
    public static Swapping PrettifyMonoMod = new(i => i, i => PrintingPod.Refresh());

    private static void Engine_Update(On.Monocle.Engine.orig_Update orig, Monocle.Engine self, Microsoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);
        DebuggerAttached.Value = Debugger.IsAttached;
    }
}