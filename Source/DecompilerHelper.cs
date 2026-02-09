using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Metadata;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    internal class NullResolver : IAssemblyResolver
    {
        public MetadataFile? Resolve(IAssemblyReference reference)
        {
            return null;
        }

        public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference)
        {
            return Task.FromResult<MetadataFile?>(null);
        }

        public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName)
        {
            return null;
        }

        public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName)
        {
            return Task.FromResult<MetadataFile?>(null);
        }
    }
    internal class DecompilerResolver : IAssemblyResolver
    {
        public MetadataFile? Resolve(IAssemblyReference reference)
        {
            string name = reference.Name;
            return Resolve(name);

        }
        public static MetadataFile? Resolve(string? name)
        {
            var maybe = AppDomain.CurrentDomain.GetAssemblies().Reversed().FirstOrDefault(x =>
            {
                return x.GetName().Name == name;
            });
            if (maybe is null)
            {
                return null;
            }
            if (AssemblyLoadContext.GetLoadContext(maybe) is not EverestModuleAssemblyContext context)
            {
                var p = name + ".dll";
                return LoadFile(Path.Combine(Everest.PathGame, p)) ?? LoadFile(maybe.Location);
            }
            var mod = context.ModuleMeta.Name;

            return LoadFile(Path.Combine(Everest.Loader.PathCache, $"{mod}.{name}.dll"));

            static MetadataFile? LoadFile(string dll)
            {
                if (File.Exists(dll))
                {
                    return new PEFile(dll, PEStreamOptions.PrefetchEntireImage);
                }
                else
                {
                    return null;
                }
            }
        }

        public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference)
        {
            return Task.Run(() => Resolve(reference));
        }

        public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName)
        {
            return null;
        }

        public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName)
        {
            return Task.FromResult<MetadataFile?>(null);
        }
    }
    internal static class Decompilation
    {
        internal static (SyntaxTree, CSharpDecompiler) NonHook(MethodBase target)
        {
            var f = DecompilerResolver.Resolve(target.Module.Assembly.GetName().Name);
            var decompiler = new CSharpDecompiler(f,
                ILHookDebuggerModule.Settings.UseDecompileResolver ? new DecompilerResolver() : new NullResolver(),
                new DecompilerSettings() { UseLambdaSyntax = true, });

            var t = target.DeclaringType;

            bool IsSame(Type? t, ICSharpCode.Decompiler.TypeSystem.ITypeDefinition? tx)
            {
                if (t == null || tx == null)
                {
                    if (t == null && tx == null)
                    {
                        return true;
                    }
                    return false;
                }
                if (t.Name != tx.Name)
                {
                    return false;
                }
                return IsSame(t.DeclaringType, tx.DeclaringTypeDefinition)
                    && t.IsNested == (tx.DeclaringTypeDefinition is { })
                    && (!t.IsNested || t.Namespace == tx.Namespace);
            }
            var found = decompiler.TypeSystem.MainModule.TypeDefinitions.First(x => IsSame(t, x));
            var o = target.GetParameters().Length;

            return (decompiler.Decompile(found.Methods.Where(x => x.Name == target.Name && x.Parameters.Count == o).Select(x => x.MetadataToken)), decompiler);
        }
        internal static (SyntaxTree, CSharpDecompiler) FromMethod(MethodBase target)
        {
            using DynamicMethodDefinition dmd = new(target);

            var hooked = DetourManager.GetDetourInfo(target).ILHooks;

            using var il = new ILContext(dmd.Definition);

            foreach (var hook in hooked)
            {
                var manip = DynamicData.For(DynamicData.For(hook).Get("hook")!).Get<ILContext.Manipulator>("Manip")!;
                if (manip.Method.DeclaringType?.Assembly != typeof(Decompilation).Assembly)
                {
                    il.Invoke(manip);
                }
            }
            using var t = PrintingPod.Operate(target, il, ILHookDebuggerModule.ILSpyFeature);
            return Final(t.output, t.name);
        }

        internal static (SyntaxTree, CSharpDecompiler) FromRunning(Duplicant target)
        {
            if (target.Asm is not { } output || target.TypeName is not { } name)
            {
                throw new NullReferenceException("hook is not prepared. just try again.");
            }
            output.Position = 0;
            return Final(output, name);
        }

        internal static (SyntaxTree, CSharpDecompiler) Final(Stream output, string name)
        {
            var decompiler = new CSharpDecompiler(new PEFile("NONAMELOL", output),
                ILHookDebuggerModule.Settings.UseDecompileResolver ? new DecompilerResolver() : new NullResolver(),
                new DecompilerSettings() { UseLambdaSyntax = true, });

            var found = decompiler.TypeSystem.MainModule.TypeDefinitions.First(x => x.Name == name);
            return (decompiler.DecompileType(found.FullTypeName), decompiler);
        }
    }
}
