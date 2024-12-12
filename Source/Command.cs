﻿using System;
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
            Assembly asm;
            if (modded)
            {
                asm = FakeAssembly.GetFakeEntryAssembly();
            }
            else
            {
                asm = typeof(Engine).Assembly;
            }
            var tar = asm.GetType(fullTypeName).GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            InsertDebugger(tar);
        }
        [Command("ILDebug_Refresh_All", """
            Refresh all debugging method.
            If someone added a new hook, run this command,
            or their hook may not work.
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
            PrintingPod.Remove();
        }
        [Command("ILDebug_Clear", """
            Remove all debuggers.
            """)]
        public static void RemoveAll()
        {
            PrintingPod.Clear();
        }

        public static void InsertDebugger(MethodInfo method)
        {
            PrintingPod.Create(method);
        }
    }
}
