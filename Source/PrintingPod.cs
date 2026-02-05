using Celeste.Mod.Helpers.LegacyMonoMod;
using Celeste.Mod.ILHookDebugger.MappingUtils;
using ICSharpCode.Decompiler.IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Data;
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
    class Transform
    {
        internal virtual void Run(ILContext il, IDEFeatures feat) { }
        internal virtual void AfterLoaded(Type r, IDEFeatures feat) { }
        internal virtual void Dispose(IDEFeatures feat) { }
    }
    class SomehowRelinker : Transform
    {
        internal override void Run(ILContext il, IDEFeatures feat)
        {

            var md = il.Method;
            var mdm = md.Module;
            ModuleDefinition? foundModule = null;
            Relinker findClone = (mtp, ctx) =>
            {
                foundModule ??= mtp switch
                {
                    MemberReference mr => mr.Module,
                    _ => null,
                };
                if (foundModule == mdm)
                {
                    foundModule = null;
                }
                return mtp;
            };
            Relinker relink = (mtp, ctx) =>
            {
                //if (mtp is MethodReference mr)
                {
                    if (mtp == md
                    //|| (mr.FullName == md.FullName
                    //    && mr.DeclaringType.FullName == md.DeclaringType.FullName
                    //    && mr.DeclaringType.Scope.Name == md.DeclaringType.Scope.Name)
                    //dmd dynamic method backend won't check this, only cecil backend checks
                    )
                        return md!;
                }
                return mdm.ImportReference(mtp);
            };
            PrintingPod.RunRelinker(findClone, md);
            if (foundModule is not null)
            {
                mdm.AssemblyReferences.AddRange(foundModule.AssemblyReferences);
                PrintingPod.RunRelinker(relink, md);
            }
        }
    }
    class Breaker : Transform
    {
        static Type StrongBoxType = typeof(StrongBox<bool>);
        static FieldInfo StrongBoxValue = StrongBoxType.GetField(nameof(StrongBox<bool>.Value))!;

        FieldDefinition shouldBreak = null!;
        internal override void Run(ILContext il, IDEFeatures feat)
        {
            var md = il.Method;
            var dmdtype = md.DeclaringType;
            var mdm = md.Module;

            ILCursor ic = new(il);
            if (!feat.HasFlag(IDEFeatures.NotRun))
            {
                var boxed = feat.HasFlag(IDEFeatures.CanOnlyModifyRefValues);
                shouldBreak = new("ShouldNotBreak_YouCanChangeThisFromYourIDEDebugger",
                        Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Public, mdm.TypeSystem.Boolean);
                if (boxed)
                {
                    shouldBreak.FieldType = mdm.ImportReference(StrongBoxType);
                }
                dmdtype.Fields.Add(shouldBreak);


                var breaking = il.DefineLabel();
                ic.EmitLdsfld(shouldBreak);
                if (boxed)
                {
                    ic.EmitLdfld(StrongBoxValue);
                }
                ic.EmitBrtrue(breaking);
                if (ILHookDebuggerModule.BreakOnce || feat.HasFlag(IDEFeatures.CanNotModifyValues))
                {
                    if (boxed)
                    {
                        ic.EmitLdsfld(shouldBreak);
                    }
                    ic.EmitCall(typeof(Debugger).GetProperty("IsAttached")!.GetGetMethod()!);
                    if (boxed)
                    {
                        ic.EmitStfld(StrongBoxValue);
                    }
                    else
                    {
                        ic.EmitStsfld(shouldBreak);
                    }
                    shouldBreak.Name = "ShouldNotBreak";
                }
                ic.EmitDelegate(Debugger.Break);
                ic.MarkLabel(breaking);
            }
        }
        internal override void AfterLoaded(Type r, IDEFeatures feat)
        {
            var boxed = feat.HasFlag(IDEFeatures.CanOnlyModifyRefValues);
            if (boxed)
            {
                var remotebox = r.GetField(shouldBreak.Name)!;
                remotebox.SetValue(null, new StrongBox<bool>(false));
            }
        }
    }
    internal class TypeAttr() : Transform()
    {
        MethodReference? ext;
        internal MethodReference MakeExtension(ILContext il)
        {
            if (ext is null)
            {
                ext = il.Import(typeof(ExtensionAttribute).GetConstructor([])!);
                TypeDefinition declaringType = il.Method.DeclaringType;
                declaringType.CustomAttributes.Add(new(ext));
                declaringType.Attributes |= Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.Abstract;
            }
            return ext;
        }
    }
    class Backup : Transform
    {
        ILContext il = null!;
        Instruction[] backup = null!;
        Instruction?[] lackup = null!;
        internal Dictionary<Instruction, object> restore = [];
        internal override void Run(ILContext _il, IDEFeatures feat)
        {
            il = _il;

            // do not change the value of any instrs, or the backup can be broken
            backup = il.Instrs.ToArray();
            // also backup the labels so that i can use moveafterlabels
            lackup = il.Labels.Select(x => x.Target).ToArray();
        }
        internal override void AfterLoaded(Type r, IDEFeatures feat)
        {
            il.Instrs.Clear();

            foreach (var (o, b) in il.Labels.Zip(lackup))
            {
                o.Target = b;
            }
            il.Instrs.AddRange(backup);
            foreach (var (k, v) in restore)
            {
                k.Operand = v;
            }
        }
    }

    class Cleanup(MethodBase mi, Backup b) : Transform
    {
        internal override void Run(ILContext il, IDEFeatures feat)
        {
            int unique = System.Threading.Interlocked.Increment(ref PrintingPod.unique);
            var md = il.Method;
            var dmdtype = md.DeclaringType;
            var mdm = md.Module;
            var asm = mdm.Assembly;
            var _iact = mdm.ImportReference(typeof(IgnoresAccessChecksToAttribute)).Resolve();
            var iact = mdm.ImportReference(_iact.GetConstructors().First());

            md.Name = mi.Name;
            dmdtype.BaseType = mdm.TypeSystem.Object;
            dmdtype.Namespace = mi.DeclaringType?.Namespace;

            dmdtype.Name = $"{nameof(ILHookDebugger)}#Type#{unique}#{mi.DeclaringType?.Name ?? "<Module>"}".Simplify(feat);
            mdm.Name = $"{nameof(ILHookDebugger)}#Module#{unique}".Simplify(feat);
            asm.Name.Name = $"{nameof(ILHookDebugger)}#Asm#{unique}".Simplify(feat);

            var debuggable = typeof(DebuggableAttribute).GetConstructor([typeof(bool), typeof(bool)]);
            var dattr = new CustomAttribute(il.Import(debuggable!));
            dattr.ConstructorArguments.Add(new(mdm.TypeSystem.Boolean, true));
            dattr.ConstructorArguments.Add(new(mdm.TypeSystem.Boolean, true));
            asm.CustomAttributes.Add(dattr);
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
                    b.restore.Add(instr, label);
                    instr.Operand = label.Target;
                }
                else if (instr.Operand is ILLabel[] targets)
                {
                    b.restore.Add(instr, targets);
                    instr.Operand = targets.Select(l => l.Target).ToArray();
                }
                //if (mod is not null)
                //{
                //    checks.Add(mod.Name.Name);
                //}
            }
            //var hooked = DetourManager.GetDetourInfo(mi).ILHooks;
            md.FixShortLongOps();
            //foreach (var s in checks)

            foreach (var _s in mdm.AssemblyReferences)
            {
                var s = _s.Name;
                var attr = new CustomAttribute(iact);
                attr.ConstructorArguments.Add(new(mdm.TypeSystem.String, s));
                asm.CustomAttributes.Add(attr);
            }
        }
    }

    static class Backport
    {
        public static IEnumerable<T> Reversed<T>(this IEnumerable<T> self) => self.Reverse();
    }
    record class Duplicant(ILHook Detour, AssemblyLoadContext Context, MethodBase Target) : IDisposable
    {
        public ILHook? Helper;
        public Stream? Asm;
        public string? TypeName;
        public TransformingResult? Transforming;
        public void Dispose()
        {
            Detour?.Dispose();
            Context?.Unload();
            Asm?.Dispose();
            Helper?.Dispose();
            Transforming?.Dispose();
        }
    }
    internal static class PrintingPod
    {
        internal static void RunRelinker(Relinker relinker, MethodDefinition md)
        {
            var def = md;
            var clone = md;
            foreach (var param in def.Parameters)
            {
                param.ParameterType = param.ParameterType.Relink(relinker, clone);
            }

            clone.ReturnType = def.ReturnType.Relink(relinker, clone);

            var body = def.Body;

            foreach (var var in clone.Body.Variables)
            {
                var.VariableType = var.VariableType.Relink(relinker, clone);
            }

            foreach (var handler in clone.Body.ExceptionHandlers)
            {
                if (handler.CatchType != null)
                    handler.CatchType = handler.CatchType.Relink(relinker, clone);
            }

            foreach (var instr in body.Instructions)
            {
                var operand = instr.Operand;

                // Import references.
                if (operand is IMetadataTokenProvider mtp && operand is not ParameterDefinition)
                {
                    operand = mtp.Relink(relinker, clone);
                }

                instr.Operand = operand;
            }
        }

        internal class Guardian(string toRelease) : IDisposable
        {
            internal static void Clear()
            {
                GC.Collect();
                var old = System.Threading.Interlocked.Exchange(ref table, []);
                foreach (var (_, g) in old)
                {
                    g.Dispose();
                }
                old.Clear();
            }
            void Delete()
            {
                try
                {
                    File.Delete(toRelease);
                }
                catch
                {
                    GC.Collect();
                    failed.Add(toRelease);
                    TryReClear();
                }
            }
            internal void TryReClear()
            {
                try
                {
                    for (int i1 = 0; i1 < failed.Count; i1++)
                    {
                        string? i = failed[i1];
                        if (i is { })
                        {
                            File.Delete(toRelease);
                        }
                        failed[i1] = null;
                    }
                }
                catch
                {
                }
                failed.Reverse();
                while (failed.Count > 0 && failed[^1] is null)
                {
                    failed.RemoveAt(failed.Count - 1);
                }
            }
            internal static List<string?> failed = [];
            internal static ConditionalWeakTable<Assembly, Guardian> table = new();

            internal static void Update(Assembly assembly, string toRelease)
            {
                table.AddOrUpdate(assembly, new(toRelease));
            }

            private bool disposedValue;
            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                    }

                    Delete();
                    disposedValue = true;
                }
            }

            ~Guardian()
            {
                Dispose(disposing: false);
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }
        public static List<Duplicant> AllDuplicants = [];
        public static Dictionary<MethodBase, Duplicant> DuplicantLookup = [];
        internal static int unique;
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
                    .Reversed()
                    .FirstOrDefault(x => asm.FullName == x.FullName);
                return f;

            };
            Duplicant duplicant = null!;
            var hook = new ILHook(mi, il =>
            {
                ILCursor ic = new(il);
                var md = il.Method;
                var op = Operate(mi, il);
                var output = op.output;
                duplicant.Asm?.Dispose();

                Assembly _tmp;
                if (ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.RequiresFileAssembly))
                {
                    var path = Path.Combine(ILHookDebuggerModule.CachePath, $"{nameof(ILHookDebugger)}#Asm#{System.Threading.Interlocked.Increment(ref PrintingPod.unique)}".Simplify() + ".dll");
                    Directory.CreateDirectory(ILHookDebuggerModule.CachePath);
                    using (var dst = File.OpenWrite(path))
                    {
                        output.CopyTo(dst);
                    }
                    _tmp = context.LoadFromAssemblyPath(path);
                    Guardian.Update(_tmp, path);

                    duplicant.Asm = File.OpenRead(path);
                }
                else
                {
                    duplicant.Asm = output;
                    _tmp = context.LoadFromStream(output);
                }
                var dup = op.Post(_tmp);
                if (duplicant.Transforming is { } old)
                {
                    old.Dispose();
                }
                duplicant.Transforming = op;
                duplicant.TypeName = dup.DeclaringType?.Name;
                for (var i = 0; i < md.Parameters.Count; i++)
                {
                    ic.EmitLdarg(i);
                }
                ic.EmitCall(dup);
                ic.EmitRet();
            }, false);
            duplicant = new(hook, context, mi);
            hook.Apply();
            AllDuplicants.Add(duplicant);
            DuplicantLookup.Add(mi, AllDuplicants[^1]);
            //scope.Dispose();
        }
        internal static List<Transform> Process(MethodBase mi, ILContext il, IDEFeatures feat)
        {
            ILCursor ic = new(il);
            
            var b = new Backup();
            var tacache = new TypeAttr();
            List<Transform> tr = [
                b,
                new SomehowRelinker(),
                new Breaker(),

                new StealDynamicMethod(),
                new MonoModPrettify(),
                new StealCompilerGenerated(tacache),

                new Cleanup(mi, b),
                ];
            foreach (var t in tr)
            {
                t.Run(il, feat);
            }
            return tr;
        }
        internal static TransformingResult Operate(MethodBase mi, ILContext il, IDEFeatures? feature = null)
        {
            var feat = feature ?? ILHookDebuggerModule.CurrentFeature;
            var tr = Process(mi, il, feat);
            var md = il.Method;
            var dmdtype = md.DeclaringType;
            var mdm = md.Module;
            var asm = mdm.Assembly;

            MemoryStream output = new();
            asm.Write(output);
            output.Seek(0, SeekOrigin.Begin);

            return new(output, dmdtype.Name, md.Name, tr, feat);
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
            foreach (var item in AllDuplicants.Select(x => x.Detour))
            {
                item.Undo();
                item.Apply();
            }
        }

        internal static void Refresh(Duplicant at)
        {
            var item = at.Detour;
            item.Undo();
            item.Apply();
        }
        internal static void Refresh(MethodBase at)
        {
            if (DuplicantLookup.TryGetValue(at, out var i))
            {
                Refresh(i);
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

        internal static void Dump(string dir, bool overwrite)
        {
            dir = Path.GetFullPath(dir);
            Directory.CreateDirectory(dir);
            foreach (var dup in AllDuplicants)
            {
                var path = Path.Combine(dir, dup.Target.GetMethodNameForFileName());
                var t = path + ".dll";
                if (!overwrite)
                {
                    int c = 0;
                    while (File.Exists(t))
                    {
                        t = string.Concat(path, "(", c++.ToString(), ").dll");
                    }
                }
                path = t;
                using var file = File.Create(path);
                var stream = dup.Asm!;
                stream.Seek(0, SeekOrigin.Begin);
                stream.CopyTo(file);
            }
        }
    }

    internal record struct TransformingResult(MemoryStream output, string name, string methodname, List<Transform> Transforms, IDEFeatures feat) : IDisposable
    {
        public readonly MethodInfo Post(Assembly _tmp)
        {
            var n = name;
            Type typs = _tmp.GetTypes().First(x => x.Name == n);
            foreach (var item in Transforms)
            {
                item.AfterLoaded(typs, feat);
            }
            var dup = typs.GetMethod(methodname)!;
            return dup;
        }
        public readonly void Dispose()
        {
            foreach (var item in Transforms)
            {
                item.Dispose(feat);
            }
        }
    }
}
