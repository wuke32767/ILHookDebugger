using Celeste.Mod.Helpers;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    public static class Soncole
    {
        // bypass bananawatch consolewriteline
        public static Action<string> WriteLine = typeof(Console).GetMethod("WriteLine", [typeof(string)])!.CreateDelegate<Action<string>>();
        public static Action<string> Write = typeof(Console).GetMethod("Write", [typeof(string)])!.CreateDelegate<Action<string>>();
    }
    public static class Commands
    {
        [Command("ILDebug", """
            Add or refresh debugger for a method.
            Only search for the Celeste/Monocle/Everest method by default.
            """)]
        public static void InsertDebugger(string fullTypeName, string method, bool modded = false)
        {
            var tar = GetMethod(fullTypeName, method, modded);
            InsertDebugger(tar!);
            Engine.Commands.Log($"Info: Successfully add debugger for the method.");
        }

        private static MethodInfo? GetMethod(string fullTypeName, string method, bool modded)
        {
            Assembly asm;
            if (modded)
            {
                asm = FakeAssembly.GetFakeEntryAssembly();
            }
            else
            {
                asm = typeof(Engine).Assembly;
            }
            return asm.GetType(fullTypeName)!.GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        [Command("ILDebug_Refresh_All", """
            Refresh all debugging method.
            Mostly do nothing.
            """)]
        public static void RefreshAll()
        {
            PrintingPod.Refresh();
        }
        [Command("ILDebug_Remove", """
            Remove the most newly added debugger.
            """)]
        public static void Remove()
        {
            Engine.Commands.Log($"Info: {PrintingPod.AllDuplicants[^1].Target} will be removed.");
            PrintingPod.Remove();
        }
        [Command("ILDebug_Clear", """
            Remove all debuggers.
            """)]
        public static void RemoveAll()
        {
            PrintingPod.Clear();
        }

        [Command("ILDebug_Dump", """
            Dump all debuggung methods.
            (it will export the method to [path].)
            ([path] will be treated as directory.)
            """)]
        public static void Dump(string path, bool overwrite = false)
        {
            PrintingPod.Dump(path, overwrite);
        }

        public static void InsertDebugger(MethodInfo method)
        {
            PrintingPod.Create(method);
        }

        [Command("ILDebug_Decompile", """
            Decompile a method.
            Must install extension: Decompiler.
            Or install MappingUtils.
            """)]
        public static void Decompile(string fullTypeName, string method, bool modded = false)
        {
            var (loaded, from) = ILHookDebuggerModule.CheckDecompiler.Value;
            if (!loaded)
            {
                Engine.Commands.Log("no decompiler found");
                return;
            }
            var target = GetMethod(fullTypeName, method, modded);
            Extract(target!, from);
            static void Extract(MethodInfo target, string from)
            {
                Logger.Log(nameof(ILHookDebugger), $"Decompiling with a decompiler from {from}...");
                var (ast, decomp) = Decompilation.FromMethod(target);
                var w = new MyTokenWriter(new MulticastTextWriter(Console.Out, new MonocleTextWriter()), decomp.TypeSystem, ILHookDebuggerModule.PaletteForConsole());
                ast.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));
                Engine.Commands.Log("there's a colorful copy in your console.\n(and a colorless copy in your log.)", Color.Yellow);
            }
        }
    }
}
