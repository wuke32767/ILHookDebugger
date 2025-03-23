using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Celeste.Mod.Helpers;
using Monocle;

namespace Celeste.Mod.ILHookDebugger
{
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
    }
}
