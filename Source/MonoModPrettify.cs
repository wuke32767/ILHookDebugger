#pragma warning disable CL0005 // ILCursor.Remove or RemoveRange used
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Celeste.Mod.ILHookDebugger
{
    public static partial class MonoModPrettify
    {
        public static Dictionary<string, string> Names = [];
        public static string ModName(MethodInfo ins)
        {
            var name = ins.Name;
            var split = ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.NormalizeName)
                ? "_" : "@";
            if (MatchLambda().Match(name) is { Success: true } lam)
            {
                name = $"{lam.Groups["in"]}{split}lam";
            }
            else if (MatchLocalFunc().Match(name) is { Success: true } loc)
            {
                name = $"{loc.Groups["in"]}{loc.Groups["id"]}{loc.Groups["name"]}";
            }
            return name + split + GetShortModName(ins);

            static string GetShortModName(MethodInfo ins)
            {
                var asmn = ins.Module?.Assembly?.GetName()?.Name;
                if (asmn is not null)
                {
                    if (Names.TryGetValue(asmn, out var pre))
                    {
                        return pre;
                    }
                    var l = string.Concat(asmn.Where(ins.Name.Length switch
                    {
                        > 25 => char.IsUpper,
                        > 15 => i => char.IsUpper(i) ||
                        (char.IsLower(i) && i != 'a' && i != 'e' && i != 'i' && i != 'o' && i != 'u'),
                        _ => any => true,
                    }));
                    return l;
                }
                return ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.NormalizeName)
                    ? "NoModule"
                    : "!NoModule";
            }
        }
        static MethodInfo GetValueTUnsafeT =
            typeof(DynamicReferenceManager).GetMethod("GetValueTUnsafe", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;
        public static void Prettify(this ILContext il)
        {
            if (!ILHookDebuggerModule.PrettifyMonoMod)
            {
                return;
            }
            ILCursor ic = new(il);
            int unique = 0;
            MethodReference method = null!;
            GenericInstanceMethod asgen = null!;
            bool check(Instruction i) =>
                i.MatchCallOrCallvirt(out method!)
                && method is GenericInstanceMethod
                {
                    DeclaringType:
                    {
                        Namespace: "MonoMod.Utils",
                        Name: "DynamicReferenceManager",
                        Scope: AssemblyNameReference { Name: "MonoMod.Utils" }
                    },
                    Name: "GetValueTUnsafe",
                    Parameters.Count: 2,
                    GenericArguments.Count: 1
                } a
                && typeof(int).Is(method.Parameters[0].ParameterType)
                && typeof(int).Is(method.Parameters[1].ParameterType)
                && (asgen = a) == a;
            while (ic.TryGotoNext(MoveType.After, check))
            {
                var bg = ic.Clone();
                int toremove = 3;
                bg.Next = bg.Prev.Previous.Previous;//-=3
                //bg.Next==index
                bool brs(Instruction i) => il.GetIncomingLabels(i).Any();
                if (brs(bg.Next.Next) || brs(ic.Prev))
                {
                    continue;
                }
                MethodReference method2 = null!;
                bool hasInvoke = (ic.Next?.MatchCallOrCallvirt(out method2!) ?? false)
                                    && method2.Parameters.Count >= 1
                                    && method2.IsMMInvoke()
                                    && !brs(ic.Next);
                if (hasInvoke)
                {
                    toremove++;
                }
                int index = (int)bg.Next!.Operand;
                int hash = (int)bg.Next!.Next!.Operand;
                var storedType = asgen.GenericArguments[0];
                var stored = GetValueTUnsafeT.MakeGenericMethod(storedType.ResolveReflection()).Invoke(null, [index, hash]);

                string name;
                if (ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.NormalizeName))
                {
                    name = $"Reference_{unique++}_{stored switch
                    {
                        Delegate d => d.GetInvocationList() switch
                        {
                            [var s] => ModName(s.Method),
                            [] => "_Empty",
                            [var s, ..] => ModName(s.Method) + "_AndMore",
                        },
                        _ => stored?.ToString() ?? "__null",
                    }}";
                }
                else
                {
                    name = $"@{unique++}_{stored switch
                    {
                        Delegate d => d.GetInvocationList() switch
                        {
                            [var s] => ModName(s.Method),
                            [] => "!Empty",
                            [var s, ..] => ModName(s.Method) + "#AndMore",
                        },
                        _ => stored?.ToString() ?? "!!null",
                    }}";
                }
                var md = new MethodDefinition(name, Mono.Cecil.MethodAttributes.Static, storedType);

                var target = new ILCursor(new ILContext(md));
                if (hasInvoke)
                {
                    foreach (var par in method2.Parameters.SkipLast(1))
                    {
                        md.Parameters.Add(par.Clone());
                    }
                    md.ReturnType = method2.ReturnType;
                    if (method2 is GenericInstanceMethod gem)
                    {
                        var resolve = gem.GenericArguments;
                        if (md.ReturnType is GenericParameter gen)
                        {
                            md.ReturnType = resolve[gen.Position];
                        }
                        else
                        {
                            md.ReturnType = method2.ReturnType;
                        }
                        for (int i = 0; i < md.Parameters.Count; i++)
                        {
                            if (md.Parameters[i].ParameterType is GenericParameter gen2)
                            {
                                md.Parameters[i].ParameterType = resolve[gen2.Position];
                            }
                        }
                    }
                    for (int i = 0; i < md.Parameters.Count; i++)
                    {
                        target.EmitLdarg(i);
                    }
                }
                target.EmitLdcI4(index);
                target.EmitLdcI4(hash);
                target.Emit(ic.Prev.OpCode, ic.Prev.Operand);

                if (hasInvoke)
                {
                    target.Emit(ic.Next.OpCode, ic.Next.Operand);
                }

                il.Method.DeclaringType.Methods.Add(md);
                target.EmitRet();
                bg.MoveAfterLabels();
                if (bg.IncomingLabels.Any())
                {
                    toremove += 0;
                }
                bg.EmitCall(md);
                bg.MoveAfterLabels();
                if (bg.IncomingLabels.Any())
                {
                    toremove += 0;
                }
                bg.RemoveRange(toremove);
                ic = bg;
            }

        }
        [GeneratedRegex(@"\AInvoke(Void|Type)(Val|Ref)(1[0-6]|[1-9])\z", RegexOptions.ExplicitCapture)]
        public static partial Regex MatchMMInvoke();
        [GeneratedRegex(@"\A<(?<in>[^>]+)>b__\d+(_\d+)?\z", RegexOptions.ExplicitCapture)]
        public static partial Regex MatchLambda();
        [GeneratedRegex(@"\A<(?<in>[^>]+)>g__(?<name>[^\|]+)\|(?<id>\d+_\d+)\z", RegexOptions.ExplicitCapture)]
        public static partial Regex MatchLocalFunc();

        static bool IsMMInvoke(this MethodReference md)
        {
            if (md.DeclaringType.FullName == "MonoMod.Cil.FastDelegateInvokers"
                //&& md.Module.Assembly.Name.Name == "MonoMod.Utils"
                && MatchMMInvoke().Match(md.Name).Success
                )
            {
                return true;
            }
            if (md.Name.StartsWith(StealDynamicMethod.MMPrefix))
            {
                return true;
            }
            return false;
        }
    }
}
