using Mono.Cecil;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.ILHookDebugger
{
    internal class DamnCacheFixer : Transform
    {
        internal override void Run(ILContext il, IDEFeatures feat)
        {
            if (feat.HasFlag(IDEFeatures.DamnTypeResolveCache))
            {
                PrintingPod.RunRelinker((m, cxt) =>
                {
                    if (m is MemberReference t)
                    {
                        var scope = t.DeclaringType ?? t as TypeReference;
                        if (scope?.Scope is AssemblyNameReference an && an.Name is "mscorlib" or "System.Runtime")
                        {
                            var mm = il.Module;
                            return t.ResolveReflection() switch
                            {
                                Type o => il.Import(o),
                                MethodBase o => il.Import(o),
                                FieldInfo o => il.Import(o),
                                _ => m,
                            };
                        }
                    }
                    return m;
                }, il.Method);
                var a = il.Module.AssemblyReferences.Where(x => x.Name == "mscorlib").ToArray();
                var b = il.Module.AssemblyReferences.Where(x => x.Name == "System.Runtime").ToArray();
                foreach (var ax in a.Concat(b))
                {
                    il.Module.AssemblyReferences.Remove(ax);
                }
                foreach (var ax in a.Concat(b))
                {
                    il.Module.AssemblyReferences.Add(ax);
                }
            }
        }
    }
}