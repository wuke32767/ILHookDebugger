using Celeste.Mod.Helpers.LegacyMonoMod;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{

    record class Duplicant(ILHook Detour, AssemblyLoadContext Context, MethodBase Target) : IDisposable
    {
        public void Dispose()
        {
            Detour?.Dispose();
            Context?.Unload();
        }
    }
    internal static class PrintingPod
    {
        public static List<Duplicant> AllDuplicants = [];
        public static Dictionary<MethodBase, Duplicant> DuplicantLookup = [];
        static int unique;
        public static void Create(MethodBase mi)
        {
            if (DuplicantLookup.TryGetValue(mi, out var exist))
            {
                Remove(exist);
            }
            var context = new AssemblyLoadContext($"{nameof(ILHookDebugger)}_{unique++}", true);
            context.Resolving += (context, asm) =>
            {
                var f = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(x => asm.FullName == x.FullName);
                return f;

            };
            var hook = new ILHook(mi, il =>
            {
                ILCursor ic = new(il);

                int unique = System.Threading.Interlocked.Increment(ref PrintingPod.unique);
                using MemoryStream output = new();
                var md = il.Method;
                var backup = il.Instrs.ToArray();
                var lackup = il.Labels.Select(x => x.Target).ToArray();
                var dmdtype = md.DeclaringType;
                var mdm = md.Module;
                var asm = mdm.Assembly;
                var _iact = mdm.ImportReference(typeof(IgnoresAccessChecksToAttribute)).Resolve();
                var iact = mdm.ImportReference(_iact.GetConstructors().First());

                FieldDefinition shouldBreak = new("ShouldNotBreak_YouCanChangeThisFromYourIDEDebugger", Mono.Cecil.FieldAttributes.Static, mdm.TypeSystem.Boolean);
                dmdtype.Fields.Add(shouldBreak);
                FieldDefinition slots = new("_slot", Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Public, il.Import(typeof(object[])));
                dmdtype.Fields.Add(slots);

                List<object> localslots = [];

                var breaking = il.DefineLabel();
                ic.EmitLdsfld(shouldBreak);
                ic.EmitBrtrue(breaking);
                if (ILHookDebuggerModule.BreakOnce)
                {
                    ic.EmitLdcI4(1);
                    ic.EmitStsfld(shouldBreak);
                    shouldBreak.Name = "ShouldNotBreak";
                }
                ic.EmitDelegate(Debugger.Break);
                ic.MarkLabel(breaking);

                md.Name = mi.Name;
                dmdtype.Name = $"{nameof(ILHookDebugger)}#Type#{unique}#{mi.DeclaringType!.Name}";
                dmdtype.BaseType = mdm.TypeSystem.Object;
                dmdtype.Namespace = mi.DeclaringType.Namespace;
                mdm.Name = $"{nameof(ILHookDebugger)}#Module#{unique}";
                asm.Name.Name = $"{(nameof(ILHookDebugger))}#Asm#{unique}";

                il.Steal(localslots, slots);
                il.Prettify();

                var hooked = DetourManager.GetDetourInfo(mi).ILHooks;
                HashSet<string> checks = [];
                Dictionary<Instruction, object> restore = [];
                foreach (var instr in il.Instrs)
                {
                    //var mod = instr.Operand switch
                    //{
                    //    MethodReference mb => mb.Module.Assembly,
                    //    FieldReference fi => fi.DeclaringType.Module.Assembly,
                    //    TypeReference type => type.Module.Assembly,
                    //    _ => null,
                    //};
                    if (instr.Operand is ILLabel label)
                    {
                        restore.Add(instr, label);
                        instr.Operand = label.Target;
                    }
                    else if (instr.Operand is ILLabel[] targets)
                    {
                        restore.Add(instr, targets);
                        instr.Operand = targets.Select(l => l.Target).ToArray();
                    }
                    //if (mod is not null)
                    //{
                    //    checks.Add(mod.Name.Name);
                    //}
                }
                md.FixShortLongOps();
                //foreach (var s in checks)
                var debuggable = typeof(DebuggableAttribute).GetConstructor([typeof(bool), typeof(bool)]);
                var dattr = new CustomAttribute(il.Import(debuggable));
                dattr.ConstructorArguments.Add(new(mdm.TypeSystem.Boolean, true));
                dattr.ConstructorArguments.Add(new(mdm.TypeSystem.Boolean, true));
                asm.CustomAttributes.Add(dattr);

                foreach (var _s in mdm.AssemblyReferences)
                {
                    var s = _s.Name;
                    var attr = new CustomAttribute(iact);
                    attr.ConstructorArguments.Add(new(mdm.TypeSystem.String, s));
                    asm.CustomAttributes.Add(attr);
                }
                

                asm.Write(output);
                output.Seek(0, SeekOrigin.Begin);

                var typs = context
                    .LoadFromStream(output)
                    .GetTypes().First(x => x.Name == dmdtype.Name);
                var dup = typs.GetMethod(md.Name)!;
                if (localslots.Any())
                {
                    var remoteslots = typs.GetField(slots.Name)!;
                    remoteslots.SetValue(null, localslots.ToArray());
                }

                il.Instrs.Clear();
                foreach (var (o, b) in il.Labels.Zip(lackup))
                {
                    o.Target = b;
                }
                il.Instrs.AddRange(backup);
                foreach (var (k, v) in restore)
                {
                    k.Operand = v;
                }
                ic.Index = 0;
                for (var i = 0; i < md.Parameters.Count; i++)
                {
                    ic.EmitLdarg(i);
                }
                ic.EmitCall(dup);
                ic.EmitRet();
            });
            var dup = new Duplicant(hook, context, mi);
            AllDuplicants.Add(dup);
            DuplicantLookup.Add(mi, AllDuplicants[^1]);
            //scope.Dispose();
        }

        internal static List<Duplicant> Clear()
        {
            var orig = AllDuplicants.ToList();
            AllDuplicants.Clear();
            DuplicantLookup.Clear();
            foreach (var item in orig)
            {
                item.Dispose();
            }
            return orig;
        }

        internal static void Refresh()
        {
            foreach (var item in AllDuplicants.Select(x => x.Detour))
            {
                item.Undo();
                item.Apply();
            }
        }

        internal static void Refresh(Duplicant at)
        {
            var item = at.Detour;
            item.Undo();
            item.Apply();
        }
        internal static void Refresh(MethodBase at)
        {
            if (DuplicantLookup.TryGetValue(at, out var i))
            {
                Refresh(i);
            }
        }

        internal static void Remove()
        {
            if (AllDuplicants.Count > 0)
            {
                var orig = AllDuplicants[^1];
                DuplicantLookup.Remove(orig.Target);
                AllDuplicants.RemoveAt(AllDuplicants.Count - 1);
                orig.Dispose();
            }
        }
        internal static void Remove(Duplicant dup)
        {
            DuplicantLookup.Remove(dup.Target);
            AllDuplicants.Remove(dup);
            dup.Dispose();
        }
        internal static void RemoveAt(int at)
        {
            var orig = AllDuplicants[at];
            DuplicantLookup.Remove(orig.Target);
            AllDuplicants.RemoveAt(at);
            orig.Dispose();
        }
    }
}
