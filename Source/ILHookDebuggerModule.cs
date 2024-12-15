using Celeste.Mod.ILHookDebugger.MappingUtils;
using Celeste.Mod.MappingUtils.ImGuiHandlers;
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

        //AutoRefresh.Value = Settings?.AutoRefresh ?? false;
        //HookMonoModInternal.Value = Settings?.HookMonoModInternal ?? false;
        //UnloadWhenDetached.Value = Settings?.UnloadWhenDetached ?? false;
        //MappingUtilsIntegration.Value = Settings?.MappingUtilsIntegration ?? false;

        // TODO: apply any hooks that should always be active
    }

    public override void Unload()
    {
        PrintingPod.Clear();
        Retreat();
        IgnoreDebugger();
        UnIntegrate();
        MakeStatic();
        // TODO: unapply any hooks applied in Load()
    }

    public static Swapping AutoRefresh = new(() =>
    {

    }, MakeStatic);
    static void MakeStatic()
    {

    }
    static ILHook? MonoModCriminal;
    public static Swapping HookMonoModInternal = new(() =>
    {
        PrintingPod.Clear();

        Logger.Error(nameof(ILHookDebugger), "Hooking MonoMod Internal as requested.");
        try
        {
            var bf = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            var mds = typeof(DetourManager).GetNestedType("ManagedDetourState", bf)!;
            var def = mds.GetField("Source", bf)!;
            var t = mds.GetMethod("UpdateEndOfChain", bf);
            if (def is null)
            {
                throw new NullReferenceException();
            }
            DynamicMethod sourcehelper = new("noname", typeof(MethodBase), [typeof(object)]);
            var il = sourcehelper.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, mds);
            il.Emit(OpCodes.Ldfld, def);
            il.Emit(OpCodes.Ret);
            var defget = sourcehelper.CreateDelegate<Func<object, MethodBase>>();

            MonoModCriminal = new(t!, il =>
            {
                ILCursor ic = new(il);
                ic.GotoNext(MoveType.Before, i => i.MatchCallOrCallvirt<DynamicMethodDefinition>("Generate"));
                ic.EmitDup();
                ic.EmitLdarg0();
                void MyHook(DynamicMethodDefinition dmd, object MissingType)
                {
                    try
                    {
                        if (defget(MissingType) is MethodBase Source && PrintingPod.DuplicantLookup.TryGetValue(Source, out var dup))
                        {
                            new ILContext(dmd.Definition).Invoke(dup.Detour.Manipulator);
                        }
                    }
                    catch (Exception e)
                    {
                        ExceptionHandler(e);
                    }
                }
#pragma warning disable CL0002
                ic.EmitDelegate(MyHook);
#pragma warning restore CL0002
            });
        }
        catch (Exception e)
        {
            ExceptionHandler(e);
            return false;
        }
        return true;

        [DoesNotReturn]
        static void ExceptionHandler(Exception e)
        {
            MonoModCriminal?.Dispose();
            MonoModCriminal = null;
            Logger.Error(nameof(ILHookDebugger), "Failed! Reset Settings.");
            Settings.HookMonoModInternal = false;
            Instance?.SaveSettings();
            throw new InvalidOperationException($"""
            {nameof(ILHookDebugger)} was failed when hooking MonoMod internal. Settings was reset.
            """, e);
        }
    }, Retreat);
    static bool Retreat()
    {
        MonoModCriminal?.Dispose();
        MonoModCriminal = null;
        PrintingPod.Clear();
        return false;

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
                if (Everest.Loader.VersionSatisfiesDependency(result.Metadata.Version, new Version(1, 8, 1)))
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

    private static void Engine_Update(On.Monocle.Engine.orig_Update orig, Monocle.Engine self, Microsoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);
        DebuggerAttached.Value = Debugger.IsAttached;
    }
}