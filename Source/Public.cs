using Celeste.Mod.ILHookDebugger.MappingUtils;
using ModInteropImportGenerator;
using MonoMod.ModInterop;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    //[GenerateImports("ILHookDebugger")]
    public static partial class ImportCallerTemplate
    {
        public static partial void ImGui_DisplayMethodIL(MethodBase method);
        /// <summary>
        /// Gets whether any decompilers are installed.
        /// </summary>
        public static partial bool CanDecompile();
        public static partial void DisplayMethodDecompilation(MethodBase method);
    }

    public static partial class ImportCallerTemplate
    {
        static bool initialized = false;

        private static void Check()
        {
            if (!initialized)
            {
                throw new InvalidOperationException("Have you ever invoked Load()?");
            }
        }

        public static partial void ImGui_DisplayMethodIL(MethodBase method)
        {
            Check();
            ImportTemplate.ImGui_DisplayMethodIL?.Invoke(method);
        }


        public static partial bool CanDecompile()
        {
            Check();
            return ImportTemplate.CanDecompile?.Invoke() ?? false;
        }

        public static partial void DisplayMethodDecompilation(MethodBase method)
        {
            Check();
            ImportTemplate.DisplayMethodDecompilation?.Invoke(method);
        }
        
        public static void Load()
        {
            typeof(ImportTemplate).ModInterop();
            initialized = true;
        }
    }
    [ModImportName("ILHookDebugger")]
    public class ImportTemplate
    {
        public static Action<MethodBase>? ImGui_DisplayMethodIL;
        public static Func<bool>? CanDecompile;
        public static Action<MethodBase>? DisplayMethodDecompilation;
    }
    [ModExportName("ILHookDebugger")]
    public static class Public
    {
        public static void ImGui_DisplayMethodIL(MethodBase method)
        {
            if (method.IsDynamicMethod())
            {
                return;
            }
            MiGui.Instance.decompiled.Add(new ControlPanel(MiGui.Instance.GetNextWindowName(method.GetMethodNameForDB()), method));

        }
        public static bool CanDecompile() => ILHookDebuggerModule.CheckDecompiler.Value.Item1;
        public static void DisplayMethodDecompilation(MethodBase method)
        {
            var (c, from) = ILHookDebuggerModule.CheckDecompiler.Value;
            if (!c)
            {
                throw new EntryPointNotFoundException("where is my decompiler");
            }
            if (method.IsDynamicMethod())
            {
                return;
            }
            MiGui.Instance.Decompile(from, method);
        }
    }
}
