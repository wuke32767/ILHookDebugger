using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp.Syntax.PatternMatching;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;
using ImGuiColorTextEditNet;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.ILHookDebugger
{
    static class Helpery
    {
        public static T? PeekOrDefault<T>(this Stack<T> self)
        {
            if (self.TryPeek(out var r))
            {
                return r;
            }
            return default;
        }
    }
    class MonocleTextWriter() : TextWriter
    {
        public override Encoding Encoding { get => Encoding.UTF8; }
        StringBuilder cache = new();
        public override void Write(char value)
        {
            cache.Append(value);
            if (cache[^1] == '\n')
            {
                TextFlush();
            }
        }

        public override void Write(string? value)
        {
            cache.Append(value);
            if (cache[^1] == '\n')
            {
                TextFlush();
            }
        }
        void TextFlush()
        {
            if (cache.Length > 0)
            {
                if (cache[^1] == '\n')
                {
                    cache.Remove(cache.Length - 1, 1);
                    if (cache[^1] == '\r')
                    {
                        cache.Remove(cache.Length - 1, 1);
                    }
                }
                Engine.Commands.Log(cache.ToString());
                cache.Clear();
            }
        }

        public override void WriteLine(string? value)
        {
            cache.AppendLine(value);
            TextFlush();
        }
        protected override void Dispose(bool disposing)
        {
            TextFlush();
        }
    }
    class MulticastTextWriter(params System.Collections.Immutable.ImmutableArray<TextWriter> Source) : TextWriter
    {
        public override Encoding Encoding { get => Encoding.UTF8; }
        public override void Write(char value)
        {
            foreach (var writer in Source)
            {
                writer.Write(value);
            }
        }

        public override void Write(string? value)
        {
            foreach (var writer in Source)
            {
                writer.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            foreach (var writer in Source)
            {
                writer.WriteLine(value);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var writer in Source)
                {
                    writer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
    interface IPalette
    {
        void Receive(int index);
        void Done();
    }
    abstract class Palette<T> : IPalette
    {
        internal T[] values;
        protected Palette()
        {
            values = [
                visibilityKeywordsColor,
                namespaceKeywordsColor,
                structureKeywordsColor,
                gotoKeywordsColor,
                queryKeywordsColor,
                exceptionKeywordsColor,
                checkedKeywordColor,
                unsafeKeywordsColor,
                valueTypeKeywordsColor,
                referenceTypeKeywordsColor,
                operatorKeywordsColor,
                parameterModifierColor,
                modifiersColor,
                accessorKeywordsColor,
                attributeKeywordsColor,

                referenceTypeColor,
                valueTypeColor,
                interfaceTypeColor,
                enumerationTypeColor,
                default!,
                delegateTypeColor,

                methodCallColor,
                methodDeclarationColor,
                fieldDeclarationColor,
                fieldAccessColor,
                propertyDeclarationColor,
                propertyAccessColor,
                eventDeclarationColor,
                eventAccessColor,

                variableColor,
                parameterColor,

                valueKeywordColor,
                thisKeywordColor,
                trueKeywordColor,
                typeKeywordsColor,

                commentColor,
                stringColor,
                ];
        }

        public void Receive(int index)
        {
            Receive(values[index]);
        }
        protected abstract void Receive(T value);

        public abstract void Done();

        public abstract T visibilityKeywordsColor { get; }
        public abstract T namespaceKeywordsColor { get; }
        public abstract T structureKeywordsColor { get; }
        public abstract T gotoKeywordsColor { get; }
        public abstract T queryKeywordsColor { get; }
        public abstract T exceptionKeywordsColor { get; }
        public abstract T checkedKeywordColor { get; }
        public abstract T unsafeKeywordsColor { get; }
        public abstract T valueTypeKeywordsColor { get; }
        public abstract T referenceTypeKeywordsColor { get; }
        public abstract T operatorKeywordsColor { get; }
        public abstract T parameterModifierColor { get; }
        public abstract T modifiersColor { get; }
        public abstract T accessorKeywordsColor { get; }
        public abstract T attributeKeywordsColor { get; }

        public abstract T referenceTypeColor { get; }
        public abstract T valueTypeColor { get; }
        public abstract T interfaceTypeColor { get; }
        public abstract T enumerationTypeColor { get; }
        // public abstract T typeParameterTypeColor { get; }
        public abstract T delegateTypeColor { get; }

        public abstract T methodCallColor { get; }
        public abstract T methodDeclarationColor { get; }
        public abstract T fieldDeclarationColor { get; }
        public abstract T fieldAccessColor { get; }
        public abstract T propertyDeclarationColor { get; }
        public abstract T propertyAccessColor { get; }
        public abstract T eventDeclarationColor { get; }
        public abstract T eventAccessColor { get; }

        public abstract T variableColor { get; }
        public abstract T parameterColor { get; }

        public abstract T valueKeywordColor { get; }
        public abstract T thisKeywordColor { get; }
        public abstract T trueKeywordColor { get; }
        public abstract T typeKeywordsColor { get; }
        public abstract T commentColor { get; }
        public abstract T stringColor { get; }
    }
    sealed class OldConsole : Palette<ConsoleColor?>
    {
        public override ConsoleColor? visibilityKeywordsColor => ConsoleColor.DarkBlue;
        public override ConsoleColor? namespaceKeywordsColor => ConsoleColor.DarkBlue;
        public override ConsoleColor? structureKeywordsColor => ConsoleColor.Blue;
        public override ConsoleColor? gotoKeywordsColor => ConsoleColor.Magenta;
        public override ConsoleColor? queryKeywordsColor => ConsoleColor.DarkBlue;
        public override ConsoleColor? exceptionKeywordsColor => ConsoleColor.Magenta;
        public override ConsoleColor? checkedKeywordColor => ConsoleColor.Blue;
        public override ConsoleColor? unsafeKeywordsColor => ConsoleColor.Blue;
        public override ConsoleColor? valueTypeKeywordsColor => ConsoleColor.Blue;
        public override ConsoleColor? referenceTypeKeywordsColor => ConsoleColor.Blue;
        public override ConsoleColor? operatorKeywordsColor => null;
        public override ConsoleColor? parameterModifierColor => ConsoleColor.Blue;
        public override ConsoleColor? modifiersColor => ConsoleColor.Blue;
        public override ConsoleColor? accessorKeywordsColor => ConsoleColor.Blue;
        public override ConsoleColor? attributeKeywordsColor => null;

        public override ConsoleColor? referenceTypeColor => ConsoleColor.Green;
        public override ConsoleColor? valueTypeColor => ConsoleColor.DarkGreen;
        public override ConsoleColor? interfaceTypeColor => ConsoleColor.DarkCyan;
        public override ConsoleColor? enumerationTypeColor => ConsoleColor.DarkCyan;
        //Consooverride leColor typeParameterTypeColor => ConsoleColor.Blue;
        public override ConsoleColor? delegateTypeColor => ConsoleColor.Green;

        public override ConsoleColor? methodCallColor => ConsoleColor.Yellow;
        public override ConsoleColor? methodDeclarationColor => ConsoleColor.Yellow;
        public override ConsoleColor? fieldDeclarationColor => ConsoleColor.Gray;
        public override ConsoleColor? fieldAccessColor => ConsoleColor.Gray;
        public override ConsoleColor? propertyDeclarationColor => null;
        public override ConsoleColor? propertyAccessColor => null;
        public override ConsoleColor? eventDeclarationColor => null;
        public override ConsoleColor? eventAccessColor => null;

        public override ConsoleColor? variableColor => null;
        public override ConsoleColor? parameterColor => null;

        public override ConsoleColor? valueKeywordColor => ConsoleColor.Blue;
        public override ConsoleColor? thisKeywordColor => ConsoleColor.Blue;
        public override ConsoleColor? trueKeywordColor => ConsoleColor.Blue;
        public override ConsoleColor? typeKeywordsColor => ConsoleColor.Blue;

        public override ConsoleColor? commentColor => ConsoleColor.DarkGreen;
        public override ConsoleColor? stringColor => ConsoleColor.DarkRed;

        public override void Done()
        {
            Console.ResetColor();
        }

        protected override void Receive(ConsoleColor? value)
        {
            if (value is { } v)
            {
                Console.ForegroundColor = v;
            }
        }
    }
    abstract class ColorfulPalette : Palette<Color>
    {
        public abstract Color Colorless { get; }
        public override Color visibilityKeywordsColor => new(86, 156, 214);
        public override Color namespaceKeywordsColor => new(86, 156, 214);
        public override Color structureKeywordsColor => new(86, 156, 214);
        public override Color gotoKeywordsColor => new(216, 160, 223);
        public override Color queryKeywordsColor => new(86, 156, 214);
        public override Color exceptionKeywordsColor => new(86, 156, 214);
        public override Color checkedKeywordColor => new(86, 156, 214);
        public override Color unsafeKeywordsColor => new(86, 156, 214);
        public override Color valueTypeKeywordsColor => new(86, 156, 214);
        public override Color referenceTypeKeywordsColor => new(86, 156, 214);
        public override Color operatorKeywordsColor => new(86, 156, 214);
        public override Color parameterModifierColor => new(86, 156, 214);
        public override Color modifiersColor => new(86, 156, 214);
        public override Color accessorKeywordsColor => new(86, 156, 214);
        public override Color attributeKeywordsColor => new(86, 156, 214);

        public override Color referenceTypeColor => new(78, 201, 176);
        public override Color valueTypeColor => new(134, 198, 145);
        public override Color interfaceTypeColor => new(184, 215, 163);
        public override Color enumerationTypeColor => new(184, 215, 163);
        //public override Color typeParameterTypeColor => new(184, 215, 163);
        public override Color delegateTypeColor => new(78, 201, 176);

        public override Color methodCallColor => new(220, 220, 170);
        public override Color methodDeclarationColor => new(220, 220, 170);
        public override Color fieldDeclarationColor => Colorless;
        public override Color fieldAccessColor => Colorless;
        public override Color propertyDeclarationColor => Colorless;
        public override Color propertyAccessColor => Colorless;
        public override Color eventDeclarationColor => Colorless;
        public override Color eventAccessColor => Colorless;

        public override Color variableColor => new(156, 220, 254);
        public override Color parameterColor => new(156, 220, 254);

        public override Color valueKeywordColor => new(86, 156, 214);
        public override Color thisKeywordColor => new(86, 156, 214);
        public override Color trueKeywordColor => new(86, 156, 214);
        public override Color typeKeywordsColor => new(86, 156, 214);
        public override Color commentColor => new(87, 166, 74);
        public override Color stringColor => new(214, 157, 133);
    }
    sealed class NewConsole : ColorfulPalette
    {
        public override Color Colorless => Color.White;

        public override void Done()
        {
            Soncole.Write("\x1B[0m");
        }

        protected override void Receive(Color value)
        {
            Soncole.Write($"\x1B[38;2;{value.R};{value.G};{value.B}m");
        }
    }
    interface IEditorColor<T>
    {
        public T Current { get; set; }
    }
    sealed class EditorColor : ColorfulPalette, IEditorColor<Color>
    {
        private static readonly Color blank = new(218, 218, 218);

        public override Color Colorless => blank;

        public Color Current { get; set; } = blank;

        public override void Done()
        {
            Current = blank;
        }

        protected override void Receive(Color value)
        {
            Current = value;
        }
    }
    sealed class ExternalEditorColor : Palette<PaletteIndex>, IEditorColor<PaletteIndex>
    {
        private static readonly PaletteIndex blank = PaletteIndex.Default;

        public PaletteIndex Current { get; set; } = blank;
        public override PaletteIndex visibilityKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex namespaceKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex structureKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex gotoKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex queryKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex exceptionKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex checkedKeywordColor => PaletteIndex.Keyword;
        public override PaletteIndex unsafeKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex valueTypeKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex referenceTypeKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex operatorKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex parameterModifierColor => PaletteIndex.Keyword;
        public override PaletteIndex modifiersColor => PaletteIndex.Keyword;
        public override PaletteIndex accessorKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex attributeKeywordsColor => PaletteIndex.Keyword;
        public override PaletteIndex referenceTypeColor => PaletteIndex.KnownIdentifier;
        public override PaletteIndex valueTypeColor => PaletteIndex.KnownIdentifier;
        public override PaletteIndex interfaceTypeColor => PaletteIndex.KnownIdentifier;
        public override PaletteIndex enumerationTypeColor => PaletteIndex.KnownIdentifier;
        public override PaletteIndex delegateTypeColor => PaletteIndex.KnownIdentifier;
        public override PaletteIndex methodCallColor => PaletteIndex.Identifier;
        public override PaletteIndex methodDeclarationColor => PaletteIndex.Identifier;
        public override PaletteIndex fieldDeclarationColor => PaletteIndex.Identifier;
        public override PaletteIndex fieldAccessColor => PaletteIndex.Identifier;
        public override PaletteIndex propertyDeclarationColor => PaletteIndex.Identifier;
        public override PaletteIndex propertyAccessColor => PaletteIndex.Identifier;
        public override PaletteIndex eventDeclarationColor => PaletteIndex.Identifier;
        public override PaletteIndex eventAccessColor => PaletteIndex.Identifier;
        public override PaletteIndex variableColor => PaletteIndex.Identifier;
        public override PaletteIndex parameterColor => PaletteIndex.Identifier;
        public override PaletteIndex valueKeywordColor => PaletteIndex.Keyword;
        public override PaletteIndex thisKeywordColor => PaletteIndex.Keyword;
        public override PaletteIndex trueKeywordColor => PaletteIndex.Keyword;
        public override PaletteIndex typeKeywordsColor => PaletteIndex.Keyword;

        public override PaletteIndex commentColor => PaletteIndex.Comment;
        public override PaletteIndex stringColor => PaletteIndex.String;

        public override void Done()
        {
            Current = blank;
        }

        protected override void Receive(PaletteIndex value)
        {
            Current = value;
        }
    }
    sealed class ExternalCustomEditorColor : IPalette, IEditorColor<PaletteIndex>
    {
        public static void InjectColor(TextEditor to)
        {
            var editorcolor = new EditorColor();
            to.SetColor((PaletteIndex)blank, Cast(editorcolor.Colorless));
            var fc = editorcolor.values;
            for (int i = 0; i < fc.Length; i++)
            {
                var c = fc[i];
                to.SetColor((PaletteIndex)(i + blank + 1), Cast(c));
            }
            static uint Cast(Color color)
            {
                return color.PackedValue;
            }
        }

        public void Receive(int index)
        {
            Current = (PaletteIndex)(blank + index + 1);
        }

        public void Done()
        {
            Current = (PaletteIndex)blank;
        }

        private static readonly int blank = (int)PaletteIndex.Custom;

        public PaletteIndex Current { get; set; } = (PaletteIndex)blank;
    }
    class EditorTextWriter<T>() : TextWriter
    {
        public override Encoding Encoding { get => Encoding.UTF8; }
        public StringBuilder str = new();
        public List<List<T>> colors = [[]];
        public required IEditorColor<T> Palette;

        public override void Write(char value)
        {
            if (value == '\r')
            {
                return;
            }
            str.Append(value);
            AddColor(value);
        }
        public void AddColor(char value)
        {
            if (value == '\n')
            {
                colors.Add([]);
            }
            else
            {
                colors[^1].Add(Palette.Current);
            }
        }

        public override void Write(string? value)
        {
            if (value == null)
            {
                return;
            }
            value = value.Replace("\r", null);
            str.Append(value);
            foreach (var i in value)
            {
                AddColor(i);
            }
        }

        public override void WriteLine(string? value)
        {
            if (value == null)
            {
                return;
            }
            value = value.Replace("\r", null);
            str.Append(value).Append('\n');
            foreach (var i in value)
            {
                AddColor(i);
            }
            colors.Add([]);
        }
    }

    //copied from CSharpHighlightingTokenWriter
    class MyTokenWriter(TextWriter writer, IDecompilerTypeSystem system, IPalette palette)
        : DecoratingTokenWriter(new TextTokenWriter(new PlainTextOutput(writer) { IndentationString = "    " }, new(), system))
    {
        const int visibilityKeywordsColor = 0;
        const int namespaceKeywordsColor = 1;
        const int structureKeywordsColor = 2;
        const int gotoKeywordsColor = 3;
        const int queryKeywordsColor = 4;
        const int exceptionKeywordsColor = 5;
        const int checkedKeywordColor = 6;
        const int unsafeKeywordsColor = 7;
        const int valueTypeKeywordsColor = 8;
        const int referenceTypeKeywordsColor = 9;
        const int operatorKeywordsColor = 10;
        const int parameterModifierColor = 11;
        const int modifiersColor = 12;
        const int accessorKeywordsColor = 13;
        const int attributeKeywordsColor = 14;

        const int referenceTypeColor = 15;
        const int valueTypeColor = 16;
        const int interfaceTypeColor = 17;
        const int enumerationTypeColor = 18;
        //const int typeParameterTypeColor = 19;
        const int delegateTypeColor = 20;

        const int methodCallColor = 21;
        const int methodDeclarationColor = 22;
        const int fieldDeclarationColor = 23;
        const int fieldAccessColor = 24;
        const int propertyDeclarationColor = 25;
        const int propertyAccessColor = 26;
        const int eventDeclarationColor = 27;
        const int eventAccessColor = 28;

        const int variableColor = 29;
        const int parameterColor = 30;

        const int valueKeywordColor = 31;
        const int thisKeywordColor = 32;
        const int trueKeywordColor = 33;
        const int typeKeywordsColor = 34;

        const int comment = 35;
        const int @string = 36;

        public override void WriteComment(CommentType commentType, string content)
        {
            BeginSpan(comment);
            base.WriteComment(commentType, content);
            EndSpan();
        }

        public override void WriteKeyword(Role role, string keyword)
        {
            int? color = null;
            switch (keyword)
            {
                case "namespace":
                case "using":
                    if (role == UsingStatement.UsingKeywordRole)
                        color = structureKeywordsColor;
                    else
                        color = namespaceKeywordsColor;
                    break;
                case "this":
                case "base":
                    color = thisKeywordColor;
                    break;
                case "true":
                case "false":
                    color = trueKeywordColor;
                    break;
                case "public":
                case "internal":
                case "protected":
                case "private":
                    color = visibilityKeywordsColor;
                    break;
                case "if":
                case "else":
                case "switch":
                case "case":
                case "default":
                case "while":
                case "do":
                case "for":
                case "foreach":
                case "lock":
                case "await":
                    color = structureKeywordsColor;
                    break;
                case "where":
                    if (nodeStack.PeekOrDefault() is QueryClause)
                        color = queryKeywordsColor;
                    else
                        color = structureKeywordsColor;
                    break;
                case "in":
                    if (nodeStack.PeekOrDefault() is ForeachStatement)
                        color = structureKeywordsColor;
                    else if (nodeStack.PeekOrDefault() is QueryClause)
                        color = queryKeywordsColor;
                    else
                        color = parameterModifierColor;
                    break;
                case "as":
                case "is":
                case "new":
                case "sizeof":
                case "typeof":
                case "nameof":
                case "stackalloc":
                    color = typeKeywordsColor;
                    break;
                case "with":
                    if (role == WithInitializerExpression.WithKeywordRole)
                        color = typeKeywordsColor;
                    break;
                case "try":
                case "throw":
                case "catch":
                case "finally":
                    color = exceptionKeywordsColor;
                    break;
                case "when":
                    if (role == CatchClause.WhenKeywordRole)
                        color = exceptionKeywordsColor;
                    break;
                case "get":
                case "set":
                case "add":
                case "remove":
                case "init":
                    if (role == PropertyDeclaration.GetKeywordRole ||
                        role == PropertyDeclaration.SetKeywordRole ||
                        role == PropertyDeclaration.InitKeywordRole ||
                        role == CustomEventDeclaration.AddKeywordRole ||
                        role == CustomEventDeclaration.RemoveKeywordRole)
                        color = accessorKeywordsColor;
                    break;
                case "abstract":
                case "const":
                case "event":
                case "extern":
                case "override":
                case "sealed":
                case "static":
                case "virtual":
                case "volatile":
                case "async":
                case "partial":
                    color = modifiersColor;
                    break;
                case "readonly":
                    if (role == ComposedType.ReadonlyRole)
                        color = parameterModifierColor;
                    else
                        color = modifiersColor;
                    break;
                case "checked":
                case "unchecked":
                    color = checkedKeywordColor;
                    break;
                case "fixed":
                case "unsafe":
                    color = unsafeKeywordsColor;
                    break;
                case "enum":
                case "struct":
                    color = valueTypeKeywordsColor;
                    break;
                case "class":
                case "interface":
                case "delegate":
                case "extension":
                    color = referenceTypeKeywordsColor;
                    break;
                case "record":
                    color = role == Roles.RecordKeyword ? referenceTypeKeywordsColor : valueTypeKeywordsColor;
                    break;
                case "select":
                case "group":
                case "by":
                case "into":
                case "from":
                case "orderby":
                case "let":
                case "join":
                case "on":
                case "equals":
                    if (nodeStack.PeekOrDefault() is QueryClause)
                        color = queryKeywordsColor;
                    break;
                case "ascending":
                case "descending":
                    if (nodeStack.PeekOrDefault() is QueryOrdering)
                        color = queryKeywordsColor;
                    break;
                case "explicit":
                case "implicit":
                case "operator":
                    color = operatorKeywordsColor;
                    break;
                case "params":
                case "ref":
                case "out":
                case "scoped":
                    color = parameterModifierColor;
                    break;
                case "break":
                case "continue":
                case "goto":
                case "yield":
                case "return":
                    color = gotoKeywordsColor;
                    break;
            }
            if (nodeStack.PeekOrDefault() is AttributeSection)
                color = attributeKeywordsColor;
            if (color != null)
            {
                BeginSpan(color.Value);
            }
            base.WriteKeyword(role, keyword);
            if (color != null)
            {
                EndSpan();
            }
        }

        public override void WritePrimitiveType(string type)
        {
            int? color = null;
            switch (type)
            {
                case "new":
                case "notnull":
                    // Not sure if reference type or value type
                    color = referenceTypeKeywordsColor;
                    break;
                case "bool":
                case "byte":
                case "char":
                case "decimal":
                case "double":
                case "enum":
                case "float":
                case "int":
                case "long":
                case "sbyte":
                case "short":
                case "struct":
                case "uint":
                case "ushort":
                case "ulong":
                case "unmanaged":
                case "nint":
                case "nuint":
                    color = valueTypeKeywordsColor;
                    break;
                case "class":
                case "object":
                case "string":
                case "void":
                case "dynamic":
                    color = referenceTypeKeywordsColor;
                    break;
            }
            if (color != null)
            {
                BeginSpan(color.Value);
            }
            base.WritePrimitiveType(type);
            if (color != null)
            {
                EndSpan();
            }
        }

        public override void WriteIdentifier(Identifier identifier)
        {
            int? color = null;
            if (identifier.Parent?.GetResolveResult() is ILVariableResolveResult rr)
            {
                if (rr.Variable.Kind == VariableKind.Parameter)
                {
                    if (identifier.Name == "value"
                        && identifier.Ancestors.OfType<Accessor>().FirstOrDefault() is { } accessor
                        && accessor.Role != PropertyDeclaration.GetterRole)
                    {
                        color = valueKeywordColor;
                    }
                    else
                    {
                        color = parameterColor;
                    }
                }
                else
                {
                    color = variableColor;
                }
            }
            if (identifier.Parent is AstType)
            {
                switch (identifier.Name)
                {
                    case "var":
                        color = queryKeywordsColor;
                        break;
                    case "global":
                        color = structureKeywordsColor;
                        break;
                }
            }
            switch (GetCurrentDefinition())
            {
                case ITypeDefinition t:
                    color = ApplyTypeColor(t);
                    break;
                case IMethod:
                    color = methodDeclarationColor;
                    break;
                case IField:
                    color = fieldDeclarationColor;
                    break;
                case IProperty:
                    color = propertyDeclarationColor;
                    break;
                case IEvent:
                    color = eventDeclarationColor;
                    break;
            }
            switch (GetCurrentMemberReference())
            {
                case IType t:
                    color = ApplyTypeColor(t);
                    break;
                case IMethod m:
                    color = methodCallColor;
                    if (m.IsConstructor)
                        color = ApplyTypeColor(m.DeclaringType);
                    break;
                case IField:
                    color = fieldAccessColor;
                    break;
                case IProperty:
                    color = propertyAccessColor;
                    break;
                case IEvent:
                    color = eventAccessColor;
                    break;
            }
            if (color != null)
            {
                BeginSpan(color.Value);
            }
            base.WriteIdentifier(identifier);
            if (color != null)
            {
                EndSpan();
            }
        }

        int? ApplyTypeColor(IType type)
        {
            int? color = null;
            switch (type?.Kind)
            {
                case TypeKind.Delegate:
                    color = delegateTypeColor;
                    break;
                case TypeKind.Class:
                    color = referenceTypeColor;
                    break;
                case TypeKind.Interface:
                    color = interfaceTypeColor;
                    break;
                case TypeKind.Enum:
                    color = enumerationTypeColor;
                    break;
                case TypeKind.Struct:
                    color = valueTypeColor;
                    break;
            }
            return color;
        }

        public override void WritePrimitiveValue(object value, LiteralFormat format)
        {
            int? color = null;
            if (value is null)
            {
                color = valueKeywordColor;
            }
            if (value is true || value is false)
            {
                color = trueKeywordColor;
            }
            if (value is string or char)
            {
                color = @string;
            }
            if (color != null)
            {
                BeginSpan(color.Value);
            }
            base.WritePrimitiveValue(value, format);
            if (color != null)
            {
                EndSpan();
            }
        }

        ISymbol? GetCurrentDefinition()
        {
            if (nodeStack == null || nodeStack.Count == 0)
                return null;

            var node = nodeStack.Peek();
            if (node is Identifier)
                node = node.Parent;
            if (TextTokenWriter.IsDefinition(ref node))
                return node.GetSymbol();

            return null;
        }

        ISymbol? GetCurrentMemberReference()
        {
            if (nodeStack == null || nodeStack.Count == 0)
                return null;

            AstNode node = nodeStack.Peek();
            var symbol = node.GetSymbol();
            if (symbol == null && node.Role == Roles.TargetExpression && node.Parent is InvocationExpression)
            {
                symbol = node.Parent.GetSymbol();
            }
            if (symbol != null && node.Role == Roles.Type && node.Parent is ObjectCreateExpression)
            {
                var ctorSymbol = node.Parent.GetSymbol();
                if (ctorSymbol != null)
                    symbol = ctorSymbol;
            }
            if (node is IdentifierExpression && node.Role == Roles.TargetExpression && node.Parent is InvocationExpression && symbol is IMember member)
            {
                var declaringType = member.DeclaringType;
                if (declaringType != null && declaringType.Kind == TypeKind.Delegate)
                    return null;
            }
            return symbol;
        }

        readonly Stack<AstNode> nodeStack = new();

        public override void StartNode(AstNode node)
        {
            nodeStack.Push(node);
            base.StartNode(node);
        }

        public override void EndNode(AstNode node)
        {
            base.EndNode(node);
            nodeStack.Pop();
        }

        readonly Stack<ConsoleColor> colorStack = new();
        ConsoleColor currentColor = new();
        int currentColorBegin = -1;

        private void BeginSpan(int ConsoleColor)
        {
            palette.Receive(ConsoleColor);
        }

        private void EndSpan()
        {
            palette.Done();
        }
    }
}
