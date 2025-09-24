using Celeste.Mod.ILHookDebugger.MappingUtils;
using Celeste.Mod.ImGuiHelper;
using Celeste.Mod.MappingUtils.ImGuiHandlers;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using YamlDotNet.Core.Tokens;

namespace Celeste.Mod.ILHookDebugger;
public enum Compatibility
{
    None = 0, VisualStudio, Rider, dnSpy,
};
[Flags]
public enum IDEFeatures
{
    None = 0,
    NormalizeName = 1 << 0,
    CanDebuggerLaunch = 1 << 1,
    RequiresFileAssembly = 1 << 2,
    CanOnlyModifyRefValues = 1 << 3,
    CanNotModifyValues = 1 << 4,
    CanDebuggerLaunchButNotDefault = (1 << 5) | CanDebuggerLaunch,
}

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
    public static string CachePath = Path.Combine(Everest.Loader.PathCache, "ILHookDebuggerCache");
    public override void Load()
    {
#if DEBUG
        GC.Collect();
#endif
        try
        {
            if (Directory.Exists(CachePath))
            {
                Directory.Delete(CachePath, true);
            }
        }
        catch
        {

        }
        ImGuiManager.Handlers.Add(MiGui.Instance);
        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
        DetourManager.ILHookApplied += OnHookApply;
        //AutoRefresh.Value = Settings?.AutoRefresh ?? false;
        //HookMonoModInternal.Value = Settings?.HookMonoModInternal ?? false;
        //UnloadWhenDetached.Value = Settings?.UnloadWhenDetached ?? false;
        //MappingUtilsIntegration.Value = Settings?.MappingUtilsIntegration ?? false;

        // TODO: apply any hooks that should always be active
    }

    private void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
        PrintingPod.Clear();
        GC.Collect();
        PrintingPod.Guardian.Clear();
    }

    public override void Unload()
    {
        DetourManager.ILHookApplied -= OnHookApply;

        ImGuiManager.Handlers.Remove(MiGui.Instance);
        PrintingPod.Clear();
        IgnoreDebugger();
        AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
        UnIntegrate();
        GC.Collect();
        PrintingPod.Guardian.Clear();
        // TODO: unapply any hooks applied in Load()
    }

    static ILHook? MonoModCriminal;

    static void OnHookApply(ILHookInfo info)
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
    public static IDEFeatures CurrentFeature;
    internal static OnChanged<Compatibility> IDE = new(_ => { }, o =>
    {
        CurrentFeature = o switch
        {
            Compatibility.VisualStudio =>
                IDEFeatures.NormalizeName |
                IDEFeatures.CanDebuggerLaunch,
            Compatibility.Rider =>
                IDEFeatures.NormalizeName |
                IDEFeatures.RequiresFileAssembly |
                IDEFeatures.CanDebuggerLaunchButNotDefault,
            Compatibility.dnSpy =>
                IDEFeatures.CanNotModifyValues,
            _ =>
                IDEFeatures.None,
        };
        PrintingPod.Refresh();
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
    public static Swapping TextConvertor = new(i => i, i => PrintingPod.Refresh());
    public static Swapping PrettifyMonoMod = new(i => i, i => PrintingPod.Refresh());

    private static void Engine_Update(On.Monocle.Engine.orig_Update orig, Monocle.Engine self, Microsoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);
        DebuggerAttached.Value = Debugger.IsAttached;
    }
}