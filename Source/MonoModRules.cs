using Celeste.Mod;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.InlineRT;
using MonoMod.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoMod
{
    [MonoModCustomAttribute(nameof(MonoModRules.PatchDependency))]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    class PatchDependencyAttribute : Attribute { }

    internal static class MonoModRules
    {
        static void RemoveIf<T>(this Mono.Collections.Generic.Collection<T> self, Func<T, bool> p)
        {
            for (int i = 0; i < self.Count;)
            {
                if (p(self[i]))
                {
                    self.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
        }
        public static void PatchDependency(ICustomAttributeProvider provider, CustomAttribute _)
        {
            var p = (TypeDefinition)provider;
            var r = p.Module.AssemblyReferences;
            void Invoke(string ext, string act)
            {
                string v = $"{act}.NoMap";
                var found = r.FirstOrDefault(x => x.Name == act) ?? r.FirstOrDefault(x => x.Name == v);
                if (found != null)
                {
                    found.Name = act;
                    if (Everest.Loader.TryGetDependency(new() { Name = ext, Version = new(0, 0, 0) }, out var m))
                    {
                        using var stream = Everest.Content.Get($"{ext}:/{act}.dll").Stream;
                        using var re = AssemblyDefinition.ReadAssembly(stream);
                        found.Version = re.Name.Version;
                    }
                    else if (Everest.Loader.TryGetDependency(new() { Name = "MappingUtils", Version = new(1, 0, 0) }, out var result))
                    {
                        var rr = result.Metadata.AssemblyContext.Resolve(found);
                        found.Version = rr.Name.Version;
                    }
                }
            }
            Invoke("ILHookDebuggerExtension_Decompiler", "ICSharpCode.Decompiler");
            Invoke("ILHookDebuggerExtension_TextEditor", "ImGuiColorTextEditNet");

            p.CustomAttributes.RemoveIf(x => x.Constructor.DeclaringType.Name == "PatchDependencyAttribute");
            p.Module.Types.RemoveIf(x => x.Namespace == "MonoMod");
        }
    }
}
