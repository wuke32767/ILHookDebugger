using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using Celeste.Mod.Helpers.LegacyMonoMod;
using Mono.Cecil;
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

    record class Duplicant(ILHook Detour, AssemblyLoadContext Context, MethodBase Target, ILHook? Dummy) : IDisposable
    {
        public void Dispose()
        {
            Detour?.Dispose();
            Context?.Unload();
            Dummy?.Dispose();
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
                var dmdtype = md.DeclaringType;
                var mdm = md.Module;
                var asm = mdm.Assembly;
                var _iact = mdm.ImportReference(typeof(IgnoresAccessChecksToAttribute)).Resolve();
                var iact = mdm.ImportReference(_iact.GetConstructors().First());

                FieldDefinition shouldBreak = new("ShouldNotBreak_YouCanChangeThisFromYourIDEDebugger", Mono.Cecil.FieldAttributes.Static, mdm.TypeSystem.Boolean);
                dmdtype.Fields.Add(shouldBreak);

                var breaking = il.DefineLabel();
                ic.EmitLdsfld(shouldBreak);
                ic.EmitBrtrue(breaking);
                ic.EmitDelegate(Debugger.Break);
                ic.MarkLabel(breaking);

                md.Name = mi.Name;
                dmdtype.Name = $"{nameof(ILHookDebugger)}#Type#{unique}#{mi.DeclaringType!.Name}";
                dmdtype.BaseType = mdm.TypeSystem.Object;
                dmdtype.Namespace = mi.DeclaringType.Namespace;
                mdm.Name = $"{nameof(ILHookDebugger)}#Module#{unique}";
                asm.Name.Name = $"{(nameof(ILHookDebugger))}#Asm#{unique}";

                var hooked = DetourManager.GetDetourInfo(mi).ILHooks;
                HashSet<string> checks = [];
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
                        instr.Operand = label.Target;
                    }
                    else if (instr.Operand is ILLabel[] targets)
                    {
                        instr.Operand = targets.Select(l => l.Target).ToArray();
                    }
                    //if (mod is not null)
                    //{
                    //    checks.Add(mod.Name.Name);
                    //}
                }
                md.FixShortLongOps();
                //foreach (var s in checks)
                foreach (var _s in mdm.AssemblyReferences)
                {
                    var s = _s.Name;
                    var attr = new CustomAttribute(iact);
                    attr.ConstructorArguments.Add(new(mdm.TypeSystem.String, s));
                    asm.CustomAttributes.Add(attr);
                }


                asm.Write(output);
                output.Seek(0, SeekOrigin.Begin);

                var dup = context
                    .LoadFromStream(output)
                    .GetTypes().First(x => x.Name == dmdtype.Name)
                    .GetMethod(md.Name)!;

                ic.Index = 0;
                for (var i = 0; i < md.Parameters.Count; i++)
                {
                    ic.EmitLdarg(i);
                }
                ic.EmitCall(dup);
                ic.EmitRet();
            }, !ILHookDebuggerModule.HookMonoModInternal);
            ILHook? _dummy = null;
            if (ILHookDebuggerModule.HookMonoModInternal)
            {
                _dummy = new ILHook(mi, i => { }, false);
            }
            var dup = new Duplicant(hook, context, mi, _dummy);
            AllDuplicants.Add(dup);
            DuplicantLookup.Add(mi, AllDuplicants[^1]);
            _dummy?.Apply();
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
            var orig = Clear();
            foreach (var item in orig)
            {
                Create(item.Detour.Method);
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
