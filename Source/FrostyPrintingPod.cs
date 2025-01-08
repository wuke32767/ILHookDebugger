using System;
using System.Collections.Generic;
using System.Reflection;
using ImGuiNET;
using System.Linq;
using MonoMod.RuntimeDetour;
using System.Collections;
using Monocle;
using Celeste.Mod.ImGuiHelper;
using Microsoft.Xna.Framework;
using System.Runtime.InteropServices;

namespace Celeste.Mod.ILHookDebugger.MappingUtils
{
    public class FrostyPrintingPod : Mod.MappingUtils.ImGuiHandlers.Tab
    {
        private MethodBase? _selectedMethod;

        public override string Name => "ILHookDebugger";

        public override bool CanBeVisible() => true;

        public override void Render(Level? level)
        {
            MiGui.RenderCore();
        }
    }

    public class MiGui : ImGuiHandler
    {
        static readonly List<Duplicant> toremove = [];
        static string? exception = null;

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

        static byte[] searchText = new byte[512];
        static readonly List<(string name, MethodBase method)> searchResult = [];
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
        }
        public static bool Display = false;
        public static void RenderCore()
        {
            if (exception is not null)
            {
                ImGui.Text("Encounter an error. Check logs.");
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
                if (!Dialog.Languages.TryGetValue("english", out var lang))
                {
                    ImGui.Text("Waiting for Everest loading");
                    return;
                }
                bool cur = ILHookDebuggerModule.BreakOnce;
                if (ImGui.Checkbox("Break Once", ref cur))
                {
                    ILHookDebuggerModule.BreakOnce.Value = cur;
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_BreakOnce_Help", lang));
                ImGui.SameLine();

                cur = ILHookDebuggerModule.PrettifyMonoMod;
                if (ImGui.Checkbox("Prettify MonoMod", ref cur))
                {
                    ILHookDebuggerModule.PrettifyMonoMod.Value = cur;
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_PrettifyMonoMod_Help", lang));
                ImGui.SameLine();

                cur = ILHookDebuggerModule.UnloadWhenDetached;
                if (ImGui.Checkbox("Unload When Detached", ref cur))
                {
                    ILHookDebuggerModule.UnloadWhenDetached.Value = cur;
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_UnloadWhenDetached_Help", lang));
                ImGui.SameLine();
                if (ImGui.Button("Save Settings"))
                {
                    ILHookDebuggerModule.Instance.SaveSettings();
                }



                if (ImGui.Button("Refresh"))
                {
                    PrintingPod.Refresh();
                }
                ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_RefreshIsAllYouNeed", Dialog.Languages["english"]));

                ImGui.SameLine();
                //https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.debugger.launch
                //windows only
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (ImGui.Button("Launch IDE Debugger"))
                    {
                        System.Diagnostics.Debugger.Launch();
                    }
                }

                if (ImGui.BeginTable("Search..", 1,
                    ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.Hideable |
                    ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.ScrollY,
                    new(0, ImGui.GetTextLineHeightWithSpacing() * Calc.Clamp(searchResult.Count + 2, 2, 15))))
                {

                    ImGui.TableSetupColumn("Search..", ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHeaderLabel);
                    ImGui.TableSetupScrollFreeze(0, 1);
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

                    foreach (var (na, me) in searchResult)
                    {
                        ImGui.TableNextColumn();
                        if (ImGui.Selectable(na))
                        {
                            PrintingPod.Create(me);
                        }
                    }

                    ImGui.EndTable();
                }

                var flags = PrintingPod.AllDuplicants;
                toremove.Clear();
                if (ImGui.BeginTable("Debugging", 1,
                    ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.Hideable |
                    ImGuiTableFlags.NoSavedSettings))
                {

                    ImGui.TableSetupColumn("Debugging", ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    foreach (var f in flags)
                    {
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(150);
                        if (ImGui.Selectable(f.Target.GetMethodNameForDB()))
                        {
                            toremove.Add(f);
                        }
                    }

                    ImGui.EndTable();
                }
                foreach (var f in toremove)
                {
                    PrintingPod.Remove(f);
                }

            }
            catch (Exception ex)
            {
                Logger.Error(nameof(ILHookDebugger), "[MappingUtilsIntegartion]" + (exception = $"""
                    error.
                    {ex}
                    """));
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
    }
}



