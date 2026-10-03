// 受限 C# 子集 → GDScript 3 转译器（原型）。
//
//   dotnet run --project transpiler -- <core 目录> <输出目录>
//
// 1. 用 Roslyn 编译 core/*.cs（含运行库 Rt.cs），拿到完整的类型信息
// 2. 子集检查：语法和语义两层，违规一律报 文件:行 并失败
// 3. 除 Rt.cs 外每个类型生成一个 .gd：静态类 → 静态函数模块，实例类 → 只有字段的 Reference
//
// 子集（够写日志编解码和统计核心，再多的等真用到再加）：
//   类型    long/int、double、bool、string、List<T>、Dictionary<string,V>、核心里的类
//   语句    局部变量、赋值（含 += 等）、if/else、while、foreach（只对 List）、return/break/continue、x++ 作语句
//   表达式  算术、比较、逻辑、位运算、三元、(long)/(int)/(double) 转换、new、下标
//   调用    核心的静态方法、Rt.*，以及白名单里的字符串 / List / Dictionary 成员
//   不允许  float/char/decimal、struct/接口/继承/构造函数、ref/out/in、lambda/委托、异常、LINQ、
//           for/do/switch/goto、字符串插值、?. ??、静态可变字段、字符串与非字符串相加
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法: Transpiler <core 目录> <输出目录>");
            return 2;
        }
        var coreDir = Path.GetFullPath(args[0]);
        var outDir = Path.GetFullPath(args[1]);

        var trees = Directory.GetFiles(coreDir, "*.cs").OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p)).ToList();

        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var comp = CSharpCompilation.Create("core", trees, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            foreach (var e in errors) Console.Error.WriteLine(e);
            return 1;
        }

        var emitters = new List<FileEmitter>();
        foreach (var tree in trees)
        {
            if (Path.GetFileName(tree.FilePath) == "Rt.cs") continue;   // 运行库各语言手写
            var model = comp.GetSemanticModel(tree);
            foreach (var type in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
                emitters.Add(new FileEmitter(model, type));
        }

        var violations = emitters.SelectMany(e => e.Check()).ToList();
        if (violations.Count > 0)
        {
            Console.Error.WriteLine($"子集检查失败（{violations.Count} 处）：");
            foreach (var v in violations) Console.Error.WriteLine("  " + v);
            return 1;
        }

        Directory.CreateDirectory(outDir);
        foreach (var e in emitters)
        {
            var path = Path.Combine(outDir, Names.FileOf(e.TypeName));
            File.WriteAllText(path, e.Emit(), new UTF8Encoding(false));
            Console.WriteLine($"{Path.GetFileName(e.SourcePath)} → {Path.GetFileName(path)}");
        }
        return 0;
    }
}

internal static class Names
{
    public static string FileOf(string typeName)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < typeName.Length; i++)
        {
            var c = typeName[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb + ".gd";
    }

    private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "and", "or", "not", "in", "is", "as", "var", "func", "const", "class", "class_name", "extends", "static",
        "self", "pass", "break", "continue", "return", "if", "elif", "else", "for", "while", "match", "true", "false",
        "null", "signal", "export", "tool", "yield", "assert", "breakpoint", "onready", "remote", "master", "puppet",
        "remotesync", "mastersync", "puppetsync", "setget", "enum", "void", "bool", "int", "float", "String", "Array",
        "Dictionary", "str", "len", "range", "print", "load", "preload", "abs", "max", "min", "floor", "round", "ceil",
        "PI", "TAU", "INF", "NAN", "funcref", "typeof", "convert", "Object", "Reference", "Error", "Variant",
    };

    public static string Local(string name) => Reserved.Contains(name) ? name + "_" : name;
}

internal sealed class FileEmitter
{
    private readonly SemanticModel _m;
    private readonly TypeDeclarationSyntax _type;
    private readonly INamedTypeSymbol _symbol;
    private readonly SortedSet<string> _deps = new SortedSet<string>(StringComparer.Ordinal);
    private readonly List<string> _violations = new List<string>();

    public FileEmitter(SemanticModel model, TypeDeclarationSyntax type)
    {
        _m = model;
        _type = type;
        _symbol = model.GetDeclaredSymbol(type);
    }

    public string TypeName => _symbol.Name;
    public string SourcePath => _type.SyntaxTree.FilePath;
    private bool IsModule => _symbol.IsStatic;

    // ================================================================ 子集检查

    public List<string> Check()
    {
        foreach (var node in _type.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case StructDeclarationSyntax _:
                case InterfaceDeclarationSyntax _:
                case RecordDeclarationSyntax _:
                case DelegateDeclarationSyntax _:
                case EnumDeclarationSyntax _:
                case ConstructorDeclarationSyntax _:
                case PropertyDeclarationSyntax _:
                case LambdaExpressionSyntax _:
                case AnonymousMethodExpressionSyntax _:
                case LocalFunctionStatementSyntax _:
                case TryStatementSyntax _:
                case ThrowStatementSyntax _:
                case ThrowExpressionSyntax _:
                case QueryExpressionSyntax _:
                case ForStatementSyntax _:
                case DoStatementSyntax _:
                case SwitchStatementSyntax _:
                case SwitchExpressionSyntax _:
                case GotoStatementSyntax _:
                case InterpolatedStringExpressionSyntax _:
                case ConditionalAccessExpressionSyntax _:
                case IsPatternExpressionSyntax _:
                case CheckedExpressionSyntax _:
                    Bad(node, "不在子集里：" + node.Kind());
                    break;
                case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression):
                    Bad(node, "不在子集里：??");
                    break;
                case BaseListSyntax _:
                    Bad(node, "不允许继承 / 实现接口");
                    break;
                case ParameterSyntax p when p.Modifiers.Count > 0:
                    Bad(node, "参数不能带 ref / out / in / params");
                    break;
                case PostfixUnaryExpressionSyntax u when !(u.Parent is ExpressionStatementSyntax):
                    Bad(node, "++ / -- 只能单独成句");
                    break;
                case PrefixUnaryExpressionSyntax u when (u.IsKind(SyntaxKind.PreIncrementExpression) ||
                                                         u.IsKind(SyntaxKind.PreDecrementExpression)) &&
                                                        !(u.Parent is ExpressionStatementSyntax):
                    Bad(node, "++ / -- 只能单独成句");
                    break;
                case FieldDeclarationSyntax f when f.Modifiers.Any(SyntaxKind.StaticKeyword) && !f.Modifiers.Any(SyntaxKind.ConstKeyword):
                    Bad(node, "不允许静态可变字段（GDScript 3 没有静态变量）");
                    break;
                case ForEachStatementSyntax fe when !IsList(_m.GetTypeInfo(fe.Expression).Type):
                    Bad(node, "foreach 只能遍历 List（Map 的遍历顺序各语言不一）");
                    break;
                case ExpressionSyntax e:
                    CheckType(e);
                    break;
            }
        }
        // 方法体之外也不允许出现 float / char
        foreach (var v in _type.DescendantNodes().OfType<VariableDeclarationSyntax>()) CheckTypeSymbol(v, _m.GetTypeInfo(v.Type).Type);
        foreach (var p in _type.DescendantNodes().OfType<ParameterSyntax>()) CheckTypeSymbol(p, _m.GetDeclaredSymbol(p)?.Type);
        if (!IsModule && _type.Members.Any(x => x is MethodDeclarationSyntax))
            Bad(_type, "实例类只能有字段（方法写成静态类里的函数）");
        if (IsModule && _type.Members.OfType<FieldDeclarationSyntax>().Any(f => !f.Modifiers.Any(SyntaxKind.ConstKeyword)))
            Bad(_type, "静态类里只能有 const 字段");
        // 先跑一遍生成，生成器不认识的写法也算违规
        Emit();
        return _violations;
    }

    private void CheckType(ExpressionSyntax e)
    {
        var t = _m.GetTypeInfo(e).Type;
        CheckTypeSymbol(e, t);
        if (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression) && IsString(_m.GetTypeInfo(b).Type))
        {
            if (!IsString(_m.GetTypeInfo(b.Left).Type) || !IsString(_m.GetTypeInfo(b.Right).Type))
                Bad(e, "字符串只能和字符串相加（数字先过 Rt.LToS / NumText）");
        }
    }

    private void CheckTypeSymbol(SyntaxNode at, ITypeSymbol t)
    {
        if (t == null) return;
        switch (t.SpecialType)
        {
            case SpecialType.System_Single:
                Bad(at, "不允许 float：其它语言的浮点都是 64 位，用 double");
                break;
            case SpecialType.System_Char:
                Bad(at, "不允许 char：字符串只当整体用");
                break;
            case SpecialType.System_Decimal:
                Bad(at, "不允许 decimal");
                break;
        }
        if (t is INamedTypeSymbol nt && nt.IsGenericType)
            foreach (var a in nt.TypeArguments) CheckTypeSymbol(at, a);
    }

    private void Bad(SyntaxNode at, string why)
    {
        var line = at.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var msg = $"{Path.GetFileName(at.SyntaxTree.FilePath)}:{line}  {why}";
        if (!_violations.Contains(msg)) _violations.Add(msg);
    }

    // ================================================================ 生成

    private StringBuilder _sb;
    private int _indent;

    public string Emit()
    {
        _sb = new StringBuilder();
        _deps.Clear();
        var body = new StringBuilder();
        var saved = _sb;
        _sb = body;

        foreach (var member in _type.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax f:
                    EmitField(f);
                    break;
                case MethodDeclarationSyntax md:
                    EmitMethod(md);
                    break;
                default:
                    Bad(member, "不在子集里的成员：" + member.Kind());
                    break;
            }
        }

        _sb = saved;
        _sb.Append("extends Reference\n\n");
        _sb.Append($"# 由 {Path.GetFileName(SourcePath)} 生成（受限 C# → GDScript 3），不要手改。\n");
        var deps = _deps.Where(d => d != TypeName).ToList();
        if (deps.Count > 0)
        {
            _sb.Append('\n');
            foreach (var d in deps)
                _sb.Append($"const {d} = preload(\"{(d == "Rt" ? "rt.gd" : Names.FileOf(d))}\")\n");
        }
        _sb.Append(body);
        return _sb.ToString();
    }

    private string Src(SyntaxNode n) => $"{Path.GetFileName(SourcePath)}:{n.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";

    private void EmitField(FieldDeclarationSyntax f)
    {
        var isConst = f.Modifiers.Any(SyntaxKind.ConstKeyword);
        foreach (var v in f.Declaration.Variables)
        {
            var type = _m.GetTypeInfo(f.Declaration.Type).Type;
            var init = v.Initializer != null ? Expr(v.Initializer.Value) : DefaultOf(type, v);
            Line((isConst ? "const " : "var ") + (isConst ? v.Identifier.Text : Names.Local(v.Identifier.Text)) + " = " + init);
        }
    }

    private void EmitMethod(MethodDeclarationSyntax md)
    {
        if (!md.Modifiers.Any(SyntaxKind.StaticKeyword)) Bad(md, "只支持静态方法");
        if (md.TypeParameterList != null) Bad(md, "不允许自定义泛型");
        _sb.Append('\n');
        Line($"# {Src(md)}");
        var ps = string.Join(", ", md.ParameterList.Parameters.Select(p => Names.Local(p.Identifier.Text)));
        Line($"static func {md.Identifier.Text}({ps}):");
        _indent++;
        if (md.ExpressionBody != null)
        {
            var returnsVoid = _m.GetDeclaredSymbol(md).ReturnsVoid;
            Line((returnsVoid ? "" : "return ") + Expr(md.ExpressionBody.Expression));
        }
        else
        {
            BlockBody(md.Body);
        }
        _indent--;
    }

    private void Line(string s) => _sb.Append('\t', _indent).Append(s).Append('\n');

    private void BlockBody(StatementSyntax s)
    {
        var start = _sb.Length;
        if (s is BlockSyntax b)
            foreach (var x in b.Statements) Stmt(x);
        else
            Stmt(s);
        if (_sb.Length == start) Line("pass");
    }

    private void Stmt(StatementSyntax s)
    {
        switch (s)
        {
            case BlockSyntax b:
                foreach (var x in b.Statements) Stmt(x);
                break;

            case LocalDeclarationStatementSyntax ld:
                if (ld.IsConst) Bad(s, "局部 const 不支持");
                foreach (var v in ld.Declaration.Variables)
                {
                    var type = _m.GetTypeInfo(ld.Declaration.Type).Type;
                    var init = v.Initializer != null ? Expr(v.Initializer.Value) : DefaultOf(type, v);
                    Line($"var {Names.Local(v.Identifier.Text)} = {init}");
                }
                break;

            case ExpressionStatementSyntax es:
                Line(StmtExpr(es.Expression));
                break;

            case IfStatementSyntax ifs:
                Line($"if {Cond(ifs.Condition)}:");
                Indented(ifs.Statement);
                var els = ifs.Else;
                while (els != null)
                {
                    if (els.Statement is IfStatementSyntax elif)
                    {
                        Line($"elif {Cond(elif.Condition)}:");
                        Indented(elif.Statement);
                        els = elif.Else;
                    }
                    else
                    {
                        Line("else:");
                        Indented(els.Statement);
                        els = null;
                    }
                }
                break;

            case WhileStatementSyntax ws:
                Line($"while {Cond(ws.Condition)}:");
                Indented(ws.Statement);
                break;

            case ForEachStatementSyntax fe:
                Line($"for {Names.Local(fe.Identifier.Text)} in {Expr(fe.Expression)}:");
                Indented(fe.Statement);
                break;

            case ReturnStatementSyntax rs:
                Line(rs.Expression == null ? "return" : "return " + Expr(rs.Expression));
                break;

            case BreakStatementSyntax _:
                Line("break");
                break;

            case ContinueStatementSyntax _:
                Line("continue");
                break;

            default:
                Bad(s, "不在子集里的语句：" + s.Kind());
                break;
        }
    }

    private void Indented(StatementSyntax s)
    {
        _indent++;
        BlockBody(s);
        _indent--;
    }

    private string Cond(ExpressionSyntax e) => Strip(Expr(e));

    private static string Strip(string s)
    {
        // 去掉最外层一对括号（确实是同一对时）
        if (s.Length < 2 || s[0] != '(' || s[s.Length - 1] != ')') return s;
        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (s[i] == '"') return s;   // 有字符串字面量就不冒险
            if (depth == 0 && i < s.Length - 1) return s;
        }
        return s.Substring(1, s.Length - 2);
    }

    private string StmtExpr(ExpressionSyntax e)
    {
        switch (e)
        {
            case AssignmentExpressionSyntax a:
            {
                var op = a.Kind() switch
                {
                    SyntaxKind.SimpleAssignmentExpression => "=",
                    SyntaxKind.AddAssignmentExpression => "+=",
                    SyntaxKind.SubtractAssignmentExpression => "-=",
                    SyntaxKind.MultiplyAssignmentExpression => "*=",
                    SyntaxKind.OrAssignmentExpression => "|=",
                    SyntaxKind.AndAssignmentExpression => "&=",
                    _ => null,
                };
                if (op == null)
                {
                    Bad(e, "不支持的赋值：" + a.Kind());
                    return "pass";
                }
                if (op != "=" && IsString(_m.GetTypeInfo(a.Left).Type) && !IsString(_m.GetTypeInfo(a.Right).Type))
                    Bad(e, "字符串只能和字符串相加");
                return $"{Expr(a.Left)} {op} {Strip(Expr(a.Right))}";
            }
            case PostfixUnaryExpressionSyntax u:
                return Expr(u.Operand) + (u.IsKind(SyntaxKind.PostIncrementExpression) ? " += 1" : " -= 1");
            case PrefixUnaryExpressionSyntax u when u.IsKind(SyntaxKind.PreIncrementExpression) || u.IsKind(SyntaxKind.PreDecrementExpression):
                return Expr(u.Operand) + (u.IsKind(SyntaxKind.PreIncrementExpression) ? " += 1" : " -= 1");
            case InvocationExpressionSyntax _:
                return Expr(e);
            default:
                Bad(e, "不能单独成句的表达式：" + e.Kind());
                return "pass";
        }
    }

    private string Expr(ExpressionSyntax e)
    {
        switch (e)
        {
            case LiteralExpressionSyntax lit:
                return Literal(lit);

            case ParenthesizedExpressionSyntax p:
                return Expr(p.Expression);   // 二元表达式自己带括号

            case IdentifierNameSyntax id:
                return Identifier(id);

            case MemberAccessExpressionSyntax ma:
                return MemberAccess(ma);

            case InvocationExpressionSyntax inv:
                return Invocation(inv);

            case ObjectCreationExpressionSyntax oc:
                return Creation(oc, _m.GetTypeInfo(oc).Type, oc.ArgumentList);

            case ImplicitObjectCreationExpressionSyntax ioc:
                return Creation(ioc, _m.GetTypeInfo(ioc).Type, ioc.ArgumentList);

            case ElementAccessExpressionSyntax ea:
            {
                var t = _m.GetTypeInfo(ea.Expression).Type;
                if (!IsList(t) && !IsDict(t)) Bad(e, "下标只能用在 List / Dictionary 上");
                if (ea.ArgumentList.Arguments.Count != 1) Bad(e, "只支持一维下标");
                return $"{Expr(ea.Expression)}[{Strip(Expr(ea.ArgumentList.Arguments[0].Expression))}]";
            }

            case BinaryExpressionSyntax b:
                return Binary(b);

            case PrefixUnaryExpressionSyntax u when u.IsKind(SyntaxKind.UnaryMinusExpression):
                return $"(-{Expr(u.Operand)})";

            case PrefixUnaryExpressionSyntax u when u.IsKind(SyntaxKind.LogicalNotExpression):
                return $"(not {Expr(u.Operand)})";

            case ConditionalExpressionSyntax c:
                return $"({Expr(c.WhenTrue)} if {Expr(c.Condition)} else {Expr(c.WhenFalse)})";

            case CastExpressionSyntax cast:
                return Cast(cast);

            default:
                Bad(e, "不在子集里的表达式：" + e.Kind());
                return "null";
        }
    }

    private string Literal(LiteralExpressionSyntax lit)
    {
        switch (lit.Kind())
        {
            case SyntaxKind.StringLiteralExpression:
                return Quote(lit.Token.ValueText);
            case SyntaxKind.TrueLiteralExpression:
                return "true";
            case SyntaxKind.FalseLiteralExpression:
                return "false";
            case SyntaxKind.NullLiteralExpression:
                return "null";
            case SyntaxKind.NumericLiteralExpression:
            {
                var t = _m.GetTypeInfo(lit).ConvertedType ?? _m.GetTypeInfo(lit).Type;
                var text = lit.Token.Text.TrimEnd('d', 'D', 'l', 'L', 'u', 'U');
                if (IsDouble(t) && text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0) text += ".0";
                return text;
            }
            default:
                Bad(lit, "不在子集里的字面量：" + lit.Kind());
                return "null";
        }
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    private string Identifier(IdentifierNameSyntax id)
    {
        var sym = _m.GetSymbolInfo(id).Symbol;
        switch (sym)
        {
            case ILocalSymbol _:
            case IParameterSymbol _:
                return Names.Local(id.Identifier.Text);
            case IFieldSymbol f when f.IsConst:
                return QualifiedStatic(f.ContainingType, f.Name);
            default:
                Bad(id, $"不认识的名字 {id.Identifier.Text}（{sym?.Kind}）");
                return id.Identifier.Text;
        }
    }

    private string QualifiedStatic(INamedTypeSymbol owner, string name)
    {
        if (SymbolEqualityComparer.Default.Equals(owner, _symbol)) return name;
        if (!IsCore(owner)) Bad(_type, $"引用了核心以外的类型 {owner.Name}");
        _deps.Add(owner.Name);
        return owner.Name + "." + name;
    }

    private string MemberAccess(MemberAccessExpressionSyntax ma)
    {
        var sym = _m.GetSymbolInfo(ma).Symbol;
        var recvType = _m.GetTypeInfo(ma.Expression).Type;
        switch (sym)
        {
            case IPropertySymbol p when p.Name == "Length" && IsString(recvType):
                return Expr(ma.Expression) + ".length()";
            case IPropertySymbol p when p.Name == "Count" && (IsList(recvType) || IsDict(recvType)):
                return Expr(ma.Expression) + ".size()";
            case IFieldSymbol f when f.IsConst:
                return QualifiedStatic(f.ContainingType, f.Name);
            case IFieldSymbol f when !f.IsStatic && IsCore(f.ContainingType):
                return Expr(ma.Expression) + "." + Names.Local(f.Name);
            default:
                Bad(ma, $"成员不在白名单里：{sym?.ToDisplayString() ?? ma.ToString()}");
                return ma.ToString();
        }
    }

    private string Invocation(InvocationExpressionSyntax inv)
    {
        var sym = _m.GetSymbolInfo(inv).Symbol as IMethodSymbol;
        if (sym == null)
        {
            Bad(inv, "调用解析不出来：" + inv);
            return "null";
        }
        // StringComparison 参数只用来声明比较方式，不生成
        var args = inv.ArgumentList.Arguments
            .Where(a => _m.GetTypeInfo(a.Expression).Type?.ToDisplayString() != "System.StringComparison")
            .Select(a => Strip(Expr(a.Expression))).ToList();
        var owner = sym.ContainingType;

        if (sym.IsStatic && IsCore(owner))
        {
            if (owner.Name == "Rt") _deps.Add("Rt");
            var call = SymbolEqualityComparer.Default.Equals(owner, _symbol) ? sym.Name : QualifiedStatic(owner, sym.Name);
            return $"{call}({string.Join(", ", args)})";
        }

        var recv = inv.Expression is MemberAccessExpressionSyntax ma ? Expr(ma.Expression) : null;
        var recvType = inv.Expression is MemberAccessExpressionSyntax ma2 ? _m.GetTypeInfo(ma2.Expression).Type : null;

        if (IsString(recvType))
        {
            bool Ordinal() => inv.ArgumentList.Arguments.Count == 2 &&
                              inv.ArgumentList.Arguments[1].Expression.ToString().EndsWith("Ordinal", StringComparison.Ordinal);
            bool OneStringArg() => sym.Parameters.Length >= 1 && sym.Parameters[0].Type.SpecialType == SpecialType.System_String;
            switch (sym.Name)
            {
                case "StartsWith" when OneStringArg() && Ordinal():
                    return $"{recv}.begins_with({args[0]})";
                case "EndsWith" when OneStringArg() && Ordinal():
                    return $"{recv}.ends_with({args[0]})";
                case "IndexOf" when OneStringArg() && Ordinal():
                    return $"{recv}.find({args[0]})";
                case "Replace" when OneStringArg() && sym.Parameters.Length == 2:
                    return $"{recv}.replace({args[0]}, {args[1]})";
                case "Substring" when sym.Parameters.Length == 1:
                    return $"{recv}.substr({args[0]}, -1)";
                case "Substring" when sym.Parameters.Length == 2:
                    return $"{recv}.substr({args[0]}, {args[1]})";
                case "StartsWith":
                case "EndsWith":
                case "IndexOf":
                    Bad(inv, $"字符串的 {sym.Name} 必须用 string 参数并写明 StringComparison.Ordinal（默认按区域比较）");
                    return "null";
            }
        }
        if (IsList(recvType) && sym.Name == "Add") return $"{recv}.append({args[0]})";
        if (IsDict(recvType) && sym.Name == "ContainsKey") return $"{recv}.has({args[0]})";

        Bad(inv, $"调用不在白名单里：{sym.ToDisplayString()}");
        return "null";
    }

    private string Creation(ExpressionSyntax at, ITypeSymbol t, ArgumentListSyntax argList)
    {
        if (argList != null && argList.Arguments.Count > 0)
        {
            Bad(at, "new 不能带参数");
            return "null";
        }
        if (IsList(t)) return "[]";
        if (IsDict(t)) return "{}";
        if (t is INamedTypeSymbol nt && IsCore(nt) && !nt.IsStatic)
        {
            _deps.Add(nt.Name);
            return nt.Name + ".new()";
        }
        Bad(at, $"不能 new {t?.ToDisplayString()}");
        return "null";
    }

    private string Binary(BinaryExpressionSyntax b)
    {
        var lt = _m.GetTypeInfo(b.Left).Type;
        var rt = _m.GetTypeInfo(b.Right).Type;
        var l = Expr(b.Left);
        var r = Expr(b.Right);
        switch (b.Kind())
        {
            case SyntaxKind.AddExpression: return $"({l} + {r})";
            case SyntaxKind.SubtractExpression: return $"({l} - {r})";
            case SyntaxKind.MultiplyExpression: return $"({l} * {r})";
            case SyntaxKind.DivideExpression:
                // 整数 / 整数：两边都是向 0 截断；GDScript 里 int / int 也是整数除法
                return $"({l} / {r})";
            case SyntaxKind.ModuloExpression:
                return IsIntegral(lt) && IsIntegral(rt) ? $"({l} % {r})" : $"fmod({l}, {r})";
            case SyntaxKind.EqualsExpression: return $"({l} == {r})";
            case SyntaxKind.NotEqualsExpression: return $"({l} != {r})";
            case SyntaxKind.LessThanExpression: return $"({l} < {r})";
            case SyntaxKind.LessThanOrEqualExpression: return $"({l} <= {r})";
            case SyntaxKind.GreaterThanExpression: return $"({l} > {r})";
            case SyntaxKind.GreaterThanOrEqualExpression: return $"({l} >= {r})";
            case SyntaxKind.LogicalAndExpression: return $"({l} and {r})";
            case SyntaxKind.LogicalOrExpression: return $"({l} or {r})";
            case SyntaxKind.BitwiseAndExpression: return $"({l} & {r})";
            case SyntaxKind.BitwiseOrExpression: return $"({l} | {r})";
            case SyntaxKind.ExclusiveOrExpression: return $"({l} ^ {r})";
            case SyntaxKind.LeftShiftExpression: return $"({l} << {r})";
            case SyntaxKind.RightShiftExpression: return $"({l} >> {r})";
            default:
                Bad(b, "不在子集里的运算：" + b.Kind());
                return "null";
        }
    }

    private string Cast(CastExpressionSyntax cast)
    {
        var to = _m.GetTypeInfo(cast.Type).Type;
        var from = _m.GetTypeInfo(cast.Expression).Type;
        var inner = Strip(Expr(cast.Expression));
        if (IsIntegral(to) && IsDouble(from)) return $"int({inner})";      // 都是向 0 截断
        if (IsIntegral(to) && IsIntegral(from)) return Expr(cast.Expression); // GDScript 只有 64 位 int
        if (IsDouble(to)) return $"float({inner})";
        Bad(cast, $"不支持的转换 {from?.ToDisplayString()} → {to?.ToDisplayString()}");
        return inner;
    }

    private string DefaultOf(ITypeSymbol t, SyntaxNode at)
    {
        if (IsIntegral(t)) return "0";
        if (IsDouble(t)) return "0.0";
        if (t?.SpecialType == SpecialType.System_Boolean) return "false";
        if (IsString(t)) return "\"\"";
        return "null";
    }

    // ================================================================ 类型判断

    private static bool IsString(ITypeSymbol t) => t?.SpecialType == SpecialType.System_String;
    private static bool IsDouble(ITypeSymbol t) => t?.SpecialType == SpecialType.System_Double;

    private static bool IsIntegral(ITypeSymbol t) =>
        t?.SpecialType == SpecialType.System_Int32 || t?.SpecialType == SpecialType.System_Int64;

    private static bool IsList(ITypeSymbol t) =>
        t is INamedTypeSymbol n && n.IsGenericType && n.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.List<T>";

    private static bool IsDict(ITypeSymbol t) =>
        t is INamedTypeSymbol n && n.IsGenericType &&
        n.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>" &&
        n.TypeArguments[0].SpecialType == SpecialType.System_String;

    private static bool IsCore(INamedTypeSymbol t) => t?.ContainingNamespace?.ToDisplayString() == "TrackerCore";
}
