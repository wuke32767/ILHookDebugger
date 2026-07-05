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
        public virtual ImGuiWindowFlags flags => ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoSavedSettings;
    }

    public class EditorDisplayer(TextEditor editor, string title) : IExtraWindow
    {
        public override void Render()
        {
            editor.Render("");
        }
        public override string Title { get => title; }
    }

    internal sealed class MinimalDisplayer(IReadOnlyList<IReadOnlyList<Token<Color>>> colors, string title) : IExtraWindow
    {
        public override string Title { get => title; }

        public override void Render()
        {
            if (ImGui.Button("Copy Code"))
            {
                ImGui.SetClipboardText(string.Join('\n', colors.Select(x => string.Concat(x.Select(y => y.token)))));
            }
            if (ImGui.BeginChild(""))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(0, ImGui.GetStyle().ItemSpacing.Y));
                for (int i = 0; i < colors.Count; i++)
                {
                    var s = colors[i];
                    var c = i >= colors.Count ? [] : colors[i];
                    for (int j = 0; j < s.Count; j++)
                    {
                        var word = s[j];
                        var cc = word.color.ToVector4();
                        Helpery.TextGoodColored(cc, word.token);
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
