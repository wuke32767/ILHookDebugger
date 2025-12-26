using Celeste.Mod.ImGuiHelper;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.IL;
using ImGuiColorTextEditNet;
using ImGuiNET;
using Microsoft.Xna.Framework;
using ModInteropImportGenerator;
using Monocle;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger.MappingUtils
{
    file class not_not
    {
        internal static void Launch()
        {
            Task.Run(Debugger.Launch);
        }
    }

    [GenerateImports("MappingUtils.Tabs")]
    public static partial class MappingUtilsTabs
    {
        public static partial void RegisterTab(string modName, string tabName, Action renderImGui, Func<bool> canBeVisible,
            Action? onOpen, Action? onClose);
    }

    public class MiGui : ImGuiHandler
    {
        public static MiGui Instance = new();

        readonly List<Duplicant> toremove = [];
        string? exception = null;

        private static readonly FieldInfo DetourManager_detourStates =
            typeof(DetourManager).GetField("detourStates", BindingFlags.Static | BindingFlags.NonPublic)!;
        public static IEnumerable<MethodBase> GetEverHookedMethods()
        {
            var detourStates = (IDictionary)DetourManager_detourStates.GetValue(null)!;
            return detourStates.Keys.OfType<MethodBase>()/*.Where(x =>
            {
                var info = DetourManager.GetDetourInfo(x);
                return info.Detours.Any() || info.ILHooks.Any();
            })*/;
        }

        bool helpinghand = false;
        byte[] searchText = new byte[512];
        byte[] dumpPath = new byte[512];
        object dumpobj => dumpPath;
        readonly List<(string name, MethodBase method)> searchResult = [];
        Stopwatch watch = new();
        object? watching;
        void See(object ins)
        {
            watching = ins;
            watch.Reset();
            watch.Start();
        }
        bool Look(object ins)
        {
            if (ins == watching && watch.ElapsedMilliseconds < 1000)
            {
                return true;
            }
            return false;
        }
        List<(string name, object content, int id)> decompiled = [];
        static object CreateEditor(SyntaxTree content, ICSharpCode.Decompiler.CSharp.CSharpDecompiler decomp)
        {
            if (ILHookDebuggerModule.Settings.UseTextEditor && ILHookDebuggerModule.CheckEditor.Value.Item1)
            {
                static object Extract(SyntaxTree content, ICSharpCode.Decompiler.CSharp.CSharpDecompiler decomp)
                {
                    ExternalCustomEditorColor color = new();
                    using EditorTextWriter<PaletteIndex> o = new() { Palette = color, };
                    var w = new MyTokenWriter(o, decomp.TypeSystem, color);
                    content.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));

                    var r = new TextEditor() { AllText = o.str.ToString(), SyntaxHighlighter = new SyntaxTreeHighlighter(o.colors), Options = { IsReadOnly = true } };
                    ExternalCustomEditorColor.InjectColor(r);
                    return r;
                }
                return Extract(content, decomp);
            }
            else
            {
                EditorColor color = new();
                using EditorTextWriter<Color> o = new() { Palette = color, };
                var w = new MyTokenWriter(o, decomp.TypeSystem, color);
                content.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));
                var str = o.str.ToString();

                return new MinimalDisplayer(str.Split('\n'), o.colors);
            }
        }

        public override void Render()
        {
            base.Render();

            if (!Display)
            {
                return;
            }

            ImGui.SetNextWindowPos(new(0, 0), ImGuiCond.FirstUseEver, System.Numerics.Vector2.Zero);
            ImGui.SetNextWindowSize(new(150 * 2.5f, ImGui.GetMainViewport().Size.Y), ImGuiCond.FirstUseEver);

            if (ImGui.Begin("ILHookDebugger##ILHookDebugger", ImGuiWindowFlags.NoFocusOnAppearing))
            {
                try
                {
                    RenderCore();
                }
                finally
                {
                    ImGui.End();
                }
            }
            int remove = -1;
            foreach (var ((name, content, id), index) in decompiled.Select((x, i) => (x, i)))
            {
                bool open = true;
                ImGui.SetNextWindowSize(new(150 * 2.5f, ImGui.GetMainViewport().Size.Y / 4), ImGuiCond.FirstUseEver);
                if (ImGui.Begin($"{name}##ILHookDebugger{id}", ref open, ImGuiWindowFlags.HorizontalScrollbar))
                {
                    if (ILHookDebuggerModule.CheckEditor.Value.Item1)
                    {
                        Extract(content);
                        static void Extract(object content)
                        {
                            if (content is TextEditor editor)
                            {
                                editor.Render("");
                            }
                        }
                    }
                    if (content is MinimalDisplayer displayer)
                    {
                        displayer.Render();
                    }

                    ImGui.End();
                }
                if (!open)
                {
                    remove = index;
                }
            }
            if (remove >= 0)
            {
                decompiled.RemoveAt(remove);
            }

        }
        public bool Display = false;
        public bool overwrite = false;
        public void RenderCore()
        {
            // ImGuiHelper don't support any cjk characters.
            if (exception is not null)
            {
                ImGui.Text("Encounter an error. Please report.");
                if (ImGui.Button("ok"))
                {
                    exception = null;
                    return;
                }
                ImGui.Text(exception);
                return;
            }
            try
            {
                var helpcolor = new System.Numerics.Vector4(1, 0.5f, 0, 1);
                if (!Dialog.Languages.TryGetValue("english", out var lang))
                {
                    ImGui.Text("Waiting for Everest loading");
                    return;
                }
                if (!helpinghand)
                {
                    if (ILHookDebuggerModule.Settings.ShowHelp)
                    {
                        if (ImGui.Button("Show Help"))
                        {
                            helpinghand = true;
                        }
                    }
                }
                else
                {
                    if (ImGui.Button("Hide Help"))
                    {
                        helpinghand = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Hide FOREVER"))
                    {
                        ImGui.OpenPopup("HIDER##ILHookDebugger");
                    }
                    if (ImGui.BeginPopup("HIDER##ILHookDebugger"))
                    {
                        ImGui.Text(Dialog.Clean("ILHookDebugger_Helping_Disable", lang));
                        if (ImGui.Button("HIDE"))
                        {
                            ILHookDebuggerModule.Settings.ShowHelp = false;
                            helpinghand = false;
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.EndPopup();
                    }
                }

                if (helpinghand)
                {
                    ImGui.TextColored(helpcolor, Dialog.Clean("ILHookDebugger_Helping_Sdep1", lang));
                }

                bool cur = ILHookDebuggerModule.BreakOnce;
                if (ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.CanNotModifyValues))
                {
                    cur = true;
                    ImGui.BeginDisabled();
                    ImGui.Checkbox("Break Once", ref cur);
                    ImGui.EndDisabled();
                    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_BreakOnce_Help_Disabled", lang));
                }
                else
                {
                    if (ImGui.Checkbox("Break Once", ref cur))
                    {
                        ILHookDebuggerModule.BreakOnce.Value = cur;
                    }
                    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_BreakOnce_Help", lang));
                }
                ImGui.SameLine();

                cur = ILHookDebuggerModule.PrettifyMonoMod;
                if (ImGui.Checkbox("Prettify MonoMod", ref cur))
                {
                    ILHookDebuggerModule.PrettifyMonoMod.Value = cur;
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_PrettifyMonoMod_Help", lang));
                ImGui.SameLine();

                cur = ILHookDebuggerModule.TextConvertor;
                if (ImGui.Checkbox("Another Symbol Convertor", ref cur))
                {
                    ILHookDebuggerModule.TextConvertor.Value = cur;
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_Convertor_Help", lang));
                ImGui.SameLine();

                cur = ILHookDebuggerModule.UnloadWhenDetached;
                if (ImGui.Checkbox("Unload When Detached", ref cur))
                {
                    ILHookDebuggerModule.UnloadWhenDetached.Value = cur;
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_UnloadWhenDetached_Help", lang));
                var (hasEd, src) = ILHookDebuggerModule.CheckEditor.Value;
                var (hasdecom, fromd) = ILHookDebuggerModule.CheckDecompiler.Value;

                if (hasdecom)
                {
                    cur = ILHookDebuggerModule.Settings.OpenInEditor;
                    if (ImGui.Checkbox("Use In-Game Decompile Displayer", ref cur))
                    {
                        ILHookDebuggerModule.Settings.OpenInEditor = cur;
                    }
                    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_Displayer", lang));
                    ImGui.SameLine();

                    if (cur)
                    {
                        ImGui.BeginDisabled(!hasEd);
                        cur = ILHookDebuggerModule.Settings.UseTextEditor;
                        if (ImGui.Checkbox("Use Text Editor", ref cur))
                        {
                            ILHookDebuggerModule.Settings.UseTextEditor = cur;
                        }
                        ImGui.EndDisabled();
                        if (hasEd)
                        {
                            ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_Editor", lang) + "\n" + Dialog.Clean("ILHookDebugger_Help_Editor_True", lang) + src);
                        }
                        else
                        {
                            ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_Editor", lang));
                        }
                    }
                    //ImGui.SameLine();
                    else
                    {
                        cur = ILHookDebuggerModule.Settings.ColorfulConsole;
                        if (ImGui.Checkbox("Use Colorful Console", ref cur))
                        {
                            ILHookDebuggerModule.Settings.ColorfulConsole = cur;
                        }
                        ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_ConsoleColor_Help", lang));
                    }
                    //ImGui.SameLine();

                    //cur = ILHookDebuggerModule.Settings.UseDecompileResolver;
                    //if (ImGui.Checkbox("Use Decompiler Resolver", ref cur))
                    //{
                    //    ILHookDebuggerModule.Settings.UseDecompileResolver = cur;
                    //}
                    //ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_DecompileResolver_Help", lang));
                }
                //if (hasdecom || ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.CanNotInlineDelegate))
                //{
                //    ImGui.SameLine();

                //    cur = ILHookDebuggerModule.Settings.DecompilerHackFix1;
                //    if (ImGui.Checkbox("Decompiler Hack Fix", ref cur))
                //    {
                //        ILHookDebuggerModule.Settings.DecompilerHackFix1 = cur;
                //    }
                //    ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_DecompileHackFix_Help", lang));
                //}

                if (ImGui.Button("Save Settings"))
                {
                    ILHookDebuggerModule.Instance.SaveSettings();
                }

                ImGui.SameLine();


                if (ImGui.Button("Refresh"))
                {
                    PrintingPod.Refresh();
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_RefreshIsAllYouNeed", lang));

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    && ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.CanDebuggerLaunch))
                {
                    ImGui.SameLine();
                    //https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.debugger.launch
                    //windows only
                    if (ImGui.Button("Launch IDE Debugger"))
                    {
                        not_not.Launch();
                    }
                    if (ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.CanDebuggerLaunchButNotDefault))
                    {
                        ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_LaunchNeedConfigure", lang));
                    }
                }

                ImGui.Text("");
                ImGui.Separator();
                if (helpinghand)
                {
                    ImGui.TextColored(helpcolor, Dialog.Clean("ILHookDebugger_Helping_Sdep2", lang));
                }

                if (ImGui.BeginTable("Search..", 2,
                    ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.Hideable |
                    ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.ScrollY,
                    new(0, ImGui.GetTextLineHeightWithSpacing() * Calc.Clamp(searchResult.Count + 2, 2, 15))))
                {

                    ImGui.TableSetupColumn("Search..", ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHeaderLabel);
                    if (helpinghand)
                    {
                        ImGui.TableSetupScrollFreeze(0, 2);
                    }
                    else
                    {
                        ImGui.TableSetupScrollFreeze(0, 1);
                    }
                    //ImGui.TableHeadersRow();
                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                    ImGui.TableSetColumnIndex(0);

                    unsafe
                    {
                        ImGui.InputText("Search..", searchText, 512, ImGuiInputTextFlags.CallbackEdit, i =>
                        {
                            var s = System.Text.Encoding.UTF8.GetString(i->Buf, i->BufTextLen).Trim();
                            if (!string.IsNullOrEmpty(s))
                            {
                                searchResult.Clear();
                                searchResult.AddRange(
                                    GetEverHookedMethods()
                                    .Select(x => (x.GetMethodNameForDB(), x))
                                    .Where(x => x.Item1.Contains(s, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(x => x.Item1));
                            }
                            else
                            {
                                searchResult.Clear();
                                searchResult.AddRange(GetEverHookedMethods().Select(x => (x.GetMethodNameForDB(), x)).OrderBy(x => x.Item1));
                            }

                            return 0;
                        });
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear"))
                    {
                        searchText[0] = 0;
                        searchResult.Clear();
                    }
                    ImGui.TableNextColumn();

                    if (helpinghand)
                    {
                        if (searchResult.Count > 0)
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextColored(helpcolor, Dialog.Clean("ILHookDebugger_Helping_Sdep3", lang));
                            ImGui.TableNextColumn();
                            ImGui.TextColored(helpcolor, Dialog.Clean("ILHookDebugger_Helping_Sdep33", lang));
                        }
                    }
                    foreach (var (na, me) in searchResult)
                    {
                        ImGui.TableNextColumn();
                        ImGui.Text(na);
                        ImGui.TableNextColumn();
                        if (ImGui.Button("Insert Breakpoint##" + na))
                        {
                            PrintingPod.Create(me);
                        }
                        ImGui.SetItemTooltip(na);
                        ImGui.SameLine();
                        ImGui.BeginDisabled(!hasdecom);
                        if (ImGui.Button(Look(me) ? "Done" : ("Decompile Only##" + na)))
                        {
                            if (ILHookDebuggerModule.Settings.OpenInEditor)
                            {
                                Extract(me, fromd);
                                void Extract(MethodBase target, string from)
                                {
                                    Logger.Log(nameof(ILHookDebugger), $"Decompiling with a decompiler from {from}...");
                                    var (ast, decomp) = Decompilation.FromMethod(target);
                                    decompiled.Add((na, CreateEditor(ast, decomp), decompiled.LastOrDefault().id + 1));
                                }
                            }
                            else
                            {
                                Extract(me, fromd);
                                static void Extract(MethodBase target, string from)
                                {
                                    Logger.Log(nameof(ILHookDebugger), $"Decompiling with a decompiler from {from}...");
                                    var (ast, decomp) = Decompilation.FromMethod(target);
                                    var w = new MyTokenWriter(Console.Out, decomp.TypeSystem, ILHookDebuggerModule.PaletteForConsole());
                                    ast.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));
                                }
                            }
                            See(me);
                        }
                        ImGui.EndDisabled();
                        if (hasdecom)
                        {
                            ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_Decompiler", lang) + "\n" + Dialog.Clean("ILHookDebugger_Help_Decompiler_True", lang) + fromd);
                        }
                        else
                        {
                            ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_Decompiler", lang));
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.Text("");

                var flags = PrintingPod.AllDuplicants;
                toremove.Clear();
                if (ImGui.BeginTable("Debugging", 2,
                    ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.Hideable |
                    ImGuiTableFlags.NoSavedSettings))
                {

                    ImGui.TableSetupColumn("Debugging", ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    if (flags.Count == 0)
                    {
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(150);
                        ImGui.TextDisabled("Empty...");
                    }
                    else
                    {
                        if (helpinghand)
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextColored(helpcolor, Dialog.Clean("ILHookDebugger_Helping_Sdep4", lang));
                            ImGui.TableNextColumn();
                        }
                        foreach (var f in flags)
                        {
                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(150);
                            ImGui.Text(f.Target.GetMethodNameForDB());
                            ImGui.TableNextColumn();
                            if (ImGui.Button("Remove##" + f.TypeName))
                            {
                                toremove.Add(f);
                            }
                            ImGui.SameLine();
                            ImGui.BeginDisabled(!hasdecom);
                            if (ImGui.Button(Look(f) ? "Done" : ("Decompile##" + f.TypeName)))
                            {
                                if (ILHookDebuggerModule.Settings.OpenInEditor)
                                {
                                    Extract(f, fromd);
                                    void Extract(Duplicant target, string from)
                                    {
                                        Logger.Log(nameof(ILHookDebugger), $"Decompiling with a decompiler from {from}...");
                                        var (ast, decomp) = Decompilation.FromRunning(target);
                                        decompiled.Add((target.TypeName!, CreateEditor(ast, decomp), decompiled.LastOrDefault().id + 1));
                                    }
                                }
                                else
                                {
                                    Extract(f, fromd);
                                    static void Extract(Duplicant target, string from)
                                    {
                                        Logger.Log(nameof(ILHookDebugger), $"Decompiling with a decompiler from {from}...");
                                        var (ast, decomp) = Decompilation.FromRunning(target);
                                        var w = new MyTokenWriter(Console.Out, decomp.TypeSystem, ILHookDebuggerModule.PaletteForConsole());
                                        ast.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));
                                    }
                                }
                                See(f);
                            }
                            ImGui.EndDisabled();
                            if (hasdecom)
                            {
                                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_Decompiler", lang) + "\n" + Dialog.Clean("ILHookDebugger_Help_Decompiler_True", lang) + fromd);
                            }
                            else
                            {
                                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_Decompiler", lang));
                            }
                        }
                    }
                    ImGui.EndTable();
                }
                foreach (var f in toremove)
                {
                    PrintingPod.Remove(f);
                }

                ImGui.Text("");
                ImGui.Separator();

                if (ImGui.Button(Look(dumpobj) ? "Done" : "Dump"))
                {
                    var count = dumpPath.TakeWhile(x => x != 0).Count();
                    PrintingPod.Dump(System.Text.Encoding.UTF8.GetString(dumpPath, 0, count), overwrite);
                    See(dumpobj);
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_WhatIsDump", lang));
                ImGui.SameLine();
                ImGui.Checkbox("overwrite", ref overwrite);
                ImGui.SameLine();
                unsafe
                {
                    GCHandle? handle = null;
                    ImGui.InputText("Path", dumpPath, (uint)dumpPath.Length, ImGuiInputTextFlags.CallbackResize, data =>
                    {
                        if (data->EventFlag == ImGuiInputTextFlags.CallbackResize)
                        {
                            Array.Resize(ref dumpPath, dumpPath.Length * 2);
                            handle = GCHandle.Alloc(dumpPath, GCHandleType.Pinned);
                            fixed (byte* c = dumpPath)
                            {
                                data->Buf = c;
                            }
                            data->BufSize = dumpPath.Length;
                        }
                        return 0;
                    });
                    handle?.Free();
                }

                var enums = Enum.GetNames<Compatibility>();
                int curi = (int)ILHookDebuggerModule.IDE.Value;
                if (ImGui.ListBox("IDE", ref curi, enums, enums.Length))
                {
                    ILHookDebuggerModule.IDE.Value = (Compatibility)curi;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(nameof(ILHookDebugger), "[MappingUtilsIntegartion]" + (exception = ex.ToString()));
            }
        }
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (ILHookDebuggerModule.Settings.PanelKey?.Pressed ?? false)
            {
                ILHookDebuggerModule.Settings.PanelKey.ConsumePress();
                Display = !Display;
            }
        }
    }
    static class Helpery
    {
        public static string GetMethodNameForDB(this MethodBase method)
        {
            ParameterInfo[]? param = null;
            param = method switch
            {
                MethodInfo mi => mi.GetParameters(),
                ConstructorInfo ci => ci.GetParameters(),
                _ => null
            };

            if (param is null)
            {
                return $"{method.DeclaringType?.FullName}.{method.Name}(?)";
            }
            return $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(',',
                param.Select(x => x.ParameterType.Name))})";
        }
        public static string GetMethodNameForFileName(this MethodBase method)
            => new(
                method
                .GetMethodNameForDB()
                .Select(x => invalidFileName.Value.Contains(x) ? '_' : x)
                .ToArray()
                );
        static readonly Lazy<HashSet<char>> invalidFileName = new(() =>
        {
            return [.. Path.GetInvalidFileNameChars()];
        });
    }
}



