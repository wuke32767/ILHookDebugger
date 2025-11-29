using ICSharpCode.Decompiler.CSharp.Syntax;
using ImGuiColorTextEditNet;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    internal class SyntaxTreeHighlighter(List<List<PaletteIndex>> tree) : ISyntaxHighlighter
    {
        public bool AutoIndentation => false;
        public int MaxLinesPerFrame => 1919810;

        public object Colorize(Span<Glyph> line, object? state)
        {
            int at;
            if (state is null)
            {
                at = 0;
            }
            else if (state is int i)
            {
                at = i + 1;
            }
            else
            {
                throw new InvalidOperationException();
            }
            if (at >= tree.Count)
            {
                return at;
            }
            var cur = tree[at];
            for (int i = 0; i < line.Length; i++)
            {
                if (i >= cur.Count)
                {
                    break;
                }
                line[i] = new(line[i].Char, cur[i]);
            }
            return at;
        }

        public string? GetTooltip(string id)
        {
            return null;
        }
    }
}
