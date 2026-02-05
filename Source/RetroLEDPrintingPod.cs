using Celeste.Mod.ILHookDebugger.MappingUtils;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.IL;
using Mono.Cecil.Cil;
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
        public static Action Get(HttpListenerContext http) => () =>
        {
            //var q = http.Request.QueryString;
            Dictionary<string, string> q = [];
            try
            {
                q = JsonSerializer.Deserialize<Dictionary<string, string>>(http.Request.InputStream) ?? q;
            }
            catch { }
            var n = q.GetValueOrDefault("name");
            if (int.TryParse(q.GetValueOrDefault("hint"), out var hint)
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
            serialize["message"] = ourmessage;
            try
            {
                var ils = hooked.Select(x => x.ManipulatorMethod.GetMethodNameForDB()).ToArray();
                serialize["ils"] = ils;
                serialize["ons"] = DetourManager.GetDetourInfo(method).Detours.Select(x => x.Entry.GetMethodNameForDB()).ToArray();
                if (ils.Length > 0)
                {
                    using var il = new ILContext(dmd.Definition);

                    Cecils(il);
                    var dif = new Diff(il);
                    bool diffgood = true;
                    static void Cecils(ILContext il)
                    {
                        foreach (var instr in il.Instrs)
                        {
                            if (instr.Operand is Instruction target)
                                instr.Operand = il.DefineLabel(target);
                            else if (instr.Operand is Instruction[] targets)
                                instr.Operand = targets.Select(t => il.DefineLabel(t)).ToArray();
                        }
                    }
                    foreach (var hook in hooked)
                    {
                        var manip = DynamicData.For(DynamicData.For(hook).Get("hook")!).Get<ILContext.Manipulator>("Manip")!;
                        if (manip.Method.DeclaringType?.Assembly != typeof(Decompilation).Assembly)
                        {
                            manip(il);
                            Cecils(il);
                            try
                            {
                                if (diffgood)
                                {
                                    dif.Update(il, manip.Method.GetMethodNameForDB());
                                }
                            }
                            catch (Exception ex)
                            {
                                diffgood = false;
                                ourmessage.Add("failed to diff method. this *may* indicates that one of our ilhooks is too fancy.");
                                ourmessage.Add(manip.Method.GetMethodNameForDB());
                                ourmessage.Add(ex.ToString());
                            }
                        }
                    }
                    if (diffgood)
                    {
                        dif.Final(il);
                        serialize["diff"] = dif.GetResult();
                    }
                    if (ILHookDebuggerModule.CheckDecompiler.Value.Item1)
                    {
                        try
                        {
                            Cecils(il);
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
