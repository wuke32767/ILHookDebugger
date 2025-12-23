using Celeste.Mod.ILHookDebugger.MappingUtils;
using Celeste.Mod.ImGuiHelper;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;
using ImGuiColorTextEditNet;
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
using System.Runtime.CompilerServices;
using YamlDotNet.Core.Tokens;
using YamlDotNet.Serialization;

[assembly: SuppressMessage("Usage", "CL0005")]

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
    CanNotInlineDelegate = 1 << 6,
    NotRun = 1 << 7,
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
        MappingUtilsTabs.Load();
        DoMappingUtils();
        //AutoRefresh.Value = Settings?.AutoRefresh ?? false;
        //HookMonoModInternal.Value = Settings?.HookMonoModInternal ?? false;
        //UnloadWhenDetached.Value = Settings?.UnloadWhenDetached ?? false;
        //MappingUtilsIntegration.Value = Settings?.MappingUtilsIntegration ?? false;

        // TODO: apply any hooks that should always be active
    }

    internal void DoMappingUtils()
    {
        if (MappingUtilsTabs.IsImported && !doneMappingUtils && Settings.MappingUtilsIntegration2)
        {
            MappingUtilsTabs.RegisterTab("ILHookDebug", "ILHookDebug", MiGui.Instance.RenderCore, () => true, null, null);
            doneMappingUtils = true;
        }
    }

    bool doneMappingUtils = false;


    private void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
        PrintingPod.Clear();
        GC.Collect();
        PrintingPod.Guardian.Clear();
    }

    static Hook? issue;
    internal static void Unfix()
    {
        issue?.Dispose();
        issue = null;
    }
    internal static void TryFix()
    {
        if (MethodWithIssue is not null)
        {
            Try(() => issue ??= new(MethodWithIssue!, (Func<ICSharpCode.Decompiler.IL.Transforms.DelegateConstruction,
                ILInstruction, IMethod, ILInstruction, IType, ILFunction?> orig,
                ICSharpCode.Decompiler.IL.Transforms.DelegateConstruction self,
                ILInstruction value, IMethod targetMethod,
                ILInstruction target, IType delegateType) =>
            {
                try
                {
                    return orig(self, value, targetMethod, target, delegateType);
                }
                catch (BadImageFormatException)
                {
                }
                return null;
            }));
        }
    }
    static MethodBase? MethodWithIssue = null;
    internal static IPalette PaletteForConsole() => Settings.ColorfulConsole ? new NewConsole() : new OldConsole();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Nothing<T>(T _)
    {
    }

    internal static Lazy<(bool, string)> CheckDecompiler = new(() =>
    {
        var r = TestWith(() => Nothing(typeof(ICSharpCode.Decompiler.CSharp.CSharpDecompiler)), "ILHookDebuggerExtension_Decompiler", "ICSharpCode.Decompiler.dll");
        if (r.Item1)
        {
            static void Extract()
            {
                MethodWithIssue = typeof(ICSharpCode.Decompiler.IL.Transforms.DelegateConstruction).GetMethod("TransformDelegateConstruction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            }
            try
            {
                Extract();
            }
            catch
            {
            }
            if (MethodWithIssue is not null)
            {
                try
                {
                    MonoMod.Core.Platforms.PlatformTriple.Current.TryDisableInlining(MethodWithIssue);
                }
                catch
                {
                }
                if (Settings.DecompilerHackFix1)
                {
                    TryFix();
                }
            }
        }
        return r;
    });

    static bool Try(Action _loaded)
    {
        try
        {
            _loaded();
            return true;
        }
        catch
        {
            return false;
        }
    }
    static (bool, string) TestWith(Action _loaded, string ext, string at)
    {
        if (Try(_loaded))
        {
            return (true, "Unknown");
        }

        if (Everest.Loader.TryGetDependency(new() { Name = "MappingUtils", Version = new(1, 0, 0) }, out var result))
        {
            Assembly? func(System.Runtime.Loader.AssemblyLoadContext _, AssemblyName name) => result.Metadata.AssemblyContext.LoadFromAssemblyName(name);
            Instance.Metadata.AssemblyContext.Resolving += func;
            try
            {
                if (Try(_loaded))
                {
                    return (true, "MappingUtils");
                }
            }
            finally
            {
                Instance.Metadata.AssemblyContext.Resolving -= func;
            }
        }
        if (Everest.Loader.TryGetDependency(new() { Name = ext, Version = new(0, 0, 0) }, out var result2))
        {
            using var stream = Everest.Content.Get($"{ext}:/{at}").Stream;
            Instance.Metadata.AssemblyContext.LoadFromStream(stream);
            return (true, "Extension");
        }
        return (false, "");
    }
    internal static Lazy<(bool, string)> CheckEditor = new(() =>
    {
        return TestWith(() => Nothing(typeof(TextEditor)), "ILHookDebuggerExtension_TextEditor", "ImGuiColorTextEditNet.dll");
    });

    public override void Unload()
    {
        DetourManager.ILHookApplied -= OnHookApply;

        Engine.Scene.OnEndOfFrame += () =>
        {
            ImGuiManager.Handlers.Remove(MiGui.Instance);
            MiGui.Instance.Display = false;
        };
        PrintingPod.Clear();
        IgnoreDebugger();
        AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
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

    public static IDEFeatures CurrentFeature;
    internal static OnChanged<Compatibility> IDE = new(_ => { }, o =>
    {
        CurrentFeature = o switch
        {
            Compatibility.VisualStudio =>
                IDEFeatures.NormalizeName |
                IDEFeatures.CanNotInlineDelegate |
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
    internal static readonly IDEFeatures ILSpyFeature = 
        IDEFeatures.CanNotInlineDelegate |
        IDEFeatures.NotRun;
    // mappingutils can be not loaded
    static object? toremove;
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