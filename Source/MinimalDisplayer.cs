using ImGuiColorTextEditNet;
using ImGuiNET;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    public abstract class IExtraWindow()
    {
        string? ex;
        public void ProtectedRender()
        {
            if (ex != null)
            {
                ImGui.Text("error.");
                ImGui.Text(ex.ToString());
                if (ImGui.Button("OK"))
                {
                    ex = null;
                }
            }
            else
            {
                try
                {
                    Render();
                }
                catch (Exception e)
                {
                    ex = e.ToString();
                }
            }
        }
        public abstract void Render();
        public abstract string Title { get; }
    }

    public class EditorDisplayer(TextEditor editor, string title) : IExtraWindow
    {
        public override void Render()
        {
            editor.Render("");
        }
        public override string Title { get => title; }
    }

    internal sealed class MinimalDisplayer(IReadOnlyList<string> strings, IReadOnlyList<IReadOnlyList<Color>> colors, string title) : IExtraWindow
    {
        public override string Title { get => title; }

        public override void Render()
        {
            if (ImGui.Button("Copy All"))
            {
                ImGui.SetClipboardText(string.Join('\n', strings));
            }
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
