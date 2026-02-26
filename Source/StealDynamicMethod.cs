

namespace Celeste.Mod.ILHookDebugger
{
    using global::Celeste.Mod.Helpers;
    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using MonoMod.Cil;
    using MonoMod.Utils;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    internal class StealDynamicMethod : Transform
    {
        public static string Prefix => "#ILHDStolen#";
        public static string MMPrefix => Prefix;
        List<object> localslots = [];
        FieldDefinition slots = null!;

        internal override void Run(ILContext il, IDEFeatures feat)
        {
            var md = il.Method;
            var dmdtype = md.DeclaringType;

            slots = new("_slot", Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Public, il.Import(typeof(object[])));

            ILCursor ic = new(il);
            DynamicMethod? dm = null;
            while (ic.Next is not null)
            {
                dm = null;
                dm ??= ic.Next.Operand as DynamicMethod;
                dm ??= (ic.Next.Operand as DynamicMethodReference)?.DynamicMethod as DynamicMethod;
                if (dm is not null)
                {
                    var def = new MethodDefinition((MMPrefix + dm.Name + "@" + localslots.Count).Simplify(feat),
                                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                                    il.Import(dm.ReturnType ?? typeof(void)));
                    def.Parameters.AddRange(dm.GetParameters().Select(x => new ParameterDefinition(il.Import(x.ParameterType))));
                    il.Method.DeclaringType.Methods.Add(def);
                    var del = MakeDelegate(def, feat);

                    TypeReference objtype = il.Module.TypeSystem.Object;
                    ILCursor ix = new(new ILContext(def));
                    var inslot = localslots.Count;
                    localslots.Add(dm);
                    var delegateslot = localslots.Count;
                    localslots.Add(null!);

                    ix.EmitLdsfld(slots);
                    ix.EmitLdcI4(delegateslot);
                    ix.EmitLdelemRef();
                    //slot[delegate]
                    var stub = ix.DefineLabel();
                    ix.EmitBrtrue(stub);
                    var oo = ix.Prev;
                    //if (slot[delegate] is null)
                    //{

                    ix.EmitLdsfld(slots);
                    ix.EmitLdcI4(delegateslot);
                    //slot[delegate] =

                    ix.EmitLdsfld(slots);
                    ix.EmitLdcI4(inslot);
                    ix.EmitLdelemRef();
                    ix.EmitLdtoken(del);
                    ix.EmitDelegate(Type.GetTypeFromHandle);
                    ix.EmitCallvirt(typeof(DynamicMethod).GetMethod("CreateDelegate", [typeof(Type)]));
                    //    slot[inslot].CreateDelegate(del);
                    ix.EmitStelemRef();

                    //}
                    ix.EmitLdsfld(slots);
                    oo.Operand = ix.Prev;
                    ix.EmitLdcI4(delegateslot);
                    ix.EmitLdelemRef();
                    for (int j = 0; j < def.Parameters.Count; j++)
                    {
                        ix.EmitLdarg(j);
                    }
                    ix.EmitCallvirt(del.Methods.First(x => x.Name == "Invoke"));
                    ix.EmitRet();

                    ic.MoveAfterLabels();
                    ic.Emit(ic.Next.OpCode, def);
                    ic.Remove();
                }
                else
                {
                    ic.Index++;
                }
            }

            static TypeDefinition MakeDelegate(MethodDefinition def, IDEFeatures feat)
            {
                ModuleDefinition module = def.Module;
                var deletype = new TypeDefinition("", ("ILHookDebugger#Type#Delegate" + def.Name).Simplify(feat),
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.Class,
                    module.ImportReference(typeof(MulticastDelegate)));
                var delector = new MethodDefinition(
                    ".ctor",
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.RTSpecialName,
                    module.TypeSystem.Void);
                delector.IsRuntime = true;
                delector.Parameters.Add(new ParameterDefinition("object", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Object));
                delector.Parameters.Add(new ParameterDefinition("method", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.IntPtr));
                var deleinvoke = new MethodDefinition("Invoke",
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.NewSlot,
                    def.ReturnType);
                deleinvoke.Parameters.AddRange(def.Parameters.Select(x => x.Clone()));
                deleinvoke.IsRuntime = true;
                deletype.Methods.Add(delector);
                deletype.Methods.Add(deleinvoke);
                module.Types.Add(deletype);
                return deletype;
            }
            if (localslots.Any())
            {
                dmdtype.Fields.Add(slots);
            }
        }
        internal override void AfterLoaded(Type r, IDEFeatures feat)
        {
            if (localslots.Any())
            {
                var remoteslots = r.GetField(slots.Name)!;
                remoteslots.SetValue(null, localslots.ToArray());
            }
        }
    }
}
