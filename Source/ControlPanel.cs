using Celeste.Mod.ILHookDebugger.MappingUtils;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ImGuiNET;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    internal class ControlPanel : IExtraWindow
    {
        public string Title { get; }

        static ConditionalWeakTable<MethodBase, ILHook> disablev2 = [];
        static ConditionalWeakTable<MethodBase, MonoMod.Core.ICoreDetour> disableonv2 = [];
        HashSet<MethodBase> filter = [];
        private readonly MethodBase method;

        public ControlPanel(string v, MethodBase me)
        {
            Title = v;
            method = me;
            Update();
        }
        bool success = false;
        List<Diff.Annotated> Current = null!;
        List<(Vector4 a, Vector4 b, string? ua, string? ub)> CurrentColor = null!;
        List<(object?, string?)> CurrentRef = null!;
        Exception ex = null!;
        string by = null!;
        static ConditionalWeakTable<MethodBase, DetourInfo> keeptrackedon = [];
        static ConditionalWeakTable<MethodBase, ILHookInfo> keeptrackedil = [];
        internal static MethodInfo getvalue = typeof(DynamicReferenceManager).GetMethod("GetValueTUnsafe", BindingFlags.NonPublic | BindingFlags.Static)!;
        internal void Update()
        {
            using DynamicMethodDefinition dmd = new(method);

            var hooked = DetourManager.GetDetourInfo(method).ILHooks;

            DetourManager.GetDetourInfo(method).Detours.Select(x => x.Entry.GetMethodNameForDB()).ToArray();
            using var il = new ILContext(dmd.Definition);
            Cecils(il);
            var dif = new Diff(il);
            var hooks = hooked
                .Select(hook => hook.hook.Manip)
                .Where(x => !filter.Contains(x.Method)).ToArray();
            foreach (var hook in hooks)
            {
                if (hook.Method.DeclaringType?.Assembly != typeof(Decompilation).Assembly)
                {
                    hook(il);
                    Cecils(il);
                    dif.Update(il, hook.Method);
                }
            }
            if (dif.exception is { } _ex)
            {
                ex = _ex;
                by = dif.when.GetMethodNameForDB();
                success = false;
            }
            else
            {
                dif.Final(il);
                success = true;
                Current = dif.instructions;
                CurrentColor = [];
                CurrentColor.EnsureCapacity(Current.Count);
                var color = hooks.Zip(Alloc(hooks.Length)).ToDictionary(x => (MethodBase)x.First.Method, x => (color: x.Second, name: x.First.Method.GetMethodNameForDB()));
                foreach (var i in Current)
                {
                    var ka = i.Anon.by is { } a && color.TryGetValue(a, out var k) ? k : default;
                    var kb = i.Anon.And is { } b && color.TryGetValue(b, out var k2) ? k2 : default;
                    CurrentColor.Add((ka.color, kb.color, ka.name, kb.name));
                }
                CurrentRef = [default, default];
                for (int i = 0; i < Current.Count - 2; i++)
                {
                    var a = Current[i];
                    var b = Current[i + 1];
                    var c = Current[i + 2];
                    if (a.Instr.MatchLdcI4(out var ax) && b.Instr.MatchLdcI4(out var bx) && c.Instr.MatchCall(out var cm) &&
                        cm is GenericInstanceMethod gcm && gcm.ElementMethod.Is(getvalue))
                    {
                        var cur = getvalue.MakeGenericMethod(gcm.GenericArguments[0].ResolveReflection()).Invoke(null, [ax, bx]);
                        string r;
                        if (cur is Delegate d)
                        {
                            r = "    |> Invoke: " + d.Method.GetMethodNameForDB();
                        }
                        else
                        {
                            r = "    |> Reference: " + (cur?.ToString() ?? "null");
                        }
                        CurrentRef.Add((cur, r));
                    }
                    else
                    {
                        CurrentRef.Add(default);
                    }
                }

            }
        }
        List<(string name, MethodBase entry)> cache = [];
        List<(string name, MethodBase entry)> cache2 = [];
        List<Action> Delayed = [];
        public unsafe void Render()
        {

            if (Dialog.Languages?.TryGetValue("english", out var lang) != true)
            {
                ImGui.Text("Waiting for Everest loading");
                return;
            }
            bool shouldUpdate = false;
            if (ImGui.BeginTable("Hooks", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
                var hooked = DetourManager.GetDetourInfo(method);
                foreach (var (hook, i) in hooked.Detours.Concat(keeptrackedon.Select(x => x.Value)).Select((s, i) => (s, i)))
                {
                    ImGui.TableNextColumn();
                    while (cache.Count <= i)
                    {
                        cache.Add(default!);
                    }
                    MethodBase entry = hook.Entry;
                    if (cache[i].entry != entry)
                    {
                        cache[i] = (entry.GetMethodNameForDB(), entry);
                        shouldUpdate = true;
                    }
                    ImGui.Text("On");
                    ImGui.TableNextColumn();
                    ImGui.Text(cache[i].name);
                    MakeDecompile(entry, lang, cache[i].name);
                    ImGui.TableNextColumn();
                    bool state = hook.IsApplied;
                    if (ImGui.Checkbox("Enable##on" + i.ToString(), ref state))
                    {
                        if (state)
                        {
                            Delayed.Add(() =>
                            {
                                hook.Apply();
                                keeptrackedon.Remove(entry);
                            });
                        }
                        else
                        {
                            Delayed.Add(() =>
                            {
                                hook.Undo();
                                keeptrackedon.Add(entry, hook);
                            });
                        }
                    }
                    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Tooltips_Enable", lang));
                    var f = disableonv2.TryGetValue(entry, out var h);
                    state = !f;
                    ImGui.SameLine();
                    if (ImGui.Checkbox("EnableV2##on" + i.ToString(), ref state))
                    {
                        if (state != f)
                        {
                            throw new Exception("How?");
                        }
                        if (f)
                        {
                            h!.Dispose();
                            disableonv2.Remove(entry);
                        }
                        else
                        {
                            disableonv2.Add(entry, MonoMod.Core.DetourFactory.Current.CreateDetour(new(hook.detour.InvokeTarget, hook.detour.NextTrampoline.TrampolineMethod)));
                        }
                    }
                    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Tooltips_EnableV2", lang));
                }
                var d = Alloc(hooked.ILHooks.Count() - filter.Count).GetEnumerator();
                foreach (var (hook, i) in
                    hooked.ILHooks.Concat(keeptrackedil.Select(x => x.Value))
                    .Select((s, i) => (s, i)))
                {
                    ImGui.TableNextColumn();
                    while (cache2.Count <= i)
                    {
                        cache2.Add(default!);
                    }
                    MethodBase entry = hook.ManipulatorMethod;
                    bool filtered = filter.Contains(entry);
                    if (cache2[i].entry != entry)
                    {
                        cache2[i] = (entry.GetMethodNameForDB(), entry);
                        shouldUpdate = true;
                    }
                    ImGui.Text("IL");
                    ImGui.TableNextColumn();
                    var col = new Vector4(0.6f, 0.6f, 0.6f, 1);
                    if (!filtered && hook.IsApplied)
                    {
                        d.MoveNext();
                        col = d.Current;
                    }
                    ImGui.TextColored(col, cache2[i].name);
                    MakeDecompile(entry, lang, cache2[i].name);
                    ImGui.TableNextColumn();
                    bool state = hook.IsApplied;
                    if (ImGui.Checkbox("Enable##il" + i.ToString(), ref state))
                    {
                        shouldUpdate = true;
                        if (state)
                        {
                            Delayed.Add(() =>
                            {
                                hook.Apply();
                                keeptrackedil.Remove(entry);
                            });
                        }
                        else
                        {
                            Delayed.Add(() =>
                            {
                                hook.Undo();
                                keeptrackedil.Add(entry, hook);
                            });
                        }
                    }
                    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Tooltips_Enable", lang));
                    var f = disablev2.TryGetValue(entry, out var h);
                    state = !f;
                    ImGui.SameLine();
                    if (ImGui.Checkbox("EnableV2##il" + i.ToString(), ref state))
                    {
                        shouldUpdate = true;
                        if (state != f)
                        {
                            throw new Exception("How?");
                        }
                        if (f)
                        {
                            h!.Dispose();
                            disablev2.Remove(entry);
                        }
                        else
                        {
                            disablev2.Add(entry, new(entry, il =>
                            {
                                il.Instrs.Clear();
                                il.Method.Body.ExceptionHandlers.Clear();
                                ILCursor ic = new(il);
                                ic.EmitRet();
                            }));
                        }
                    }
                    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Tooltips_EnableV2", lang));
                    ImGui.SameLine();
                    state = filtered;
                    if (ImGui.Checkbox("Hide##" + i.ToString(), ref state))
                    {
                        shouldUpdate = true;
                        if (state)
                        {
                            filter.Add(entry);
                        }
                        else
                        {
                            filter.Remove(entry);
                        }
                    }
                    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Tooltips_Hide", lang));
                }
                ImGui.EndTable();
            }
            ImGui.Separator();
            if (success)
            {
                if (ImGui.BeginTable("Then", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg))
                {
                    var c = *ImGui.GetStyleColorVec4(ImGuiCol.Text);
                    foreach (var (i, (ca, cb, na, nb), (obj, r)) in Current.Zip(CurrentColor, CurrentRef))
                    {
                        ImGui.TableNextColumn();
                        var (an, cx) = (i.Anon.stat & Diff.Status.Crossed) switch
                        {
                            Diff.Status.Added => (" + ", new Vector4(0, 0.8f, 0, 1)),
                            Diff.Status.Removed => (" + ", new(0.8f, 0, 0, 1)),
                            Diff.Status.Crossed when i.Anon.stat.HasFlag(Diff.Status.NotInArray) => (" - ", new(0.8f, 0.8f, 0, 1)),
                            Diff.Status.Crossed => (" + ", new(0.8f, 0.8f, 0, 1)),
                            _ => ("   ", c),
                        };
                        ImGui.TextColored(cx, an + i.Instr.ToString());
                        ImGui.TableNextColumn();
                        if (i.Anon.by is { } a)
                        {
                            ImGui.TextColored(ca, na);
                            MakeDecompile(a, lang, na);
                            if (i.Anon.And is { } b)
                            {
                                ImGui.TextColored(cb, nb);
                                MakeDecompile(b, lang, nb);
                            }
                        }
                        if (r is { })
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextColored(new(0.7f, 0.7f, 0.7f, 0.7f), r);
                            if (obj is Delegate d)
                            {
                                MakeDecompile(d.Method, lang, r);
                            }
                            ImGui.TableNextColumn();
                        }
                    }
                    ImGui.EndTable();
                }
            }
            else
            {
                ImGui.Text("failed to diff. this *may* indicate that one of our ilhook is too fancy.");
                ImGui.Text(by);
                ImGui.Text(ex.ToString());
            }
            foreach (var item in Delayed)
            {
                item();
            }
            Delayed.Clear();
            if (shouldUpdate)
            {
                Update();
            }
        }
        internal void MakeDecompile(MethodBase src, Language? language, string? cache = null)
        {
            cache ??= src.GetMethodNameForDB();
            var (d, f) = ILHookDebuggerModule.CheckDecompiler.Value;
            if (d)
            {
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Tooltips_Decompile", language));
                if (ImGui.IsItemClicked())
                {
                    Logger.Log(nameof(ILHookDebugger), $"Decompiling with a decompiler from {f}...");
                    var (ast, decomp) = Decompilation.NonHook(src);
                    if (ILHookDebuggerModule.Settings.OpenInEditor)
                    {
                        var inst = MiGui.Instance;
                        inst.decompiled.Add(MiGui.CreateEditor(ast, decomp, inst.GetNextWindowName(cache)));
                    }
                    else
                    {
                        var w = new MyTokenWriter(Console.Out, decomp.TypeSystem, ILHookDebuggerModule.PaletteForConsole());
                        ast.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));
                    }

                }
            }
        }
        internal static void Cecils(ILContext il)
        {
            foreach (var instr in il.Instrs)
            {
                if (instr.Operand is Instruction target)
                    instr.Operand = il.DefineLabel(target);
                else if (instr.Operand is Instruction[] targets)
                    instr.Operand = targets.Select(il.DefineLabel).ToArray();
            }
        }
        internal static IEnumerable<Vector4> Alloc(int c)
        {
            //double step = (Math.Sqrt(5) - 1) * 3;
            double step = 5.5 / c;
            double h = 0;
            for (int i = 0; i < c; i++)
            {
                var x = h % 1;
                static Vector4 map(double k) => k switch
                {
                    0 => new(1, 0, 0, 1),
                    1 => new(1, 1, 0, 1),
                    2 => new(0, 1, 0, 1),
                    3 => new(0, 1, 1, 1),
                    4 => new(0.5f, 0.5f, 1, 1), // blue is bad
                    5 => new(1, 0, 1, 1),
                    _ => new(1, 0, 0, 1),
                };
                Vector4 ret = Vector4.Lerp(map(Math.Floor(h)), map(Math.Ceiling(h)), (float)x);
                ret *= 0.7f;
                ret += new Vector4(0.2f);
                ret.W = 1;
                yield return ret;
                h += step;
                if (h >= 6)
                {
                    h -= 6;
                }
            }
            while (true)
            {
                yield return new(1, 1, 1, 1);
            }
        }
    }
}
