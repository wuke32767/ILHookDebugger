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
            var maybe = AppDomain.CurrentDomain.GetAssemblies().Reversed().FirstOrDefault(x => x.GetName().Name == reference.Name);
            if (maybe is null)
            {
                return null;
            }
            if (AssemblyLoadContext.GetLoadContext(maybe) is not EverestModuleAssemblyContext context)
            {
                var dll = Path.Combine(Everest.PathGame, reference.Name + ".dll");
                return LoadFile(dll);
            }
            var mod = context.ModuleMeta.Name;

            return LoadFile(Path.Combine(Everest.Loader.PathCache, $"{mod}.{reference.Name}.dll"));

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
            var (output, name, _) = PrintingPod.Operate(target, il, true, ILHookDebuggerModule.ILSpyFeature);
            return Final(output, name);
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

        static (SyntaxTree, CSharpDecompiler) Final(Stream output, string name)
        {
            var decompiler = new CSharpDecompiler(new PEFile("NONAMELOL", output), ILHookDebuggerModule.Settings.UseDecompileResolver ? new DecompilerResolver() : new NullResolver(), new DecompilerSettings());

            var found = decompiler.TypeSystem.MainModule.TypeDefinitions.First(x => x.Name == name);
            return (decompiler.DecompileType(new(found.FullName)), decompiler);
        }
    }
}
