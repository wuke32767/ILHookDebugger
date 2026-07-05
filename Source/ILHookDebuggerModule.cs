using Celeste.Mod.ILHookDebugger.MappingUtils;
using Celeste.Mod.ImGuiHelper;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;
using ImGuiColorTextEditNet;
using Monocle;
using MonoMod;
using MonoMod.Cil;
using MonoMod.ModInterop;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;
using YamlDotNet.Serialization;

[assembly: SuppressMessage("Usage", "CL0005")]

namespace Celeste.Mod.ILHookDebugger;

public enum Compatibility
{
    None = 0, VisualStudio, Rider, dnSpy, Celeste,
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
    DamnTypeResolveCache = 1 << 8,
    UseCeleste = 1 << 9,
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
        typeof(Public).ModInterop();
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

    static HttpListener? http;
    static CancellationTokenSource? httpcancel;
    internal static void CtrlC()
    {
        httpcancel?.Cancel();
        httpcancel?.Dispose();
        httpcancel = null;
        http?.Close();
        http = null;
    }
    internal static DateTime servertime = DateTime.UtcNow;
    internal static void Service()
    {
        servertime = DateTime.UtcNow;
        http ??= new();
        httpcancel ??= new();
        try
        {
            http.Prefixes.Add($"http://localhost:{Settings.Port}/");
            http.Start();
            var token = httpcancel.Token;
            Task.Run(async () =>
            {
                while (http is { } h)
                {
                    var cur = await h.GetContextAsync().WaitAsync(token);
                    _ = Task.Run(cur.Request.Url?.Segments.ElementAtOrDefault(1) switch
                    {
                        "list" => RetroLEDPrintingPod.List(cur),
                        "method" => RetroLEDPrintingPod.Get(cur),
                        "version" => RetroLEDPrintingPod.Version(cur),
                        "" or null or "index.html" or "index.htm" => () =>
                        {
                            cur.Response.ContentType = "text/html";
                            using var s = Everest.Content.Get("ILHookDebugger:/index.html").Stream;
                            s.CopyTo(cur.Response.OutputStream);
                        }
                        ,
                        _ => () =>
                        {
                            cur.Response.StatusCode = 404;
                        }
                    } + (() =>
                    {
                        cur.Response.Close();
                    }));
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error(nameof(ILHookDebugger), "can't run as server.");
            Logger.Error(nameof(ILHookDebugger), ex.ToString());
        }
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
        var r = TestWith(() => typeof(ICSharpCode.Decompiler.CSharp.CSharpDecompiler), "ILHookDebuggerExtension_Decompiler");
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
    static (bool, string) TestWith(Func<Type> _loaded, string ext)
    {
        if (Try(() => _loaded()))
        {
            var t = (AssemblyLoadContext.GetLoadContext(_loaded().Assembly) as EverestModuleAssemblyContext)?.Name ?? "Unknown";
            return (true, t == ext ? "Extension" : t);
        }

        return (false, "");
    }

    internal static Lazy<(bool, string)> CheckRunner = new(() =>
    {
        return TestWith(() => typeof(WritingMango.ILRunner.RunnerFactory), "ILHookDebuggerExtension_ILRunner");
    });

    internal static Lazy<(bool, string)> CheckScript = new(() =>
    {
        return TestWith(() => typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation), "ILHookDebuggerExtension_CSScript");
    });

    internal static Lazy<(bool, string)> CheckEditor = new(() =>
    {
        return TestWith(() => typeof(TextEditor), "ILHookDebuggerExtension_TextEditor");
    });

    public override void Unload()
    {
        CtrlC();
        DetourManager.ILHookApplied -= OnHookApply;

        MainThreadHelper.Schedule(() =>
        {
            ImGuiManager.Handlers.Remove(MiGui.Instance);
            MiGui.Instance.Display = false;
        }).AsTask();
        PrintingPod.Clear();
        IgnoreDebugger();
        AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
        GC.Collect();
        PrintingPod.Guardian.Clear();
        Unfix();
        Mirror.Unload();
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
                    PrintingPod.FastTrack(info.Method.Method);
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
                IDEFeatures.DamnTypeResolveCache |
                IDEFeatures.NormalizeName |
                IDEFeatures.RequiresFileAssembly |
                IDEFeatures.CanDebuggerLaunchButNotDefault,
            Compatibility.dnSpy =>
                IDEFeatures.CanNotModifyValues,
            Compatibility.Celeste =>
                IDEFeatures.UseCeleste |
                ILSpyFeature,
            _ =>
                IDEFeatures.None,
        };
        PrepareLoop.Value = o is Compatibility.Celeste;
        PrintingPod.Refresh();
    });
    internal static IDEFeatures ILSpyFeature =>
        (Settings.DecompilerHackFix1 ? IDEFeatures.None : IDEFeatures.CanNotInlineDelegate) |
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
    public static Swapping IEnumeratorPatch = new(i => i, i => PrintingPod.Refresh());
    public static Swapping TextConvertor = new(i => i, i => PrintingPod.Refresh());
    public static Swapping PrettifyMonoMod = new(i => i, i => PrintingPod.Refresh());
    public static Swapping PrepareLoop = new(Mirror.Load, Mirror.Unload);

    private static void Engine_Update(On.Monocle.Engine.orig_Update orig, Monocle.Engine self, Microsoft.Xna.Framework.GameTime gameTime)
    {
        orig(self, gameTime);
        DebuggerAttached.Value = Debugger.IsAttached;
    }
}