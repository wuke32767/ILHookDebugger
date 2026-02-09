using Celeste.Mod.ILHookDebugger.MappingUtils;
using Celeste.Mod.UI;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.IL;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    internal static class RetroLEDPrintingPod
    {
        public static Action List(HttpListenerContext http) => () =>
        {
            JsonSerializer.Serialize(http.Response.OutputStream, MiGui.GetEverHookedMethods().Select(x => x.GetMethodNameForDB()));
        };
        public static Action Version(HttpListenerContext http) => () =>
        {
            JsonSerializer.Serialize(http.Response.OutputStream,
                new
                {
                    info = ILHookDebuggerModule.Settings.ServerInfo,
                    time = ILHookDebuggerModule.servertime,
                    mods = Everest.Modules.Select(x => new { name = x.Metadata.Name, version = x.Metadata.Version }),
                });
        };
        public class Reqwest
        {
            public string? name { get; set; }
            public int? hint { get; set; }
            public HashSet<string> disabled { get; set; } = [];
        }
        public static Action Get(HttpListenerContext http) => () =>
        {
            //var q = http.Request.QueryString;
            Reqwest? q = null;
            try
            {
                q = JsonSerializer.Deserialize<Reqwest>(http.Request.InputStream);
            }
            catch { }
            q ??= new();
            var n = q.name;
            if (q.hint is { } hint
                && MiGui.GetEverHookedMethods().ElementAtOrDefault(hint) is { } method
                && method.GetMethodNameForDB() == n)
            {
            }
            else if (MiGui.GetEverHookedMethods().FirstOrDefault(x => x.GetMethodNameForDB() == n) is { } method2)
            {
                method = method2;
            }
            else
            {
                http.Response.StatusCode = 400;
                http.Response.OutputStream.Write(Encoding.UTF8.GetBytes("""{"message":"No match method"}"""));
                return;
            }

            using DynamicMethodDefinition dmd = new(method);

            var hooked = DetourManager.GetDetourInfo(method).ILHooks;

            Dictionary<string, object> serialize = [];
            List<string> ourmessage = [];
            if (Engine.Scene is null or OverworldLoader or AutoModUpdater or GameLoader)
            {
                ourmessage.Add("It seems like the game is still loading. Results may not be accurate.");
            }
            serialize["message"] = ourmessage;
            try
            {
                var d = q.disabled;
                var ils = hooked.Select(x =>
                {
                    var n = x.ManipulatorMethod.GetMethodNameForDB();
                    return new { name = n, disabled = d.Contains(n), raw = x, };
                }).ToArray();
                serialize["ils"] = ils.Select(x => new { x.name, x.disabled });
                serialize["ons"] = DetourManager.GetDetourInfo(method).Detours.Select(x => x.Entry.GetMethodNameForDB()).ToArray();
                if (ils.Length > 0)
                {
                    using var il = new ILContext(dmd.Definition);
                    ControlPanel.Cecils(il);
                    var dif = new Diff(il);
                    foreach (var hook in ils.Where(x => !x.disabled).Select(x => x.raw))
                    {
                        var manip = DynamicData.For(DynamicData.For(hook).Get("hook")!).Get<ILContext.Manipulator>("Manip")!;
                        if (manip.Method.DeclaringType?.Assembly != typeof(Decompilation).Assembly)
                        {
                            manip(il);
                            ControlPanel.Cecils(il);
                            dif.Update(il, manip.Method);
                        }
                    }
                    if (dif.exception is { } ex2)
                    {
                        ourmessage.Add("failed to diff method. this *may* indicates that one of our ilhooks is too fancy.");
                        ourmessage.Add(dif.when.GetMethodNameForDB());
                        ourmessage.Add(ex2.ToString());
                    }
                    else
                    {
                        dif.Final(il);
                        serialize["diff"] = dif.GetResult();
                    }
                    if (ILHookDebuggerModule.CheckDecompiler.Value.Item1)
                    {
                        try
                        {
                            ControlPanel.Cecils(il);
                            PrintingPod.Process(method, il, ILHookDebuggerModule.ILSpyFeature);
                            var notbad = Extract(dmd);
                            serialize["decompile"] = notbad;
                        }
                        catch (Exception ex)
                        {
                            ourmessage.Add("failed to decompile.");
                            ourmessage.Add(ex.ToString());
                        }
                        static List<List<Token<string?>>> Extract(DynamicMethodDefinition dmd)
                        {
                            var type = dmd.Definition.DeclaringType.Name;
                            MemoryStream ms = new();
                            dmd.Module.Write(ms);
                            ms.Position = 0;
                            var (content, decomp) = Decompilation.Final(ms, type);

                            CssColor color = new();
                            using TokenBasedTextWriter<string?> o = new() { Palette = color, };
                            var w = new MyTokenWriter(o, decomp.TypeSystem, color);
                            content.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));
                            return o.colors;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ourmessage.Add("something went wrong. nobody knows why.");
                ourmessage.Add(ex.ToString());
            }
            JsonSerializer.Serialize(http.Response.OutputStream, serialize);
        };
    }
}
