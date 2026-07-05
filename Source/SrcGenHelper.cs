using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using EventAttributes = Mono.Cecil.EventAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using GenericParameterAttributes = Mono.Cecil.GenericParameterAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Celeste.Mod.ILHookDebugger
{
    internal static class SrcGenHelper
    {
        public class StateGroup()
        {
            public CSharpCompilation Com = null!;
            public AssemblyLoadContext alc = null!;
            ~StateGroup()
            {
                alc.Unload();
            }
        }
        public static ConditionalWeakTable<Assembly, object> AllReferenceAssembly = [];
        public static volatile Task<StateGroup>? sg;
        public static CSharpParseOptions csp =
            CSharpParseOptions.Default
                .WithKind(SourceCodeKind.Script)
                .WithLanguageVersion(LanguageVersion.Preview);
        static readonly object lockable = new();
        public static bool Anything = false;
        public static void Refresh()
        {
            sg = null;
        }
        public static void EnsureInitialized()
        {
            if (ILHookDebuggerModule.CheckScript.Value.Item1)
            {
                _EnsureInitialized();
            }
        }
        static void _EnsureInitialized()
        {
            if (sg is { })
            {
                return;
            }
            var o = new Task<StateGroup>(() =>
            {
                var all =
                    AppDomain
                        .CurrentDomain
                        .GetAssemblies()
                        .Reversed()
                        .DistinctBy(z => z.GetName().Name)
                        .ToArray();
                Assembly assembly = typeof(WritingMango.ILRunner.RunnerFactory).Assembly;
                var m =
                    Anything ?
                        all
                        .Where(z => z.Location is null or "")
                        .Where(z => !AllReferenceAssembly.TryGetValue(z, out _))
                        : [assembly]
                        ;
                foreach (var x in m)
                {
                    try
                    {
                        var modname = x.GetName();
                        using ModuleDefinition module = ModuleDefinition.CreateModule(modname.Name, ModuleKind.Dll);
                        module.Assembly.Name.Version = modname.Version;
                        module.Assembly.Name.Culture = modname.CultureName;
                        module.Assembly.Name.PublicKeyToken = modname.GetPublicKeyToken();
                        Dictionary<Type, TypeDefinition> mt = [];
                        Dictionary<Type, GenericParameter> genmap = [];
                        CustomAttribute selector(CustomAttributeData z, IGenericParameterProvider provider)
                        {
                            var c = new CustomAttribute(module.ImportReference(z.Constructor));
                            c.ConstructorArguments.AddRange(z.ConstructorArguments.Select(x =>
                            {
                                return ConvertArgument(x, provider);
                                //var v = x.Value;
                                //if (v is Type t)
                                //{
                                //    v = module.ImportReference(t);
                                //}
                                //return new CustomAttributeArgument(module.ImportReference(x.ArgumentType), x.Value);
                            }));
                            CustomAttributeNamedArgument selector(System.Reflection.CustomAttributeNamedArgument z)
                            {
                                var x = z.TypedValue;
                                //var v = x.Value;
                                //if (v is Type t)
                                //{
                                //    v = module.ImportReference(t);
                                //}
                                return new CustomAttributeNamedArgument(z.MemberName, ConvertArgument(x, provider));
                            }
                            c.Fields.AddRange(z.NamedArguments.Where(x => x.IsField).Select(selector));
                            c.Properties.AddRange(z.NamedArguments.Where(x => !x.IsField).Select(selector));
                            return c;
                        }
                        // ai
                        CustomAttributeArgument ConvertArgument(
                            CustomAttributeTypedArgument reflectionArg,
                            IGenericParameterProvider provider)
                        {
                            Type argumentType = reflectionArg.ArgumentType;
                            var value = reflectionArg.Value;

                            TypeReference typeRef = ImportReference(argumentType);

                            if (argumentType.IsArray && value is IEnumerable<CustomAttributeTypedArgument> arrayElements)
                            {
                                var elementType = argumentType.GetElementType()!;
                                var elementTypeRef = ImportReference(elementType);
                                var arrayValue = arrayElements
                                    .Select(e => ConvertArgument(e, provider))
                                    .ToArray();

                                return new CustomAttributeArgument(typeRef, arrayValue);
                            }

                            if (argumentType.IsEnum)
                            {
                                var underlyingType = Enum.GetUnderlyingType(argumentType);
                                var convertedValue = Convert.ChangeType(value, underlyingType);
                                return new CustomAttributeArgument(typeRef, convertedValue);
                            }
                            if (value is Type t)
                            {
                                value = ImportReference(t);
                            }

                            return new CustomAttributeArgument(typeRef, value);
                        }

                        void ImportGenericParmaetersA(IGenericParameterProvider ct, Type[] genericTypeParameters)
                        {
                            foreach (var i in genericTypeParameters)
                            {
                                GenericParameter item = new(i.Name, ct)
                                {
                                    Attributes = (GenericParameterAttributes)i.Attributes,
                                };
                                genmap[i] = item;
                                ct.GenericParameters.Add(item);
                            }
                        }
                        void ImportGenericParmaetersB(IGenericParameterProvider ct, Type[] genericTypeParameters)
                        {
                            for (int i = 0; i < genericTypeParameters.Length; i++)
                            {
                                var item = ct.GenericParameters[i];
                                var ix = genericTypeParameters[i];
                                attrs(item, ix);
                                item.Constraints.AddRange(ix.GetGenericParameterConstraints().Select(x => new GenericParameterConstraint(ImportReference(x))));
                            }
                        }
                        TypeReference ImportReference(Type t)
                        {
                            if (genmap.TryGetValue(t, out var gg))
                            {
                                return gg;
                            }
                            if (t.IsGenericTypeParameter || t.IsGenericMethodParameter)
                            {
                                throw new NotSupportedException("how");
                            }
                            if (mt.TryGetValue(t, out var dd))
                            {
                                return dd;
                            }
                            if (t.IsByRef)
                            {
                                return ImportReference(t.GetElementType()!).MakeByReferenceType();
                            }
                            if (t.IsSZArray)
                            {
                                return ImportReference(t.GetElementType()!).MakeArrayType();
                            }
                            if (t.IsArray)
                            {
                                var r = t.GetArrayRank();
                                return ImportReference(t.GetElementType()!).MakeArrayType(r);
                            }
                            if (t.IsPointer)
                            {
                                return ImportReference(t.GetElementType()!).MakePointerType();
                            }
                            if (t.IsGenericType)
                            {
                                if (t.IsGenericTypeDefinition)
                                {
                                    return module.ImportReference(t);
                                }
                                else
                                {
                                    var def = t.GetGenericTypeDefinition();
                                    return module.ImportReference(def).MakeGenericInstanceType(t.GetGenericArguments().Select(ImportReference).ToArray());
                                }
                            }
                            return module.ImportReference(t);
                        }

                        Type[] ts = x.GetTypesSafe();
                        void attrs(Mono.Cecil.ICustomAttributeProvider to, MemberInfo from)
                        {
                            foreach (var i in from.CustomAttributes)
                            {
                                to.CustomAttributes.Add(selector(i, (to as IGenericParameterProvider)!));
                            }
                        }
                        foreach (var _t in ts)
                        {
                            var t = (System.Reflection.TypeInfo)_t;
                            TypeDefinition ct = new(t.Namespace, t.Name, (TypeAttributes)t.Attributes);
                            if (t.ContainsGenericParameters)
                            {
                                ImportGenericParmaetersA(ct, t.GenericTypeParameters);
                            }
                            mt.Add(t, ct);
                        }
                        foreach (var _t in ts)
                        {
                            var t = (System.Reflection.TypeInfo)_t;
                            var ct = mt[t];
                            ct.BaseType = ImportReference(t.BaseType ?? typeof(object));
                            if (t.DeclaringType is { } d)
                            {
                                mt[d].NestedTypes.Add(ct);
                            }
                            else
                            {
                                module.Types.Add(ct);
                            }
                            if (t.ContainsGenericParameters)
                            {
                                ImportGenericParmaetersB(ct, t.GenericTypeParameters);
                            }
                            attrs(ct, t);
                            ct.Interfaces.AddRange(t.ImplementedInterfaces.Select(x => new InterfaceImplementation(ImportReference(x))));
                            Dictionary<MethodBase, MethodDefinition> mm = [];
                            foreach (var f in t.GetFields())
                            {
                                FieldDefinition fc = new(f.Name, (FieldAttributes)f.Attributes, ImportReference(f.FieldType));
                                ct.Fields.Add(fc);
                                attrs(fc, f);
                            }
                            foreach (var f in t.GetMethods())
                            {
                                MethodDefinition fc = new(f.Name, (MethodAttributes)f.Attributes, module.TypeSystem.Void);
                                ct.Methods.Add(fc);
                                if (f.ContainsGenericParameters)
                                {
                                    ImportGenericParmaetersA(fc, f.GetGenericArguments());
                                    ImportGenericParmaetersB(fc, f.GetGenericArguments());
                                }
                                fc.ReturnType = ImportReference(f.ReturnType);
                                attrs(fc, f);
                                fc.Parameters.AddRange(f.GetParameters().Select(x => new ParameterDefinition(ImportReference(x.ParameterType)) { Attributes = 0 }));

                                mm[f] = fc;
                            }
                            foreach (var f in t.GetEvents())
                            {
                                if (f.EventHandlerType is not null)
                                {
                                    EventDefinition fc = new(f.Name, (EventAttributes)f.Attributes, ImportReference(f.EventHandlerType));
                                    ct.Events.Add(fc);
                                    attrs(fc, f);
                                    fc.AddMethod = mm.GetValueOrDefaultOrNull(f.AddMethod);
                                    fc.RemoveMethod = mm.GetValueOrDefaultOrNull(f.RemoveMethod);
                                    fc.InvokeMethod = mm.GetValueOrDefaultOrNull(f.RaiseMethod);
                                    fc.OtherMethods.AddRange(f.GetOtherMethods().Select(z => mm.GetValueOrDefaultOrNull(z)));
                                }
                            }
                            foreach (var f in t.GetProperties())
                            {
                                PropertyDefinition fc = new(f.Name, (PropertyAttributes)f.Attributes, ImportReference(f.PropertyType));
                                ct.Properties.Add(fc);
                                attrs(fc, f);
                                fc.GetMethod = mm.GetValueOrDefaultOrNull(f.GetMethod);
                                fc.SetMethod = mm.GetValueOrDefaultOrNull(f.SetMethod);
                                //fc.OtherMethods.AddRange(f.().Select(z => mm.GetValueOrDefaultOrNull(z)));
                            }
                        }

                        using MemoryStream ms = new();
                        module.Write(ms);
                        var ar = ms.ToArray().ToImmutableArray();
                        AllReferenceAssembly.Add(x, ar);
                    }
                    catch { }
                }
                var com = CSharpCompilation.CreateScriptCompilation("MyCompilation",
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                        .WithAllowUnsafe(true)
                        .WithUsings("System", "Celeste", "Monocle", "MonoMod.Utils"),
                    returnType: typeof(object),
                    references: all.Select(asm =>
                    {
                        if (AllReferenceAssembly.TryGetValue(asm, out var v))
                        {
                            return MetadataReference.CreateFromImage((ImmutableArray<byte>)v);
                        }
                        try
                        {
                            if (string.IsNullOrEmpty(asm.Location))
                            {
                                return null!;
                            }
                            return MetadataReference.CreateFromFile(asm.Location);
                        }
                        catch
                        {
                            return null!;
                        }
                    }).Where(x => x is not null));
                var alc = new AssemblyLoadContext("", true);
                alc.Resolving += (arg1, asm) =>
                    {
                        var f = AppDomain.CurrentDomain.GetAssemblies()
                            .Reversed()
                            .FirstOrDefault(x => asm.FullName == x.FullName);
                        return f;
                    };
                return new StateGroup() { Com = com, alc = alc };
            });
            if (Interlocked.CompareExchange(ref sg, o, null) is null)
            {
                o.Start();
            }
        }

        public static Task<StateGroup> GetOrInit()
        {
            do
            {
                if (sg is { } sgx)
                {
                    return sgx;
                }
                EnsureInitialized();
            } while (true);
        }

        public static async Task<(string?, Func<Task<object>>?)> Eval(string expr)
        {
            var state = await GetOrInit();
            using var ms = new MemoryStream();
            var ret =
                state.Com
                .AddSyntaxTrees(SyntaxFactory.ParseSyntaxTree(expr, options: csp))
                .WithAssemblyName("ILHD Evaluation Assembly #" + Interlocked.Increment(ref unique).ToString())
                .Emit(ms);
            if (ret.Success)
            {
                ms.Seek(0, SeekOrigin.Begin);
                var asm = state.alc.LoadFromStream(ms);
                Type? type = asm.GetType("Script")!;
                var sc = type.GetConstructor([typeof(object[])])!.Invoke([(object?[])[null, null]]);
                var d = type.GetMethod("<Initialize>")!.CreateDelegate<Func<Task<object>>>(sc);
                GC.KeepAlive(state);
                return (null, d);
            }
            else
            {
                var r = string.Join("\n", ret.Diagnostics.Select(x => x.ToString()));
                return (r, null);
            }
        }
        static int unique = 0;
    }
    static partial class Helpery
    {
        public static TValue? GetValueOrDefaultOrNull<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey? key)
        {
            if (key == null)
            {
                return default;
            }
            return dictionary.GetValueOrDefault(key);
        }
    }
}
