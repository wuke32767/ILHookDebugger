using Celeste.Mod.ILHookDebugger.MappingUtils;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WritingMango.ILRunner;

namespace Celeste.Mod.ILHookDebugger
{
    public abstract class PauseProvider
    {
        internal int Index;

        internal virtual bool Trigger(Glass self, ILExecutionContext context)
            => TryTrigger(self, context);

        internal abstract bool TryTrigger(Glass self, ILExecutionContext context);
    }
    public class Providers : PauseProvider, IEnumerable<PauseProvider>
    {
        internal List<PauseProvider> providers = [];

        public IEnumerator<PauseProvider> GetEnumerator()
        {
            return ((IEnumerable<PauseProvider>)this.providers).GetEnumerator();
        }
        internal void Add(PauseProvider p) => providers.Add(p);
        internal override bool Trigger(Glass self, ILExecutionContext context)
            => providers.Any(x => x.Trigger(self, context));

        internal override bool TryTrigger(Glass self, ILExecutionContext context)
            => providers.Any(x => x.TryTrigger(self, context));

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)this.providers).GetEnumerator();
        }
    }
    public class BreakPoint() : PauseProvider, IComparable<BreakPoint>
    {
        public int Pos;
        public bool Conditional;
        public System.Numerics.Vector4 Color
        {
            get
            {
                var state = sg;
                return
                    !Conditional || state is null ?
                        new(241 / 255f, 144 / 255f, 91 / 255f, 1) :
                    state.condsCompiled is null || !state.condsCompiled.IsCompleted ?
                        new(144 / 255f, 141 / 255f, 91 / 255f, 1) :
                    state.exc is null ?
                        new(23 / 255f, 241 / 255f, 91 / 255f, 1) :

                        new(244 / 255f, 241 / 255f, 91 / 255f, 1);
            }
        }

        public int CompareTo(BreakPoint? other)
        {
            return Pos.CompareTo(other?.Pos ?? int.MinValue);
        }

        internal override bool TryTrigger(Glass self, ILExecutionContext context)
        {
            var state = sg;
            if (context.ProgramCounter != Pos)
            {
                return false;
            }
            if (!Conditional)
            {
                return true;
            }
            if (state is null)
            {
                return false;
            }
            if (state.condsCompiled is null)
            {
                lock (state)
                {
                    if (state.exc is null)
                    {
                        state.condsCompiled ??= Compile(null, self, state);
                    }
                }
            }
            if (state.exc is { })
            {
                return false;
            }
            return state.condsCompiled!.Result!(context);
        }
        public void RenderConfig(Mirror self)
        {
            conds ??= new byte[512];
            ImGui.Text($"A breakpoint on line [{Pos}].");
            ImGui.BeginDisabled(!ILHookDebuggerModule.CheckScript.Value.Item1);
            try
            {
                if (ImGui.Checkbox("Read All Assembly", ref SrcGenHelper.Anything))
                {
                    if (SrcGenHelper.Anything)
                    {
                        SrcGenHelper.Refresh();
                    }
                    if (Conditional)
                    {
                        SrcGenHelper.EnsureInitialized();
                    }
                }
                if (ImGui.Checkbox("", ref Conditional))
                {
                    if (Conditional)
                    {
                        SrcGenHelper.EnsureInitialized();
                    }
                }
                ImGui.SameLine();
                ImGui.BeginDisabled(!Conditional);
                try
                {
                    if (ImGui.InputText("Condition", conds, (uint)conds.Length, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        Init(self.maker);
                    }
                }
                finally
                {
                    ImGui.EndDisabled();
                }
            }
            finally
            {
                ImGui.EndDisabled();
            }
            ImGui.Text("press enter to apply.");
            //if (ImGui.Checkbox("Use StackValue", ref enable))
            //{
            //    Init(self.maker);
            //}
            if (sg is { })
            {
                if (sg.exc is not null)
                {
                    ImGui.Text("can't compile.");
                    ImGui.Text(sg.exc);
                }
                else if (sg.condsCompiled is null)
                {
                    ImGui.Text("it is not compiled, because it's never hit.");
                    ImGui.Text("i can't compile it ahead of time because i don't know the type of stack value.");
                }
                else if (!sg.condsCompiled.IsCompleted)
                {
                    ImGui.Text("compiling...");
                }
            }
        }
        class StateGroup
        {
            internal volatile string? exc;
            internal Task<Func<ILExecutionContext, bool>?>? condsCompiled;
            internal CancellationTokenSource source = new();
        }
        private byte[]? conds;
        private bool enable;
        volatile StateGroup? sg;

        public BreakPoint(int programCounter) : this()
        {
            Pos = programCounter;
        }

        private Task<Func<ILExecutionContext, bool>?> Compile(RunnerFactory? maker, Glass? self, StateGroup state)
        {
            return Task.Run(async () =>
            {
                var cc = state.source.Token;
                maker ??= self!.Context.Runner;
                const string contextname = "__context";
                cc.ThrowIfCancellationRequested();
                var ile = Helpery.GetSGName(typeof(ILExecutionContext));
                StringBuilder sb = Helpery.GetContext(maker, self, contextname);
                var to = conds.IndexOf((byte)0);
                var s = Encoding.UTF8.GetString(conds.AsSpan()[..to]);
                cc.ThrowIfCancellationRequested();
                var (a, b) = await SrcGenHelper.Eval($$"""
                unsafe bool _my_TryGet({{ile}} {{contextname}})
                {
                {{sb}}
                    return {{s}};
                }
                (global::System.Func<{{ile}}, bool>)_my_TryGet
                """);
                cc.ThrowIfCancellationRequested();
                if (a is { })
                {
                    state.exc = a;
                }
                else
                {
                    try
                    {
                        var o = await b!();
                        return (Func<ILExecutionContext, bool>)o;
                    }
                    catch (Exception e)
                    {
                        state.exc = e.ToString();
                    }
                }
                return null;
            });
        }
        private void Init(RunnerFactory maker)
        {
            StateGroup ne = new();
            if (!enable)
            {
                ne.condsCompiled = Compile(maker, null, ne);
            }
            else
            {
                SrcGenHelper.EnsureInitialized();
            }
            var old = sg;
            sg = ne;
            old?.source.Cancel();
        }
    }
    public class BreakPointList : PauseProvider, IEnumerable<BreakPoint>
    {
        internal volatile ImmutableList<BreakPoint> point = [];

        public IEnumerator<BreakPoint> GetEnumerator()
        {
            return ((IEnumerable<BreakPoint>)this.point).GetEnumerator();
        }

        internal override bool TryTrigger(Glass self, ILExecutionContext context)
        {
            var i = point.BinarySearch(new(context.ProgramCounter));
            return i >= 0 && point[i].TryTrigger(self, context);
        }
        internal override bool Trigger(Glass self, ILExecutionContext context)
        {
            var i = point.BinarySearch(new(context.ProgramCounter));
            return i >= 0 && point[i].Trigger(self, context);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)this.point).GetEnumerator();
        }
    }
    public class Step : PauseProvider
    {
        internal bool pause = false;
        internal override bool TryTrigger(Glass self, ILExecutionContext context)
        {
            return pause;
        }
        internal override bool Trigger(Glass self, ILExecutionContext context)
        {
            if (TryTrigger(self, context))
            {
                pause = false;
                return true;
            }
            return false;
        }
    }
    public class BigStep : Step
    {
        internal override bool TryTrigger(Glass self, ILExecutionContext context)
        {
            return pause && context.Stack.Count == 0;
        }
    }
    public class LineStep : Step
    {
        internal override bool TryTrigger(Glass self, ILExecutionContext context)
        {
            return pause && (self.mirror.rangeable?.Contains(context.ProgramCounter) ?? false);
        }
    }
    // won't someone rescue we
    public class Glass : DebuggerBase
    {
        public int CurrentThread = Environment.CurrentManagedThreadId;
        public bool Main = MainThreadHelper.IsMainThread;

        //internal int id = Interlocked.Increment(ref ids);
        //static int ids = 0;
        bool accept = true;
        public ILExecutionContext Context => context;
        public Providers points;

        public BreakPointList ppoint = new();
        internal volatile bool running = true;
        internal Step step = new();
        internal BigStep bigstep = new();
        internal LineStep linestep = new();
        internal readonly Mirror mirror;
        private readonly ILExecutionContext context;
        internal string?[]? EvalCache;
        public Glass(Mirror mirror, ILExecutionContext context)
        {
            this.mirror = mirror;
            this.context = context;
            points = [step, bigstep, linestep, mirror.allpoint, ppoint,];
        }

        protected override void BeforeExecute(ILExecutionContext context, ResolvedInstruction cur)
        {
            if (mirror.disposed)
            {
                return;
            }
            if (!accept)
            {
                return;
            }
            if (points.Trigger(this, context))
            {
                running = false;
                if (MainThreadHelper.IsMainThread)
                {
                    while (!running)
                    {
                        if (mirror.disposed)
                        {
                            return;
                        }
                        Mirror.Waiting = true;
                        var self = Engine.Instance;
                        self.gameTimer.Stop();
                        try
                        {
                            mirror.EnsureAdded();
                            //MInput.Update();
                            if (mirror.renderFailed is { })
                            {
                                Engine.Instance.Tick();
                            }
                            else
                            {
                                try
                                {
                                    Thread.Sleep(1);
                                    FNAPlatform.PollEvents(self, ref self.currentAdapter, self.textInputControlDown, ref self.textInputSuppress);
                                    if (self.BeginDraw())
                                    {
                                        self.Draw(self.gameTime);
                                        self.EndDraw();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    mirror.renderFailedAgain = ex;
                                    running = false;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            mirror.renderFailed = ex;
                        }
                        finally
                        {
                            self.gameTimer.Start();
                            Mirror.Waiting = false;
                        }
                    }
                }
                else
                {
                    while (!running)
                    {
                        if (mirror.disposed)
                        {
                            return;
                        }

                        Thread.Sleep(1);
                    }
                }
                EvalCache = null;
                lastlastpc = context.ProgramCounter;
            }
        }
        protected override void OnStacking(ILExecutionContext context, ILExecutionContext callee, DebuggerBase _self)
        {
            var self = (Glass)_self;
            if (MainThreadHelper.IsMainThread && Mirror.Waiting)
            {
                self.accept = false;
            }
        }
        protected override void MethodEntry(ILExecutionContext context)
        {
            mirror.Active.TryAdd(context, this);
            //Mirror.AllActive.TryAdd(id, this);
        }
        void Smash(ILExecutionContext context)
        {
            mirror.Active.Remove(context, out _);
            //Mirror.AllActive.Remove(id, out _);
        }
        protected override void MethodReturn(ILExecutionContext context, StackValue value)
        {
            Smash(context);
        }
        protected override void ExceptionUnhandled(ILExecutionContext context, Exception ex)
        {
            Smash(context);
        }

        internal int lastlastpc = -1;

    }
    // i shut my eyes
    public class Mirror : IExtraWindow
    {
        internal struct Evalor
        {
            public Task<Func<ILExecutionContext, string>> Func;
            public string str;
        }
        internal List<Evalor?> EvalCache = [];
        internal bool disposed = false;
        internal Exception? renderFailed;
        internal Exception? renderFailedAgain;
        internal ConcurrentDictionary<ILExecutionContext, Glass> Active = [];
        //public static ConcurrentDictionary<int, Glass> AllActive = [];
        internal static Dictionary<MethodBase, Mirror> DebuggingMethodsController = [];
        public bool ShouldBreak = true;
        //internal Providers allpoint ;
        internal BreakPointList allpoint;
        internal BreakPointList point => allpoint;
        internal RunnerFactory maker = null!;
        internal static FieldInfo ReflShouldBreak = typeof(Mirror).GetField(nameof(ShouldBreak), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)!;
        internal static FieldInfo ReflWaiting = typeof(Mirror).GetField(nameof(Waiting), BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;

        public Mirror(ILContext def, MethodBase method, IDEFeatures feature)
        {
            DebuggingMethodsController.Add(method, this);
            Method = method;
            Title = "CelesteDebugger - " + method.GetMethodNameForDB();
            Refresh(def, feature);
            allpoint = new() { point = [new(0)] };
            var ll = maker.VariableDefinitions;
            var aa = maker.ParameterDefinitions;
            LocalInspector = new Func<LocalValue, string?>[ll.Length];
            ArgumentInspector = new Func<LocalValue, string?>[aa.Length];
            _ = Then();
            //allpoint = [point];
            // return varsDef.Select(GetGetterFor).ToArray();
        }
        void Refresh(ILContext def, IDEFeatures feature)
        {
            maker?.Dispose();
            maker = new(def.Method);
            maker.AddDebugger(c => new Glass(this, c));
            if (/*false &&*/ ILHookDebuggerModule.CheckDecompiler.Value.Item1)
            {
                using var op = PrintingPod.Operate(Method, def, feature);
                op.output.Position = 0;
                var (ast, decomp) = Decompilation.Final(op.output, op.name);

                EditorColor color = new();
                MyCodeRange range = new(def.Method.DeclaringType);
                using TokenBasedTextWriter<Color> o = new() { Palette = color, Range = range };
                var w = new MyTokenWriter(o, decomp.TypeSystem, color, range);
                ast.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));
                //(ast.Descendants.First() as ICSharpCode.Decompiler.CSharp.Syntax.MethodDeclaration).Annotation<ICSharpCode.Decompiler.IL.ILFunction>().Variables.First().
                foreach (var item in op.Transforms.Reversed())
                {
                    item.Remap(o.ranges);
                }
                text = o.colors;
                string methodname = op.name + "::" + op.methodname;
                ranges = o.ranges.Select(_z => _z is not { } z || z.func != methodname ? (SimpleRange?)null : z).ToList();
                rangeable = ranges.Select(a => a?.from).Where(a => a.HasValue).Select(a => a!.Value).ToHashSet();
                foreach (var item in op.Transforms)
                {
                    item.AfterLoaded(null!, feature);
                }
            }
        }
        List<List<Token<Color>>>? text;
        List<SimpleRange?>? ranges;
        internal HashSet<int>? rangeable;
        async Task Then()
        {
            var ll = maker.VariableDefinitions;
            var aa = maker.ParameterDefinitions;
            for (int i = 0; i < ll.Length; i++)
            {
                LocalInspector[i] = await GetGetterFor(ll[i]);
            }
            for (int i = 0; i < aa.Length; i++)
            {
                ArgumentInspector[i] = await GetGetterFor(aa[i]);
            }
        }
        Func<LocalValue, string?>?[] ArgumentInspector;
        Func<LocalValue, string?>?[] LocalInspector;

        static Task<Func<LocalValue, string?>> GetGetterFor(LocalValue varsDef)
        {
            Type exactType = varsDef.exactType;
            if (exactType.IsByRef || exactType.IsByRefLike)
            {
                return Task.FromResult<Func<LocalValue, string?>>(_ => "NotSupported//TODO");
            }
            else
            {
#pragma warning disable CS8500
                static unsafe string? Get<T>(LocalValue loc) => (*(T*)loc.value)?.ToString();
#pragma warning restore CS8500
                var d = Get<object>;
                return Task.FromResult(d.Method.GetGenericMethodDefinition().MakeGenericMethod(exactType).CreateDelegate<Func<LocalValue, string?>>());
            }
        }


        public static Mirror GetOrUpdate(ILContext def, MethodBase method, IDEFeatures feature)
        {
            if (!DebuggingMethodsController.TryGetValue(method, out var m))
            {
                DebuggingMethodsController[method] = m = new(def, method, feature);
            }
            else
            {
                m.Refresh(def, feature);
            }
            return m;
        }
        public void Dispose()
        {
            disposed = true;
            DebuggingMethodsController.Remove(Method);
            maker.Dispose();
            MiGui.Instance.decompiled.Remove(this);
        }
        internal void EnsureAdded()
        {
            if (disposed)
            {
                return;
            }
            _ = MainThreadHelper.Schedule(() =>
            {
                if (!MiGui.Instance.decompiled.Contains(this))
                {
                    MiGui.Instance.decompiled.Add(this);
                }
            }).AsTask();
        }

        internal MethodInfo Entry => maker.FakeEntry;

        public MethodBase Method { get; }
        public override string Title { get; }

        static ILHook? hook;
        static ILHook? hook2;
        internal static bool Waiting;
        internal static bool FoundCelesteRepl;
        internal static bool ReplDone;
        public static void Load()
        {
            var GameTick = typeof(Game).GetMethod(nameof(Game.Tick))!;
            var CompRender = typeof(ComponentList).GetMethod("set_" + nameof(ComponentList.LockMode), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            hook?.Dispose();
            hook = new(GameTick, il =>
            {
                ILCursor ic = new(il);
                while (ic.TryGotoNext(i => i.MatchCallOrCallvirt<Game>("Update")))
                {
                    ic.EmitLdsfld(ReflWaiting);
                    var label = ic.DefineLabel();
                    var label2 = ic.DefineLabel();
                    ic.EmitBrtrue(label);
                    ic.Goto(ic.Next!.Next);
                    ic.EmitBr(label2);
                    ic.MarkLabel(label);
                    ic.EmitDelegate(UpdateIGuess);
                    ic.MarkLabel(label2);
                    static void UpdateIGuess(Game game, GameTime time)
                    {
                    }
                }
            });
            hook2?.Dispose();
            hook2 = new(CompRender, il =>
            {
                ILCursor ic = new(il);
                var label = ic.DefineLabel();
                ic.EmitDelegate(GetCond);
                static bool GetCond() => Waiting && MainThreadHelper.IsMainThread;
                ic.EmitBrfalse(label);
                ic.EmitRet();
                ic.MarkLabel(label);
            });
        }

        internal static void Unload()
        {
            hook?.Dispose();
            hook2?.Dispose();
        }
        Glass? last;
        int lastpc = -1;
        public override ImGuiWindowFlags flags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoSavedSettings;
        BreakPoint comparer = new(0);
        byte[] buf = new byte[512];
        bool stackaccess = false;
        bool useDecom = true;
        public override void Render()
        {
            ImGui.Checkbox("enable", ref ShouldBreak);
            ImGui.SetItemTooltip("Start capture. Greatly affect performance even if there're no break points.");
            bool selected = false;
            ImGui.Text("Currently Running:");
            if (Active.IsEmpty)
            {
                ImGui.SameLine();
                ImGui.Text("None");
            }
            //if (ImGui.Button("Watch"))
            //{
            //    Watcher.EnsureAdded();
            //}
            foreach (var i in Active)
            {
                ImGui.SameLine();
                var g = i.Value;
                var t = g.CurrentThread.ToString();
                if (g.Main)
                {
                    t += "(main thread)";
                }
                if (ImGui.RadioButton(t, g == last))
                {
                    last = g;
                }
                if (g == last)
                {
                    selected = true;
                }
            }
            if (!selected)
            {
                var c = Active.FirstOrDefault();
                last = c.Value;
            }
            ImGui.BeginDisabled(last is not { } || last.running);
            if (ImGui.Button("continue"))
            {
                last?.running = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("step"))
            {
                last?.step.pause = true;
                last?.running = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("big step"))
            {
                last?.bigstep.pause = true;
                last?.running = true;
            }
            ImGui.SetItemTooltip("continue until eval stack is empty");
            if (useDecom)
            {
                ImGui.SameLine();
                if (ImGui.Button("line step"))
                {
                    last?.linestep.pause = true;
                    last?.running = true;
                }
                ImGui.SetItemTooltip("run to next line");
            }
            ImGui.EndDisabled();
            if (text is not null)
            {
                ImGui.Checkbox("show decompile", ref useDecom);
            }
            if (last is { })
            {
                var c = last.Context;
                NewMethod1("Arguments:", c.Arguments, ArgumentInspector);
                NewMethod1("LocalVars:", c.Variables, LocalInspector);
                void NewMethod1(string fmt, LocalValue[] paramList, Func<LocalValue, string?>?[] inspector)
                {
                    var c = last.Context;
                    if (inspector.Length > 0)
                    {
                        ImGui.Text(fmt);
                        ImGui.SameLine();
                        ImGui.BeginDisabled();
                        for (int i = 0; i < inspector.Length; i++)
                        {
                            ImGui.SameLine();
                            NewMethod(paramList[i], inspector[i]);
                        }
                        ImGui.EndDisabled();
                    }
                }
                static void NewMethod(LocalValue paramList, Func<LocalValue, string?>? argumentInspector)
                {
                    string label = argumentInspector is { } ins ? ins(paramList) ?? "<null>" : "evaluating...";
                    string l = label;
                    if (l.Length > 30)
                    {
                        l = l[..30];
                    }
                    ImGui.Button(l);
                    if (label.Length > 30)
                    {
                        ImGui.SetItemTooltip(label);
                    }
                }
            }
            int? programCounter = last?.Context.ProgramCounter;
            if (ImGui.BeginTable("##" + useDecom, 3, ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None);
                ImGui.TableNextRow();
                [MemberNotNullWhen(false, nameof(ranges))]
                [MemberNotNullWhen(false, nameof(text))]
                bool dec() => text is null || useDecom is false;
                int count = dec() ? maker.Instructions.Count : text.Count;
                bool first = false;
                for (int _i = 0; _i < count; _i++)
                {
                    var i = _i;
                    var t = i + 1;
                    if (!dec())
                    {
                        i = ranges[_i]?.from ?? -1;
                        if (i < 0)
                        {
                            t = -1;
                        }
                        else if (ranges.Count > _i + 1)
                        {
                            t = Math.Max(ranges[_i + 1]?.from ?? -1, ranges[_i]?.to ?? -1);
                        }
                        else
                        {
                            t = ranges[_i]?.to ?? -1;
                        }
                    }

                    bool exe = (!first && t > programCounter && programCounter >= i) || programCounter == i;
                    if (exe && lastpc != programCounter)
                    {
                        ImGui.SetScrollHereY();
                    }
                    ImGui.TableNextColumn();
                    comparer.Pos = i;
                    var ptf = point.point.BinarySearch(comparer);
                    var ptfx = ptf < 0 ? ~ptf : ptf;
                    comparer.Pos = t;
                    var ptt = point.point.BinarySearch(comparer);
                    var pttx = ptt < 0 ? ~ptt : ptt;
                    string str_id = "##" + _i.ToString();
                    bool right = false;
                    bool left = false;
                    void Do()
                    {
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            right = true;
                        }
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        {
                            left = true;
                        }
                    }
                    System.Numerics.Vector4 color = new(1, 1, 1, 1);
                    if (i < 0)
                    {
                        color = new(0.5f, 0.5f, 0.5f, 1);
                    }
                    var str = _i.ToString();
                    if (i >= 0 && !dec())
                    {
                        str = str + "/" + i.ToString();
                    }
                    ImGui.TextColored(color, str);
                    Do();
                    if (exe)
                    {
                        if (programCounter == i)
                        {
                            System.Numerics.Vector4 cursor = new(255 / 255f, 221 / 255f, 150 / 255f, 1);
                            cursor *= 0.7f;
                            ImGui.PushStyleColor(ImGuiCol.TableRowBg, cursor);
                            ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, cursor);
                            ImGui.SetItemTooltip("Code to be executed");
                        }
                        else
                        {
                            System.Numerics.Vector4 cursor = new(168 / 255f, 201 / 255f, 75 / 255f, 1);
                            cursor *= 0.5f;
                            ImGui.PushStyleColor(ImGuiCol.TableRowBg, cursor);
                            ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, cursor);
                            ImGui.SetItemTooltip("Code to be executed, May be here");
                        }
                    }
                    ImGui.TableNextColumn();
                    try
                    {
                        BreakPoint? breakPoint = null;
                        if (ptfx != pttx)
                        {
                            breakPoint = point.point[ptfx];
                            ImGui.PushStyleColor(ImGuiCol.Text, breakPoint.Color);
                            if (pttx - ptfx > 1 || ptf < 0)
                            {
                                ImGui.Text("* ");
                                ImGui.PopStyleColor();
                                ImGui.SetItemTooltip("This breakpoint(s) can't be represented.\nSwitch to IL Mode.");
                            }
                            else
                            {
                                ImGui.Bullet();
                                ImGui.PopStyleColor();
                            }
                        }
                        else
                        {
                            ImGui.Text("  ");
                        }
                        Do();
                        if (left)
                        {
                            if (ptfx != pttx)
                            {
                                point.point = point.point.RemoveRange(ptfx, pttx - ptfx);
                            }
                            else
                            {
                                if (i >= 0)
                                {
                                    point.point = point.point.Insert(ptfx, new(i));
                                }
                            }
                        }
                        if (breakPoint is { })
                        {
                            if (right && i >= 0)
                            {
                                ImGui.OpenPopup(str_id);
                            }
                            //if (breakPoint.HasConfig)
                            {
                                if (ImGui.BeginPopup(str_id))
                                {
                                    breakPoint.RenderConfig(this);
                                    ImGui.EndPopup();
                                }
                            }
                        }
                        else
                        {
                            //if (ImGui.BeginPopup(str_id))
                            //{
                            //    ImGui.Text("Add Conditional BreakPoint");
                            //    ImGui.InputText("Condition", buf, (uint)buf.Length);
                            //    //ImGui.Checkbox("Use StackValue", ref stackaccess);
                            //    if (ImGui.Button("Add"))
                            //    {
                            //        var b = new BreakPoint(i, buf, stackaccess, maker);
                            //        buf = new byte[512];
                            //        point.point = point.point.Insert(~ptf, b);
                            //        ImGui.CloseCurrentPopup();
                            //    }
                            //    ImGui.EndPopup();
                            //}
                        }
                        ImGui.TableNextColumn();
                        if (dec())
                        {
                            var instr = maker.Instructions[i];
                            string fmt = instr.OpCode.ToString();
                            if (last?.lastlastpc == i)
                            {
                                ImGui.TextColored(new(0, 0.8f, 0.8f, 1), fmt);
                            }
                            else
                            {
                                ImGui.Text(fmt);
                            }
                            var fmt2 = talkback(instr.Operand)?.ToString();
                            if (fmt2 is { })
                            {
                                ImGui.SameLine();
                                Helpery.TextSafeColored(new(0.7f, 0.9f, 0.9f, 1), fmt2);
                            }
                        }
                        else
                        {
                            var instr = text[_i];
                            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(0, ImGui.GetStyle().ItemSpacing.Y));
                            foreach (var word in instr)
                            {
                                var cc = word.color.ToVector4();
                                Helpery.TextGoodColored(cc, word.token);
                                ImGui.SameLine();
                            }
                            ImGui.PopStyleVar();
                        }
                        ImGui.TableNextRow();
                    }
                    finally
                    {
                        if (exe)
                        {
                            ImGui.PopStyleColor(2);
                        }
                    }
                    first = first || exe;
                }
                ImGui.EndTable();
            }
            lastpc = programCounter ?? lastpc;

        }
        internal static object? talkback(object? src)
        {
            return src switch
            {
                TransformedFunctionPointer i => i.raw,
                TransformedTypedFunctionPointer i => i.raw,
                TransformedVirtualFunctionPointer i => i.raw,
                TransformedExecutable i => i.raw,
                TransformedGetter i => i.raw,
                TransformedSetter i => i.raw,
                TransformedRefer i => i.raw,
                _ => src,
            };
        }
    }
    public static partial class Helpery
    {
        internal static StringBuilder GetContext(RunnerFactory maker, Glass? self, string contextname)
        {
            StringBuilder sb = new("#pragma warning disable CS8500\n#pragma warning disable CS8321\n");
            for (int i = 0; i < maker.VariableDefinitions.Length; i++)
            {
                sb.AppendLine(MakeLocalic("Local", "Variables", i, maker.VariableDefinitions[i].exactType, contextname));
            }
            for (int i = 0; i < maker.ParameterDefinitions.Length; i++)
            {
                sb.AppendLine(MakeLocalic("Param", "Arguments", i, maker.ParameterDefinitions[i].exactType, contextname));
            }
            if (self is { })
            {
                for (int i = 0; i < self.Context.Stack.Count; i++)
                {
                    StackValue stackValue = self.Context.Stack[i];
                    if (stackValue.type is StackValueType.Byref)
                    {
                        string v = Helpery.GetSGName(stackValue.typeIf!);
                        sb.AppendLine($"unsafe ref {v} Stack_{i}_Known() => ref **({v}**)(nint){contextname}.Stack[{i}].value;");
                    }
                    else if (stackValue.type is StackValueType.Like)
                    {
                        string v = Helpery.GetSGName(stackValue.typeIf!);
                        sb.AppendLine($"unsafe {v} Stack_{i}_Known() => *({v}*)(nint){contextname}.Stack[{i}].value;");
                    }
                    sb.AppendLine($"unsafe {Helpery.GetSGName(typeof(StackValue))} Stack_{i}() => {contextname}.Stack[{i}];");
                }
            }
            static string MakeLocalic(string name, string var, int i, Type ex, string contextname)
            {
                if (ex.IsByRef)
                {
                    var e = ex.GetElementType()!;
                    var type = Helpery.GetSGName(e);
                    return $"unsafe ref {type} {name}_{i}() => ref **({type}**)({contextname}.{var}[{i}].value);";
                }
                else
                {
                    var type = Helpery.GetSGName(ex);
                    return $"unsafe {type} {name}_{i}() => *({type}*)({contextname}.{var}[{i}].value);";
                }
            }

            return sb;
        }

        public static string GetSGName(Type t)
        {
            List<string> arr = null!;
            if (t.IsGenericType)
            {
                arr = t.GetGenericArguments().Select(GetSGName).ToList();
            }
            List<string> part = [];
            while (true)
            {
                if (t.Name.LastIndexOf('`') is >= 0 and { } at)
                {
                    var arity = int.Parse(t.Name.AsSpan()[(at + 1)..]);
                    part.Add($"{t.Name[..at]}<{string.Join(", ", arr.TakeLast(arity))}>");
                }
                else
                {
                    part.Add(t.Name);
                }
                if (t.DeclaringType is not { } tb)
                {
                    break;
                }
                t = t.DeclaringType;
            }
            if (!string.IsNullOrEmpty(t.Namespace))
            {
                part.Add(t.Namespace);
            }
            part.Reverse();
            return "global::" + string.Join(".", part);
        }
    }
    class Watcher : IExtraWindow
    {
        public static Watcher watcher = new();
        public Glass? LastActive;
        public List<(string, MethodBase)> inputs = [];
        byte[] buf = new byte[512];
        public override string Title => "IL Execution Watcher";

        public static void EnsureAdded()
        {
            if (!MiGui.Instance.decompiled.Contains(watcher))
            {
                MiGui.Instance.decompiled.Add(watcher);
            }
        }

        public override void Render()
        {
            [MemberNotNullWhen(false, nameof(LastActive))]
            bool v() => LastActive is null || LastActive.running;
            if (v())
            {
                ImGui.Text("No Active Context.");
            }
            else
            {
                ImGui.Text(LastActive.mirror.Method.GetMethodNameForDB());
                ImGui.SameLine();
                ImGui.Text(LastActive.CurrentThread.ToString() + (LastActive.Main ? " (Main)" : ""));
            }
            ImGui.InputText("Watch", buf, (uint)buf.Length);
            ImGui.SetItemTooltip("""
                There're several functions that:
                // Get local variable at index X.
                T Local_X();
                // Get argument at index X.
                T Param_X();
                """);
            ImGui.Button("Add");
            if (ImGui.BeginTable("", 4))
            {
                if (!v())
                {
                    if (LastActive.EvalCache is not { Length: { } c } || c != inputs.Count)
                    {
                        LastActive.EvalCache = new string[inputs.Count];
                    }
                }
                for (int i = 0; i < inputs.Count; i++)
                {
                    var (str, on) = inputs[i];
                    ImGui.NextColumn();
                    if (!v())
                    {
                        var m = LastActive.mirror;
                        ref var ec = ref CollectionsMarshal.AsSpan(m.EvalCache)[i];
                        void Init(ref Mirror.Evalor? eval)
                        {
                            async Task<Func<ILExecutionContext, string>> init()
                            {

                                var (a, b) = await SrcGenHelper.Eval($$$"""
                                    string Eval({{{Helpery.GetSGName(typeof(ILExecutionContext))}}} __context)
                                    {
                                    {{{Helpery.GetContext(m.maker, null, "__context")}}}
                                        return ({{{str}}})?.ToString();
                                    }
                                    Eval
                                    """);
                                if (a is { })
                                {
                                    return _ => a;
                                }
                                var u = (Func<ILExecutionContext, string>)await b!();
                                return u;
                            }
                            eval ??= new() { str = str, Func = init() };
                        }
                        if (on == m.Method)
                        {
                            Init(ref ec);
                        }
                        else if (ec is not { })
                        {
                            if (ImGui.Button("do so ##" + i))
                            {
                                Init(ref ec);
                            }
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("No Active Context.");
                    }
                    ImGui.NextColumn();
                }
            }
        }
    }
}
