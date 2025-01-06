

namespace Celeste.Mod.ILHookDebugger
{
    using Mono.Cecil;
    using MonoMod.Cil;
    using MonoMod.Utils;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    static internal class StealDynamicMethod
    {
        public const string Prefix = "#ILHDStolen#";
        public const string MMPrefix = "#ILHDFix#";
        public static void Steal(this ILContext il)
        {
#pragma warning disable CS0162 // how to duplicate a dynamic method
            return;
            Dictionary<DynamicMethod, MethodReference> compiled = [];
            uint i = 0;
            ILCursor ic = new(il);
            MethodReference mr = null!;
            while (ic.TryGotoNext(MoveType.Before, x => x.MatchCallOrCallvirt(out mr)
               /* && mr.Module is null*/)
                && mr.ResolveReflection() is DynamicMethod dm)
            {
                if (compiled.TryGetValue(dm, out var mi))
                {
                    //ic.Next.Operand = mi;
                    continue;
                }
                DynamicMethodDefinition dmd = new(dm);

                MethodDefinition steal = dmd.Definition;
                var dispose = dmd.Module;
                steal.DeclaringType.Methods.Remove(steal);
                dmd.Dispose();
                il.Method.DeclaringType.Methods.Add(steal);
                steal.Name = Prefix + dm.Name + $"<{i++}>";
                dispose?.Dispose();
            }

        }

    }
}
//namespace proxy
//{
//    using System;
//    using System.Collections.Generic;
//    using System.Globalization;
//    using System.Reflection;
//    using System.Reflection.Emit;
//    class DynamicProxy(DynamicMethod method) : MethodBase
//    {
//        class MethodBodyProxy(DynamicMethod method) : MethodBody
//        {
//            public override IList<ExceptionHandlingClause> ExceptionHandlingClauses => method.
//            public override byte[] GetILAsByteArray() => throw new NotImplementedException();
//            public override IList<LocalVariableInfo> LocalVariables => throw new NotImplementedException();
//            public override bool InitLocals { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
//        }
//        public override MethodBody? GetMethodBody()
//        {
//            return new MethodBodyProxy(method);
//        }
//        public override bool ContainsGenericParameters => method.ContainsGenericParameters;
//        public override Type[] GetGenericArguments() => method.GetGenericArguments();
//        public override CallingConventions CallingConvention => method.CallingConvention;
//        public override MemberTypes MemberType => method.MemberType;
//        public override MethodAttributes Attributes => method.Attributes;

//        public override Type DeclaringType => method.DeclaringType;

//        public override RuntimeMethodHandle MethodHandle => method.MethodHandle;

//        public override string Name => method.Name;

//        public override Type ReflectedType => method.ReflectedType;

//        public override object[] GetCustomAttributes(bool inherit) => method.GetCustomAttributes(inherit);

//        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => method.GetCustomAttributes(attributeType, inherit);

//        public override MethodImplAttributes GetMethodImplementationFlags() => method.GetMethodImplementationFlags();

//        public override ParameterInfo[] GetParameters() => method.GetParameters();

//        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture) => method.Invoke(obj, invokeAttr, binder, parameters, culture);

//        public override bool IsDefined(Type attributeType, bool inherit) => method.IsDefined(attributeType, inherit);
//    }
//}