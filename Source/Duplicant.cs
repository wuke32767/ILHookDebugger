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
    record class Info(ILHook Detour, AssemblyLoadContext Context)
    {
        public void Dispose()
        {
            Detour?.Dispose();
            Context?.Unload();
        }
    }
    internal static class Duplicant
    {
        static List<Info> AllDuplicant = new();
        static int unique = 0;
        public static void Create(MethodBase mi)
        {
            var context = new AssemblyLoadContext($"{nameof(ILHookDebugger)}_{unique++}", true);
            context.Resolving += (context, asm) =>
            {
                var f = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => asm.Name == x.GetName().Name);
                return f;
            };
            using DynamicMethodDefinition dmd = new(mi);

            var il = new ILContext(dmd.Definition);
            using MemoryStream output = new();
            var md = il.Method;
            var dmdtype = md.DeclaringType;
            var mdm = md.Module;
            var asm = mdm.Assembly;
            var _iact = mdm.ImportReference(typeof(IgnoresAccessChecksToAttribute)).Resolve();
            var iact = mdm.ImportReference(_iact.GetConstructors().First());

            md.Name = mi.Name;
            dmdtype.Name = $"{nameof(ILHookDebugger)}#Type#{unique}#{mi.DeclaringType.Name}";
            dmdtype.BaseType = mdm.TypeSystem.Object;
            mdm.Name = $"{nameof(ILHookDebugger)}#Module#{unique}";
            asm.Name.Name = $"{(nameof(ILHookDebugger))}#Asm#{unique}";

            //copied from Mapping Utils
            var hooked = DetourManager.GetDetourInfo(mi).ILHooks;
            MethodInfo dup = null;
            if (hooked is not null)
            {
                foreach (var hook1 in hooked)
                {
                    // ILHookInfo only gives us public access to the method the manipulator delegate calls.
                    // We need to retrieve the actual delegate passed to the original IL hook, as the manipulator method it calls may be non-static...
                    // Time to use monomod to access monomod internals :)
                    var hookState = new DynamicData(hook1).Get("hook")!;
                    var manipulator = new DynamicData(hookState).Get<ILContext.Manipulator>("Manip")!;
                    if (manipulator.Method != ((Action<ILContext>)hookmethod).Method)
                    {
                        try
                        {
                            il.Invoke(manipulator);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log(nameof(ILHookDebugger), $"[Copied from MappingUtils.ILHookDiffer] Failed to apply IL hook {hook1.ManipulatorMethod.GetID()}: {ex}");
                        }
                    }
                }
            }
            ILCursor ic = new(il);
            ic.EmitDelegate(Debugger.Break);
            foreach (var _s in mdm.AssemblyReferences)
            {
                var s = _s.Name;
                var attr = new CustomAttribute(iact);
                attr.ConstructorArguments.Add(new(mdm.TypeSystem.String, s));
                asm.CustomAttributes.Add(attr);
            }

            asm.Write(output);
            output.Seek(0, SeekOrigin.Begin);

            dup = context
                .LoadFromStream(output)
                .GetTypes().First(x => x.Name == dmdtype.Name)
                .GetMethod(md.Name);

            var hook = new ILHook(mi, hookmethod);
            void hookmethod(ILContext il)
            {
                ILCursor ic = new(il);
                for (var i = 0; i < il.Method.Parameters.Count; i++)
                {
                    ic.EmitLdarg(i);
                }
                ic.EmitCall(dup);
                ic.EmitRet();

            };
            unique++;

            AllDuplicant.Add(new(hook, context));
        }

        internal static void Clear()
        {
            foreach (var item in AllDuplicant)
            {
                item.Dispose();
            }
            AllDuplicant.Clear();
        }

        internal static void RefreshAll()
        {
            var orig = AllDuplicant.Select(x => x.Detour).ToList();
            Clear();
            foreach (var item in orig)
            {
                Create(item.Method);
            }
        }

        internal static void Remove()
        {
            if (AllDuplicant.Count > 0)
            {
                AllDuplicant[^1].Dispose();
            }
            AllDuplicant.RemoveAt(AllDuplicant.Count - 1);
        }
    }
}
