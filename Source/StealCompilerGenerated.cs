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

    internal class StealCompilerGenerated(TypeAttr attr) : Transform()
    {
        public static string Prefix => "GetDelegateFactory(";
        public static string Suffix => ").Create";
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
            if (!feat.HasFlag(IDEFeatures.CanNotInlineDelegate))
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
                    var code = ic.Next!.OpCode;
                    string name;
                    if (feat.HasFlag(IDEFeatures.NormalizeName))
                    {
                        name = $"CreateDelegateFor<{fun.Name}@{index++}>";
                    }
                    else
                    {
                        name = $"GetDelegateFactory({fun.Name}@{index++}).Create";
                    }
                    MethodDefinition def = new(name,
                        Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.Public,
                        ctor.DeclaringType);

                    using var il2 = new ILContext(def);
                    var ix = new ILCursor(il2);
                    ParameterDefinition p = new(il.Module.TypeSystem.Object);
                    def.Parameters.Add(p);
                    ix.EmitLdarg0();
                    if (code == OpCodes.Ldvirtftn)
                    {
                        ParameterDefinition px = new(il.Module.TypeSystem.Object);
                        def.Parameters.Add(px);
                        ix.EmitLdarg1();
                    }
                    ix.Emit(code, fun);
                    VariableDefinition local = new(il.Module.TypeSystem.IntPtr);
                    def.Body.Variables.Add(local);
                    ix.EmitStloc(local);
                    ix.EmitLdloc(local);
                    ix.EmitNewobj(ctor);

                    ix.EmitRet();

                    def.CustomAttributes.Add(new(il.Import(typeof(CompilerGeneratedAttribute).GetConstructor([])!)));
                    def.CustomAttributes.Add(new(attr.MakeExtension(il)));

                    il.Method.DeclaringType.Methods.Add(def);

                    ic.MoveAfterLabels();
                    ic.EmitCall(def);
                    ic.Remove();
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
