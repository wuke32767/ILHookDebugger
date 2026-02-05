using Celeste.Mod.ILHookDebugger.MappingUtils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Celeste.Mod.ILHookDebugger
{
    internal class Diff
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_Labels")]
        static extern ref List<ILLabel> GetLabels(ILContext il);
        [Flags]
        public enum Status
        {
            NotInArray = 1,
            Added = 1 << 1,
            Removed = 1 << 2,
            Crossed = Added + Removed,
        }
        public record struct InstrData(OpCode op, object operand)
        {
            public static implicit operator InstrData(Instruction i)
            {
                return new(i.OpCode, i.Operand);
            }
        }
        public record struct Annotation(Status stat, string? by = null, string? And = null)
        {
            internal readonly List<string> GetSrc()
            {
                List<string> src = [];
                if (by is { } b)
                {
                    src.Add(b);
                }
                if (And is { } a)
                {
                    src.Add(a);
                }
                return src;
            }
        }

        public class Annotated(Instruction instr, Annotation anon, InstrData raw)
        {
            public Annotated(Instruction instr, Annotation anon) : this(instr, anon, instr) { }
            public Instruction Instr = instr;
            public Annotation Anon = anon;
            public InstrData Raw = raw;
            public bool Changed()
            {
                return Raw != (InstrData)Instr;
            }
        }
        // all instructions, includes removed instrs.
        public List<Annotated> instructions;
        // all labels.
        public Dictionary<ILLabel, Instruction> labelmap;
        // all instructions, excludes removed instrs.
        // sometimes contains removed instrs, but who cares.
        HashSet<Instruction> instrs;
        internal Diff(ILContext il)
        {
            instructions = il.Instrs.Select(x => new Annotated(x, new(default))).ToList();
            labelmap = il.Labels.Where(x => x.Target is { }).ToDictionary(x => x, x => x.Target!);
            instrs = il.Instrs.ToHashSet();
        }

        internal void Update(ILContext il, string user)
        {
            Annotated dymmy = new(default!, default, default);
            var cur = il.Instrs;
            {
                // if ilhook adds an instruction, it should be a new instance.
                // which is guaranteed by monomod.

                // it can be broken by accessing the mono.cecil directly.

                // merges old and new array, at the same time find out which instrs were removed or added.
                int cnt = cur.Where(i => !instrs.Contains(i)).Count();
                int ii = instructions.Count - 1;
                int ic = cur.Count - 1;
                int iw = ii + cnt;
                instructions.EnsureCapacity(iw + 1);
                instructions.AddRange(Enumerable.Repeat(default(Annotated)!, cnt));
                while (iw >= 0 && ic >= 0)
                {
                    var i = ii < 0 ? dymmy : instructions[ii];
                    var c = cur[ic];
                    if (i.Instr == c)
                    {
                        instructions[iw] = i;
                        ii--;
                        ic--;
                        iw--;
                    }
                    else if (instrs.Contains(c))
                    {
                        instructions[iw] = i;
                        if (!i.Anon.stat.HasFlag(Status.NotInArray))
                        {
                            removed(user, i);
                            instrs.Remove(i.Instr);
                        }
                        ii--;
                        iw--;
                    }
                    else
                    {
                        instructions[iw] = new(c, new(Status.Added, user));
                        instrs.Add(c);
                        ic--;
                        iw--;
                    }
                }
            }
            {
                // illabel should never be used for multiple times.
                // monomod will convert any illabel to instruction, and convert back.
                HashSet<ILLabel> dup = [];
                HashSet<ILLabel> visited = [];
                foreach (var i in il.Instrs)
                {
                    if (i.Operand is ILLabel l)
                    {
                        if (!visited.Add(l))
                        {
                            dup.Add(l);
                        }
                    }
                    else if (i.Operand is ILLabel[] ls)
                    {
                        foreach (ref var ip in ls.AsSpan())
                        {
                            if (!visited.Add(ip))
                            {
                                dup.Add(ip);
                            }
                        }
                    }
                }
                foreach (var ia in instructions)
                {
                    var i = ia.Instr;
                    if (ia.Anon.by == user && !ia.Anon.stat.HasFlag(Status.NotInArray))
                    {
                        if (i.Operand is ILLabel l)
                        {
                            if (dup.Contains(l))
                            {
                                i.Operand = il.DefineLabel(l.Target!);
                                ia.Raw.operand = i.Operand;
                            }
                        }
                        else if (i.Operand is ILLabel[] ls)
                        {
                            foreach (var ip in ls)
                            {
                                if (dup.Contains(ip))
                                {
                                    i.Operand = ls.Select(x => il.DefineLabel(x.Target!)).ToArray();
                                    ia.Raw.operand = i.Operand;
                                    break;
                                }
                            }
                        }
                    }
                }
                var lost = GetLabels(il);
                lost.RemoveAll(l => !visited.Contains(l));
            }
            {
                // find out which illabels were modified.
                // related diff is generated in the next step.
                HashSet<ILLabel> badlabel = [];
                Dictionary<Instruction, int> index = [];
                foreach (var i in labelmap)
                {
                    if (i.Key.Target != i.Value)
                    {
                        index.TryAdd(i.Key.Target!, -1);
                        index.TryAdd(i.Value, -1);
                    }
                }
                for (int i1 = 0; i1 < instructions.Count; i1++)
                {
                    var i = instructions[i1];
                    ref var v = ref CollectionsMarshal.GetValueRefOrNullRef(index, i.Instr);
                    if (!Unsafe.IsNullRef(ref v))
                    {
                        v = i1;
                    }
                }
                foreach (var i in labelmap)
                {
                    var now = i.Key.Target!;
                    var old = i.Value;
                    if (now != old)
                    {
                        var l = index[now];
                        var r = index[old];
                        var ll = Math.Min(l, r);
                        var rr = Math.Max(l, r);
                        if (!instructions.Take(rr).Skip(ll).All(x => x.Anon.by == user))
                        {
                            badlabel.Add(i.Key);
                        }
                    }
                }
                foreach (var i in instructions)
                {
                    var ix = i.Instr;
                    if (ix.Operand is ILLabel l && badlabel.Contains(l))
                    {
                        i.Raw.operand = il.DefineLabel(labelmap[l]);
                    }
                    if (ix.Operand is ILLabel[] ls)
                    {
                        ILLabel[]? orig = null;
                        for (int i1 = 0; i1 < ls.Length; i1++)
                        {
                            ILLabel? lo = ls[i1];
                            if (badlabel.Contains(lo))
                            {
                                orig ??= ls.ToArray();
                                orig[i1] = il.DefineLabel(labelmap[lo]);
                            }
                        }
                        if (orig is { })
                        {
                            i.Raw.operand = orig;
                        }
                    }
                }
                labelmap = il.Labels.Where(x => x.Target is { }).ToDictionary(x => x, x => x.Target!);
            }
            // find out which instrs were modified.
            UpdateIf(i => i.Changed());
            void UpdateIf(Func<Annotated, bool> f)
            {
                int cnt = instructions.Where(f).Count();
                int ii = instructions.Count - 1;
                int iw = ii + cnt;
                instructions.EnsureCapacity(iw + 1);
                instructions.AddRange(Enumerable.Repeat(default(Annotated)!, cnt));
                while (iw >= 0)
                {
                    var i = instructions[ii];
                    if (f(i))
                    {
                        var o = i;
                        i = new(o.Instr, new(Status.Added, user));
                        o.Instr = Instruction.Create(OpCodes.Nop);
                        o.Instr.OpCode = o.Raw.op;
                        o.Instr.Operand = o.Raw.operand;
                        o.Instr.Offset = i.Instr.Offset;
                        i.Instr.Offset = 0;
                        removed(user, o);
                        instructions[iw] = o;
                        iw--;
                    }
                    instructions[iw] = i;
                    ii--;
                    iw--;
                }
            }

            void removed(string user, Annotated i)
            {
                Annotation anon = i.Anon;
                anon.stat |= Status.Removed | Status.NotInArray;
                anon.And = anon.by;
                anon.by = user;
                i.Anon = anon;
            }
        }
        internal void Final(ILContext il)
        {
            foreach (var instr in instructions.Select(x => x.Instr))
            {
                if (instr.Operand is ILLabel target)
                    instr.Operand = target.Target;
                else if (instr.Operand is ILLabel[] targets)
                    instr.Operand = targets.Select(t => t.Target).ToArray();
            }

            int vindex = Math.Max(0x10000, instructions.Count * 10 + 10);
            foreach (var instr in instructions)
            {
                if (instr.Anon.stat.HasFlag(Status.Added) && instr.Instr.Offset == 0)
                {
                    instr.Instr.Offset = vindex++;
                }
            }
        }
        static System.Reflection.MethodInfo getvalue = typeof(DynamicReferenceManager).GetMethod("GetValueTUnsafe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        internal object GetResult()
        {
            Dictionary<Instruction, object?> drm = [];
            for (int i = 0; i < instructions.Count - 2; i++)
            {
                var a = instructions[i];
                var b = instructions[i + 1];
                var c = instructions[i + 2];
                if (a.Instr.MatchLdcI4(out var ax) && b.Instr.MatchLdcI4(out var bx) && c.Instr.MatchCall(out var cm) &&
                    cm is GenericInstanceMethod gcm && gcm.ElementMethod.Is(getvalue))
                {
                    drm[c.Instr] = getvalue.MakeGenericMethod(gcm.GenericArguments[0].ResolveReflection()).Invoke(null, [ax, bx]);
                }
            }
            string mis(Status s)
            {
                StringBuilder b = new();
                if (s.HasFlag(Status.NotInArray))
                {
                    b.Append('o');
                }
                if (s.HasFlag(Status.Added))
                {
                    b.Append('a');
                }
                if (s.HasFlag(Status.Removed))
                {
                    b.Append('r');
                }
                return b.ToString();
            }
            return instructions.Select(x =>
                new
                {
                    flag = mis(x.Anon.stat),
                    instr = x.Instr.ToString(),
                    offset = x.Instr.Offset,
                    bound = drm.TryGetValue(x.Instr, out var bb) ?
                        (bb is Delegate mi ? $"Invoke: {mi.Method.GetMethodNameForDB()}" : $"Reference: {bb}") :
                        null,
                    @break = x.Instr.Operand switch
                    {
                        Instruction ix => [ix.Offset],
                        Instruction[] ixs => ixs.Select(x => x.Offset).ToArray(),
                        _ => [],
                    },
                    src = x.Anon.GetSrc(),
                }
            ).ToArray();
        }
    }
}
