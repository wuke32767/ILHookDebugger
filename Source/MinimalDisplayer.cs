using ImGuiNET;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    internal sealed class MinimalDisplayer(IReadOnlyList<string> strings, IReadOnlyList<IReadOnlyList<Color>> colors)
    {
        public void Render()
        {
            if (ImGui.BeginChild(""))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(0, ImGui.GetStyle().ItemSpacing.Y));
                for (int i = 0; i < strings.Count; i++)
                {
                    var s = strings[i];
                    var c = i >= colors.Count ? [] : colors[i];
                    for (int j = 0; j < strings[i].Length; j++)
                    {
                        var cc = c[j].ToVector4();
                        ImGui.TextColored(new(cc.X, cc.Y, cc.Z, cc.W), s.AsSpan(j, 1));
                        ImGui.SameLine();
                    }
                    ImGui.Text("");
                }
                ImGui.PopStyleVar();
                ImGui.EndChild();
            }

        }
    }
}
