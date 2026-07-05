using Celeste.Mod.Helpers.LegacyMonoMod;
using Celeste.Mod.ILHookDebugger.MappingUtils;
using ICSharpCode.Decompiler.IL;
using Microsoft.Xna.Framework;
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
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    class Transform
    {
        internal virtual void Run(ILContext il, IDEFeatures feat) { }
        internal virtual void AfterLoaded(Type r, IDEFeatures feat) { }
        internal virtual void Dispose(IDEFeatures feat) { }
        internal virtual void Remap(List<SimpleRange?> tokens) { }
    }
    class Mergeform : Transform
    {
        internal record struct Merge(SimpleRange from, int len);
        internal List<Merge> Merges = [];

        internal override void Remap(List<SimpleRange?> tokens)
        {
            Dictionary<(string, int), (string, int)> mapperSt = [];
            Dictionary<(string, int), (string, int)> mapperEd = [];
            foreach (ref var _j in CollectionsMarshal.AsSpan(tokens))
            {
                if (_j is { } j)
                {
                    {
                        var ins = (j.func!, j.from);
                        var (m, a) = ins;
                        if (mapperSt.TryGetValue(ins, out var o))
                        {
                            (m, a) = o;
                            goto then;
                        }
                        foreach (var ix in Merges.Reversed())
                        {
                            var f = ix.from;
                            if (f.func == m && f.from <= a && a < f.from + ix.len)
                            {
                                var of = a - f.from;
                                a = f.from;
                            }
                            else if (f.from + ix.len <= a)
                            {
                                a -= ix.len;
                                a += (f.to - f.from);
                            }
                        }
                        mapperSt[ins] = (m, a);
                    then:
                        j = (new(m, a, j.to));
                    }
                    {
                        var ins = (j.func!, j.to);
                        var (m, a) = ins;
                        if (mapperSt.TryGetValue(ins, out var o))
                        {
                            (m, a) = o;
                            goto done;
                        }
                        foreach (var ix in Merges.Reversed())
                        {
                            var f = ix.from;
                            if (f.func == m && f.from < a && a <= f.from + ix.len)
                            {
                                var of = a - f.from;
                                a = f.to;
                            }
                            else if (f.from + ix.len < a)
                            {
                                a -= ix.len;
                                a += (f.to - f.from);
                            }
                        }
                        mapperSt[ins] = (m, a);
                    done:
                        j = new(m, j.from, a);
                    }
                    _j = j;
                }
                //var m

            }
        }
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
                ic.EmitBreak();
                ic.MarkLabel(breaking);
            }
        }
        internal override void AfterLoaded(Type r, IDEFeatures feat)
        {
            if (feat.HasFlag(IDEFeatures.NotRun))
            {
                return;
            }
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
    class Backup(MethodBase mi) : Transform
    {
        ILContext il = null!;
        Instruction[] backup = null!;
        Instruction?[] lackup = null!;
        internal Dictionary<Instruction, object> restore = [];
        string name = null!;
        VariableDefinition[] vars = null!;
        internal override void Run(ILContext _il, IDEFeatures feat)
        {
            int unique = System.Threading.Interlocked.Increment(ref PrintingPod.unique);
            il = _il;
            name = il.Method.Name;
            var dmdtype = il.Method.DeclaringType;
            dmdtype.Name = $"{nameof(ILHookDebugger)}#Type#{unique}#{mi.DeclaringType?.Name ?? "<Module>"}".Simplify(feat);
            il.Method.Name = mi.Name;

            // do not change the value of any instrs, or the backup can be broken
            backup = il.Instrs.ToArray();
            // also backup the labels so that i can use moveafterlabels
            lackup = il.Labels.Select(x => x.Target).ToArray();
            vars = il.Body.Variables.ToArray();
        }
        internal override void AfterLoaded(Type r, IDEFeatures feat)
        {
            il.Instrs.Clear();
            il.Method.Name = name;
            il.Body.Variables.Clear();
            il.Body.Variables.AddRange(vars);
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
        static Type iacttype = typeof(DynamicReferenceManager).Assembly.GetType("System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute")!;
        internal override void Run(ILContext il, IDEFeatures feat)
        {
            int unique = System.Threading.Interlocked.Increment(ref PrintingPod.unique);
            var md = il.Method;
            var dmdtype = md.DeclaringType;
            var mdm = md.Module;
            var asm = mdm.Assembly;
            var _iact = mdm.ImportReference(iacttype).Resolve();
            var iact = mdm.ImportReference(_iact.GetConstructors().First());

            //md.Name = mi.Name;
            dmdtype.BaseType = mdm.TypeSystem.Object;
            dmdtype.Namespace = mi.DeclaringType?.Namespace;

            mdm.Name = $"{nameof(ILHookDebugger)}#Module#{unique}".Simplify(feat);
            asm.Name.Name = $"{nameof(ILHookDebugger)}#Asm#{unique}".Simplify(feat);

            var debuggable = typeof(DebuggableAttribute).GetConstructor([typeof(bool), typeof(bool)]);
            var dattr = new CustomAttribute(il.Import(debuggable!));
            dattr.ConstructorArguments.Add(new(mdm.TypeSystem.Boolean, true));
            dattr.ConstructorArguments.Add(new(mdm.TypeSystem.Boolean, true));
            asm.CustomAttributes.Add(dattr);

            var targetf = typeof(System.Runtime.Versioning.TargetFrameworkAttribute).GetConstructor([typeof(string)])!;
            var tattr = new CustomAttribute(il.Import(targetf));
            tattr.ConstructorArguments.Add(new(mdm.TypeSystem.String, ".NETCoreApp,Version=v8.0"));
            tattr.Properties.Add(new("FrameworkDisplayName", new(mdm.TypeSystem.String, ".NET 8.0")));
            asm.CustomAttributes.Add(tattr);
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
        public DataScope<DynamicReferenceCell> dataScope;
        public Mirror? nirror;

        public void Dispose()
        {
            Detour?.Dispose();
            Context?.Unload();
            Asm?.Dispose();
            Helper?.Dispose();
            Transforming?.Dispose();
            dataScope.Dispose();
            dataScope = default;
            nirror?.Dispose();
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
                if (operand is IMetadataTokenProvider mtp && operand is not ParameterDefinition and not DynamicMethodReference and not System.Reflection.Emit.DynamicMethod)
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
                            File.Delete(i);
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
                if (ILHookDebuggerModule.CurrentFeature.HasFlag(IDEFeatures.UseCeleste))
                {
                    var m = Mirror.GetOrUpdate(il, mi, ILHookDebuggerModule.CurrentFeature);

                    duplicant.TypeName = mi.GetMethodNameForDB();
                    duplicant.nirror = m;
                    duplicant.dataScope.Dispose();
                    duplicant.dataScope = ic.EmitNewTypedReference(m, out _);
                    var label = il.DefineLabel();
                    ic.EmitLdfld(Mirror.ReflShouldBreak);
                    ic.EmitBrfalse(label);
                    for (int i = 0; i < il.Method.Parameters.Count; i++)
                    {
                        ic.EmitLdarg(i);
                    }
                    ic.EmitCall(m.Entry);
                    ic.EmitRet();
                    ic.EmitNop();
                    ic.MarkLabel(label);

                    return;
                }
                // else
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

            var b = new Backup(mi);
            var tacache = new TypeAttr();
            List<Transform> tr = [
                b,
                new SomehowRelinker(),

                new StealDynamicMethod(),
                new MonoModPrettify(),
                new StealCompilerGenerated(tacache),
                new MakeIEnumerator(),

                new Breaker(),

                new DamnCacheFixer(),
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
            SrcGenHelper.Refresh();
            var o = Clear();
            foreach (var i in o)
            {
                Create(i.Target);
            }
        }

        internal static void FastTrack(Duplicant at)
        {
            var item = at.Detour;
            item.Undo();
            item.Apply();
        }
        internal static void FastTrack(MethodBase at)
        {
            if (DuplicantLookup.TryGetValue(at, out var i))
            {
                FastTrack(i);
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

    internal partial class MakeIEnumerator : Mergeform
    {
        [GeneratedRegex(@"\A<(?<name1>[^>]*)>[0-9]+__(?<name2>.*)\z", RegexOptions.ExplicitCapture)]
        public static partial Regex MatchParamName();

        string? orig;
        internal override void Run(ILContext il, IDEFeatures feat)
        {
            if (!ILHookDebuggerModule.Settings.IEnumeratorPatch)
            {
                return;
            }
            var md = il.Method;
            var m = md.Module;
            if (md.Parameters.Count is > 1 or 0)
            {
                return;
            }
            var at = md.Parameters[0].ParameterType;
            var atf = at.ResolveReflection();
            if (!atf.IsAssignableTo(typeof(System.Collections.IEnumerator)))
            {
                return;
            }
            const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var state = Resolve(x => { x.MatchStfld(out var f); return f; }, atf.GetConstructor(bf, [typeof(int)])!);
            MethodInfo? refcur = atf.GetMethod("get_Current", bf) ?? atf.GetMethod("System.Collections.IEnumerator.get_Current", bf);
            var cur = Resolve(x => { x.MatchLdfld(out var f); return f; }, refcur);
            if (state is null || cur is null)
            {
                return;
            }

            var newm = md.Clone();
            foreach (var c in newm.Body.Instructions)
            {
                if (c.Operand is ILLabel target)
                {
                    c.Operand = newm.Body.Instructions[md.Body.Instructions.IndexOf(target.Target)];
                }
                else if (c.Operand is ILLabel[] targets)
                {
                    c.Operand = targets.Select(i => newm.Body.Instructions[md.Body.Instructions.IndexOf(i.Target)]).ToArray();
                }
            }

            newm.DeclaringType = null;
            var ppt = new TypeDefinition("", "RemoveNext", Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.NestedPrivate, m.TypeSystem.Object);
            var pt = new TypeDefinition("", "<RemoveNext>d__114514", Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.NestedAssembly, m.TypeSystem.Object);
            ppt.NestedTypes.Add(pt);
            pt.CustomAttributes.Add(new(m.ImportReference(typeof(CompilerGeneratedAttribute).GetConstructor([]))));
            pt.Methods.Add(newm);
            newm.Name = "MoveNext";
            newm.Attributes = Mono.Cecil.MethodAttributes.Final | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.NewSlot;
            newm.Parameters.Clear();

            FieldInfo[] @is = atf.GetFields(bf);
            foreach (var i in @is)
            {
                pt.Fields.Add(new(i.Name, Mono.Cecil.FieldAttributes.Assembly, m.ImportReference(i.FieldType)));
            }

            var pstate = pt.Fields.First(x => x.Name == state.Name);
            var pcur = pt.Fields.First(x => x.Name == cur.Name);

            md.DeclaringType.NestedTypes.Add(ppt);

            var raw = new FieldDefinition("RAW", Mono.Cecil.FieldAttributes.Assembly, at);
            pt.Fields.Add(raw);
            var refien = typeof(System.Collections.IEnumerator);
            var refgen = typeof(IEnumerator<>).MakeGenericType(refcur!.ReturnType);
            var refdis = typeof(IDisposable);
            var ien = m.ImportReference(refien);
            var gen = m.ImportReference(refgen);
            var dis = m.ImportReference(refdis);
            pt.Interfaces.Add(new(ien));
            pt.Interfaces.Add(new(gen));
            pt.Interfaces.Add(new(dis));
            {
                var res = new MethodDefinition("System.Collections.IEnumerator.Reset",
                    Mono.Cecil.MethodAttributes.Final | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.NewSlot,
                    m.TypeSystem.Void);
                pt.Methods.Add(res);
                var p = res.Body.GetILProcessor();
                p.Emit(OpCodes.Ret);
                res.Overrides.Add(m.ImportReference(refien.GetMethod("Reset")));
            }
            {
                // for ilspy
                var res = new MethodDefinition("System.IDisposable.Dispose",
                    Mono.Cecil.MethodAttributes.Final | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.NewSlot,
                    m.TypeSystem.Void);
                pt.Methods.Add(res);
                var p = res.Body.GetILProcessor();
                p.Emit(OpCodes.Ret);
                res.Overrides.Add(m.ImportReference(refdis.GetMethod("Dispose")));
            }
            var ctor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.RTSpecialName | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.ReuseSlot,
                m.TypeSystem.Void);
            {
                ctor.Parameters.Add(new(m.TypeSystem.Int32));
                pt.Methods.Add(ctor);
                // for ilspy
                var proc = ctor.Body.GetILProcessor();
                proc.Emit(OpCodes.Ldarg_0);
                proc.Emit(OpCodes.Ldarg_1);
                proc.Emit(OpCodes.Stfld, pstate);
                proc.Emit(OpCodes.Ret);
            }
            {
                var gc = new MethodDefinition("System.Collections.Generic.IEnumerator<something>.get_Current",
                    Mono.Cecil.MethodAttributes.SpecialName |
                    Mono.Cecil.MethodAttributes.Final | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.NewSlot,
                    pcur.FieldType);
                pt.Methods.Add(gc);
                // for ilspy
                var p = gc.Body.GetILProcessor();
                p.Emit(OpCodes.Ldarg_0);
                p.Emit(OpCodes.Ldfld, pcur);
                p.Emit(OpCodes.Ret);
                gc.Overrides.Add(m.ImportReference(refgen.GetMethod("get_Current")));

                var current = new PropertyDefinition("System.Collections.Generic.IEnumerator<something>.Current", Mono.Cecil.PropertyAttributes.None, pcur.FieldType);
                current.GetMethod = gc;
                pt.Properties.Add(current);
            }
            {
                var gc = new MethodDefinition("System.Collections.IEnumerator.get_Current",
                    Mono.Cecil.MethodAttributes.SpecialName |
                    Mono.Cecil.MethodAttributes.Final | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.NewSlot,
                    m.TypeSystem.Object);
                pt.Methods.Add(gc);
                // for ilspy
                // unreachable
                var p = gc.Body.GetILProcessor();
                p.Emit(OpCodes.Ldarg_0);
                p.Emit(OpCodes.Ldfld, pcur);
                p.Emit(OpCodes.Ret);
                gc.Overrides.Add(m.ImportReference(refien.GetMethod("get_Current")));

                var current = new PropertyDefinition("System.Collections.IEnumerator.Current", Mono.Cecil.PropertyAttributes.None, m.TypeSystem.Object);
                current.GetMethod = gc;
                pt.Properties.Add(current);
            }
            {
                var stub = new MethodDefinition("ILHookDebuggerStateMachineStub", Mono.Cecil.MethodAttributes.Static, ien);
                ppt.Methods.Add(stub);
                var atttr = new CustomAttribute(m.ImportReference(typeof(IteratorStateMachineAttribute).GetConstructor([typeof(Type)])));
                atttr.ConstructorArguments.Add(new(m.ImportReference(typeof(Type)), pt));
                stub.CustomAttributes.Add(atttr);
                var p = stub.Body.GetILProcessor();
                var self = new VariableDefinition(pt);
                stub.Body.Variables.Add(self);
                p.Emit(OpCodes.Ldc_I4, -2);
                p.Emit(OpCodes.Newobj, ctor);
                p.Emit(OpCodes.Stloc, self);
                foreach (var i in pt.Fields)
                {
                    if (i != pstate && i != pcur)
                    {
                        var n = MatchParamName().Match(i.Name) is { Success: true, Groups: { } c } ? c["name1"].Value + c["name2"].Value : i.Name;
                        var param = new ParameterDefinition(i.FieldType)
                        {
                            Name = n
                        };
                        stub.Parameters.Add(param);
                        p.Emit(OpCodes.Ldloc, self);
                        p.Emit(OpCodes.Ldarg, param);
                        p.Emit(OpCodes.Stfld, i);
                    }
                }
                p.Emit(OpCodes.Ldloc, self);
                p.Emit(OpCodes.Ret);
            }
            {
                //md.Name = "<>" + md.Name;
                md.Body.Instructions.Clear();
                md.Body.ExceptionHandlers.Clear();
                var ic = new ILCursor(il);
                var self = new VariableDefinition(pt);
                il.Body.Variables.Add(self);
                ic.EmitLdcI4(0);
                ic.EmitNewobj(ctor);
                ic.EmitStloc(self);
                foreach (var i in @is)
                {
                    ic.EmitLdloc(self);
                    ic.EmitLdarg0();
                    ic.EmitLdfld(m.ImportReference(i));
                    ic.EmitStfld(pt.Fields.First(x => x.Name == i.Name));
                }
                ic.EmitLdloc(self);
                ic.EmitLdarg0();
                ic.EmitStfld(raw);
                ic.EmitLdloc(self);
                ic.EmitCall(newm);
                ic.EmitRet();
            }
            {
                newm.Overrides.Add(m.ImportReference(refien.GetMethod("MoveNext")));
                using ILContext nil = new(newm);
                newm.HasThis = true;
                //newm.Parameters[0].ParameterType = pt;
                nil.Invoke(nil =>
                {
                    var ic = new ILCursor(nil);
                    bool pre = true;
                    while (ic.TryGotoNext(MoveType.After, i => i.MatchLdarg(0) || i.MatchSwitch(out _)))
                    {
                        if (ic.Prev.MatchSwitch(out _))
                        {
                            pre = false;
                        }
                        else
                        {
                            if (pre && (ic.Next?.MatchLdfld(out var f) ?? false))
                            {
                                f = pt.Fields.First(x => x.Name == f.Name);
                                ic.MoveAfterLabels();
                                ic.EmitLdfld(f);
                                ic.Remove();
                            }
                            else
                            {
                                int index = ic.Index;
                                Merges.Add(new(new("<RemoveNext>d__114514::MoveNext", index, index), 1));
                                ic.EmitLdfld(raw);
                            }
                        }
                    }
                    ic.Index = 0;
                    while (ic.TryGotoNext(MoveType.AfterLabel, i => i.MatchLdarga(0)))
                    {
                        int index = ic.Index;
                        Merges.Add(new(new("<RemoveNext>d__114514::MoveNext", index, index + 1), 2));
                        ic.EmitLdarg(0);
                        ic.EmitLdflda(raw);
                        ic.Remove();
                    }
                    ic.Index = 0;
                    VariableDefinition? devil = null;
                    while (ic.TryGotoNext(MoveType.AfterLabel, i => i.MatchStarg(0)))
                    {
                        if (devil is null)
                        {
                            devil = new(m.TypeSystem.Object);
                            newm.Body.Variables.Add(devil);
                        }
                        int index = ic.Index;
                        Merges.Add(new(new("<RemoveNext>d__114514::MoveNext", index, index + 1), 4));
                        ic.EmitStloc(devil);
                        ic.EmitLdarg(0);
                        ic.EmitLdloc(devil);
                        ic.EmitStfld(raw);
                        ic.Remove();
                    }
                    ic.Index = 0;
#pragma warning disable CL0006 // ilspy did almost the same thing
                    var devil2 = new VariableDefinition(pcur.FieldType);
                    newm.Body.Variables.Add(devil2);
                    int curstate = 0;
                    while (ic.TryGotoNext(MoveType.AfterLabel,
                        i => i.MatchStfld(out var a) && a.DeclaringType.Is(atf) && a.Name == cur.Name,
                        i => i.MatchLdarg0(), i => i.MatchLdfld(raw),
                        i => i.MatchLdcI4(out curstate),
                        i => i.MatchStfld(out var a) && a.DeclaringType.Is(atf) && a.Name == state.Name,
                        // + 5
                        i => i.MatchLdcI4(1),
                        i => i.MatchRet()
                        ))
                    {
                        int index = ic.Index;
                        Merges.Add(new(new("<RemoveNext>d__114514::MoveNext", index, index), 2));
                        ic.EmitStloc(devil2);
                        ic.EmitLdloc(devil2);
                        ic.Index += 5;
                        index += 7;
                        Merges.Add(new(new("<RemoveNext>d__114514::MoveNext", index, index), 6));
                        ic.EmitLdarg0();
                        ic.EmitLdloc(devil2);
                        ic.EmitStfld(pcur);
                        ic.EmitLdarg0();
                        ic.EmitLdcI4(curstate);
                        ic.EmitStfld(pstate);
                    }
                });
            }
            orig = md.GetMapKey();
            FieldReference? Resolve(Func<Instruction, FieldReference?> find, MethodBase? method)
            {
                if (method is null)
                {
                    return null;
                }
                using var origctor = new DynamicMethodDefinition(method);
                return origctor.Definition.Body.Instructions.Select(find).Where(x => x?.DeclaringType.Is(atf) ?? false).SingleOrDefault()!;
            }
        }
        internal override void Remap(List<SimpleRange?> tokens)
        {
            if (orig is { })
            {
                base.Remap(tokens);
                foreach (ref var _j in CollectionsMarshal.AsSpan(tokens))
                {
                    if (_j is { } j)
                    {
                        if (j.func is "<RemoveNext>d__114514::MoveNext")
                        {
                            _j = new(orig, j.from, j.to);
                        }
                        else if (j.func == orig)
                        {
                            _j = null;
                        }
                    }
                }
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
