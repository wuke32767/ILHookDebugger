using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    using Resolve = NotTooLazy<MethodReference, MethodBase>;

    internal class StealCompilerGenerated : Transform
    {
        public static string Prefix => "<Proxy>";
        // https://github.com/icsharpcode/ILSpy/blob/6755d27a96f4d443cbee93119fa3166334fe63d5/ICSharpCode.Decompiler/IL/Transforms/DelegateConstruction.cs#L112
        static bool IsAnonymousMethod(MethodReference method, ref Resolve cache)
        {
            if (method == null)
                return false;
            if (!(HasGeneratedName(method)
                || method.Name.Contains('$')
                || IsCompilerGenerated(method, ref cache)
                //|| TransformDisplayClassUsage.IsPotentialClosure(
                //    decompiledTypeDefinition, method.DeclaringTypeDefinition)
                || ContainsAnonymousType(method)))
            {
                return false;
            }
            return true;
        }

        public static bool HasGeneratedName(MethodReference member) => member.Name.StartsWith('<');
        public static bool HasGeneratedName(TypeReference member) => member.Name.Contains('<');
        static bool ContainsAnonymousType(MethodReference method)
        {
            static bool ContainsAnonymousType(TypeReference type) => IsAnonymousType(type); // visit what?
            static bool IsAnonymousType(TypeReference type)
            {
                if (type == null)
                    return false;
                if (string.IsNullOrEmpty(type.Namespace) && HasGeneratedName(type)
                    && (type.Name.Contains("AnonType") || type.Name.Contains("AnonymousType")))
                {
                    return (type.ResolveReflection()?.GetCustomAttribute<CompilerGeneratedAttribute>()) is not null;
                }
                return false;
            }
            if (ContainsAnonymousType(method.ReturnType))
                return true;
            foreach (var p in method.Parameters)
            {
                if (ContainsAnonymousType(p.ParameterType))
                    return true;
            }
            return false;
        }
        public static bool IsCompilerGenerated(MethodReference entity, ref Resolve cache)
        {
            return (cache.Value?.GetCustomAttribute<CompilerGeneratedAttribute>()) is not null;
        }

        static bool isDelegate(TypeReference type)
        {
            return type.ResolveReflection().IsAssignableTo(typeof(Delegate));
        }
        internal override void Run(ILContext il, IDEFeatures feat)
        {
            if (!feat.HasFlag(IDEFeatures.CanNotInlineDelegate) || !ILHookDebuggerModule.Settings.DecompilerHackFix1)
            {
                return;
            }
            int index = 0;
            ILCursor ic = new(il);
            MethodReference? fun = null;
            MethodReference? ctor = null;
            while (ic.TryGotoNext(MoveType.Before, i => i.MatchLdftn(out fun) || i.MatchLdvirtftn(out fun), i => i.MatchNewobj(out ctor)))
            {
                Resolve cache = new(static r => r.ResolveReflection(), fun!);
                if (fun is not null && ctor is not null && isDelegate(ctor.DeclaringType) && IsAnonymousMethod(fun, ref cache))
                {
                    MethodDefinition def = new(Prefix + (index++) + "$" + fun.Name.Simplify(feat),
                        Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.Public,
                        fun.ReturnType);
                    if (fun.HasThis)
                    {
                        def.Parameters.Add(new(fun.DeclaringType));
                    }
                    def.Parameters.AddRange(fun.Parameters);
                    using var il2 = new ILContext(def);
                    var ix = new ILCursor(il2);
                    foreach (var i in def.Parameters)
                    {
                        ix.EmitLdarg(i);
                    }
                    if (ic.Next!.OpCode == OpCodes.Ldvirtftn)
                    {
                        ix.EmitCallvirt(fun);
                    }
                    else
                    {
                        ix.EmitCall(fun);
                    }
                    ix.EmitRet();

                    def.CustomAttributes.Add(new(il.Import(typeof(CompilerGeneratedAttribute).GetConstructor([])!)));
                    def.CustomAttributes.Add(new(il.Import(typeof(ExtensionAttribute).GetConstructor([])!)));

                    il.Method.DeclaringType.Methods.Add(def);

                    ic.MoveAfterLabels();
                    ic.Emit(ic.Next.OpCode, def);
                    ic.Remove();
                }
                else
                {
                    ic.Index++;
                }
            }
        }
    }
}
