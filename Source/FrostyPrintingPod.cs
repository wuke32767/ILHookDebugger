using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.MappingUtils.Helpers;
using Celeste.Mod.MappingUtils.ImGuiHandlers;
using ImGuiNET;
using System.Linq;
using Celeste.Mod.MappingUtils.Commands;
using MonoMod.RuntimeDetour;
using Celeste.Mod.MappingUtils;
using System.Collections;
namespace Celeste.Mod.ILHookDebugger.MappingUtils
{
    public class FrostyPrintingPod : Tab
    {
        private MethodBase? _selectedMethod;
        private ComboCache<MethodBase> _comboCache = new();

        public override string Name => "ILHookDebugger";

        public override bool CanBeVisible() => true;

        readonly List<Duplicant> toremove = [];

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

        public override void Render(Level? level)
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

            cur = ILHookDebuggerModule.UnloadWhenDetached;
            if (ImGui.Checkbox("Unload When Detached", ref cur))
            {
                ILHookDebuggerModule.UnloadWhenDetached.Value = cur;
            }
            ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Settings_UnloadWhenDetached_Help", lang));
            //ImGui.SameLine();

            if (ImGui.Button("Refresh"))
            {
                PrintingPod.Refresh();
            }
            ImGui.SetItemTooltip(Dialog.Clean("ILHookDebugger_Help_RefreshIsAllYouNeed", Dialog.Languages["english"]));

            var hooks = GetEverHookedMethods().ToList();
            if (ImGuiExt.Combo("Method", ref _selectedMethod!, hooks, m => m?.GetMethodNameForDB() ?? "", _comboCache, tooltip: null,
                    ImGuiComboFlags.None))
            {
                if (_selectedMethod is not null)
                {
                    PrintingPod.Create(_selectedMethod!);
                }
                _selectedMethod = null;
            }

            var flags = PrintingPod.AllDuplicants;
            toremove.Clear();
            if (ImGui.BeginTable("Debugging", 1, ImGuiExt.TableFlags | ImGuiTableFlags.NoSavedSettings))
            {

                ImGui.TableSetupColumn("Debugging", ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var f in flags)
                {
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ItemWidth);
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
    }
}



