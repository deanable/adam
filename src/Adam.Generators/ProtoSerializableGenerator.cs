using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Adam.Generators
{
    [Generator(LanguageNames.CSharp)]
    public sealed class ProtoSerializableGenerator : IIncrementalGenerator
    {
        private const string ProtoFieldAttributeName = "ProtoFieldAttribute";
        private const string IProtoSerializableName = "IProtoSerializable";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(static ctx =>
            {
                ctx.AddSource(
                    "ProtoFieldAttribute.g.cs",
                    SourceText.From(AttributesSource, Encoding.UTF8));
            });

            var candidates = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidate(node),
                    transform: static (ctx, _) => GetClassInfo(ctx))
                .Where(static info => info != null)
                .Select(static (info, _) => info!);

            var collected = candidates.Collect();
            var compilationAndClasses = context.CompilationProvider.Combine(collected);

            context.RegisterSourceOutput(compilationAndClasses, static (spc, source) =>
            {
                var (compilation, classes) = source;
                GenerateCode(spc, compilation, classes);
            });
        }

        private static bool IsCandidate(SyntaxNode node)
        {
            return node is PropertyDeclarationSyntax propDecl &&
                   propDecl.AttributeLists.Count > 0 &&
                   HasProtoFieldAttribute(propDecl);
        }

        private static bool HasProtoFieldAttribute(PropertyDeclarationSyntax propDecl)
        {
            foreach (var attrList in propDecl.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var name = attr.Name.ToString();
                    if (name == "ProtoField" || name == "ProtoFieldAttribute")
                        return true;
                }
            }
            return false;
        }

        private static ClassInfo? GetClassInfo(GeneratorSyntaxContext context)
        {
            var propDecl = (PropertyDeclarationSyntax)context.Node;
            var semanticModel = context.SemanticModel;

            var classDecl = propDecl.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            if (classDecl == null) return null;

            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
            if (classSymbol == null) return null;

            bool implementsInterface = false;
            foreach (var iface in classSymbol.AllInterfaces)
            {
                if (iface.Name == IProtoSerializableName &&
                    iface.ContainingNamespace?.ToDisplayString() == "Adam.Shared.Contracts")
                {
                    implementsInterface = true;
                    break;
                }
            }
            if (!implementsInterface) return null;

            var propSymbol = semanticModel.GetDeclaredSymbol(propDecl);
            if (propSymbol == null) return null;

            foreach (var attr in propSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.Name == ProtoFieldAttributeName &&
                    attr.ConstructorArguments.Length > 0)
                {
                    int fieldNumber = (int)attr.ConstructorArguments[0].Value!;

                    object? defaultValue = null;
                    foreach (var namedArg in attr.NamedArguments)
                    {
                        if (namedArg.Key == "DefaultValue")
                            defaultValue = namedArg.Value.Value;
                    }

                    return new ClassInfo
                    {
                        ClassName = classSymbol.Name,
                        ClassNamespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? "Adam.Shared.Contracts",
                        ContainingTypeNames = GetContainingTypeNames(classSymbol),
                        FullTypeName = classSymbol.ToDisplayString(),
                        FieldNumber = fieldNumber,
                        PropertyName = propSymbol.Name,
                        PropertyType = propSymbol.Type.ToDisplayString(),
                        IsNullable = propSymbol.Type.NullableAnnotation == NullableAnnotation.Annotated,
                        IsReadOnly = propSymbol.IsReadOnly,
                        DefaultValue = defaultValue,
                    };
                }
            }
            return null;
        }

        private static ImmutableArray<string> GetContainingTypeNames(INamedTypeSymbol classSymbol)
        {
            var names = new List<string>();
            var parent = classSymbol.ContainingType;
            while (parent != null)
            {
                names.Add(parent.Name);
                parent = parent.ContainingType;
            }
            names.Reverse();
            return names.ToImmutableArray();
        }

        private static void GenerateCode(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<ClassInfo> classInfos)
        {
            if (classInfos.IsEmpty) return;

            var classGroups = classInfos
                .GroupBy(c => c.ClassName + "|" + c.ClassNamespace + "|" + string.Join(",", c.ContainingTypeNames))
                .ToList();

            foreach (var group in classGroups)
            {
                string className = "(unknown)";
                try
                {
                    var fields = group.OrderBy(c => c.FieldNumber).ToList();
                    var first = fields[0];
                    className = first.ClassName;
                    var source = GenerateClassSource(fields, first.ClassNamespace, first.ClassName, first.ContainingTypeNames);
                    context.AddSource(
                        first.ClassName + ".IProtoSerializable.g.cs",
                        SourceText.From(source, Encoding.UTF8));
                }
                catch (System.Exception ex)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            new DiagnosticDescriptor("ADAM001", "Source generator error",
                                "Error generating for " + className + ": " + ex.Message,
                                "Adam.Generators", DiagnosticSeverity.Warning, isEnabledByDefault: true),
                            Location.None));
                }
            }
        }

        private static string GenerateClassSource(
            List<ClassInfo> fields, string classNamespace, string className,
            ImmutableArray<string> containingTypeNames)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("namespace " + classNamespace + ";");
            sb.AppendLine();

            foreach (var tn in containingTypeNames)
            {
                sb.Append("partial class ");
                sb.Append(tn);
                sb.Append(" { ");
            }

            sb.Append("partial class ");
            sb.Append(className);
            sb.AppendLine(" : global::Adam.Shared.Contracts.IProtoSerializable");
            sb.AppendLine("{");

            // ── CalculateSize() ──
            sb.AppendLine("    public int CalculateSize()");
            sb.AppendLine("    {");
            sb.AppendLine("        int size = 0;");

            bool hasAnyFields = false;
            foreach (var field in fields)
            {
                string? guard = GetDefaultGuard(field);
                string sizeExpr = GetSizeExpression(field);
                if (guard != null)
                {
                    sb.Append("        if (");
                    sb.Append(guard);
                    sb.AppendLine(")");
                    sb.AppendLine("        {");
                    sb.Append("            size += ");
                    sb.Append(sizeExpr);
                    sb.AppendLine(";");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.Append("        size += ");
                    sb.Append(sizeExpr);
                    sb.AppendLine(";");
                }
                hasAnyFields = true;
            }

            if (!hasAnyFields)
                sb.AppendLine("        // No serializable fields");

            sb.AppendLine("        return size;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // ── WriteTo() ──
            sb.AppendLine("    public void WriteTo(global::Google.Protobuf.CodedOutputStream output)");
            sb.AppendLine("    {");
            foreach (var field in fields)
            {
                string? guard = GetDefaultGuard(field);
                string writeExpr = GetWriteExpression(field);
                if (guard != null)
                {
                    sb.Append("        if (");
                    sb.Append(guard);
                    sb.AppendLine(")");
                    sb.AppendLine("        {");
                    sb.Append("            ");
                    sb.Append(writeExpr);
                    sb.AppendLine(";");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.Append("        ");
                    sb.Append(writeExpr);
                    sb.AppendLine(";");
                }
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            // ── MergeFrom() ──
            sb.AppendLine("    public void MergeFrom(global::Google.Protobuf.CodedInputStream input)");
            sb.AppendLine("    {");
            sb.AppendLine("        uint tag;");
            sb.AppendLine("        while ((tag = input.ReadTag()) > 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (global::Google.Protobuf.WireFormat.GetTagFieldNumber(tag))");
            sb.AppendLine("            {");

            foreach (var field in fields)
            {
                sb.Append("                case ");
                sb.Append(field.FieldNumber.ToString());
                sb.AppendLine(":");
                sb.Append("                    ");
                sb.Append(GetReadExpression(field));
                sb.AppendLine(";");
                sb.AppendLine("                    break;");
            }

            sb.AppendLine("                default:");
            sb.AppendLine("                    input.SkipLastField();");
            sb.AppendLine("                    break;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            for (int i = 0; i < containingTypeNames.Length; i++)
                sb.AppendLine("}");

            return sb.ToString();
        }

        private static string? GetDefaultGuard(ClassInfo field)
        {
            if (field.DefaultValue != null)
            {
                var dv = field.DefaultValue;
                if (dv is int i) return field.PropertyName + " != " + i.ToString();
                if (dv is long l) return field.PropertyName + " != " + l.ToString();
                if (dv is double d) return field.PropertyName + " != " + d.ToString();
                if (dv is float f) return field.PropertyName + " != " + f.ToString() + "f";
                if (dv is string s) return field.PropertyName + " != \"" + EscapeStringLiteral(s) + "\"";
                if (dv is bool b) return field.PropertyName + " != " + (b ? "true" : "false");
                return field.PropertyName + " != default(" + field.PropertyType + ")";
            }

            string type = field.PropertyType;

            if ((type == "string?" || type == "string") && field.IsNullable)
                return field.PropertyName + " != null";

            if (type.StartsWith("long?") || type.StartsWith("int?") || type.StartsWith("double?") || type.StartsWith("float?"))
                return field.PropertyName + ".HasValue";

            if (type == "System.Guid?")
                return field.PropertyName + ".HasValue";

            if (!IsListType(type) && field.IsNullable)
                return field.PropertyName + " != null";

            return null;
        }

        private static string EscapeStringLiteral(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static string GetSizeExpression(ClassInfo field)
        {
            string p = field.PropertyName;
            int n = field.FieldNumber;

            if (field.PropertyType == "System.Guid")
                return "ProtoHelper.FieldSize(" + n + ", " + p + ".ToString())";
            if (field.PropertyType == "System.Guid?")
                return "ProtoHelper.FieldSize(" + n + ", " + p + ".Value.ToString())";
            if (IsListType(field.PropertyType))
                return "ProtoHelper.RepeatedFieldSize(" + n + ", " + p + ")";
            if (IsProtoSerializableType(field.PropertyType))
                return "ProtoHelper.FieldSize(" + n + ", " + p + ")";
            if (field.PropertyType.StartsWith("long?") || field.PropertyType.StartsWith("int?"))
                return "ProtoHelper.FieldSize(" + n + ", " + p + ".Value)";
            return "ProtoHelper.FieldSize(" + n + ", " + p + ")";
        }

        private static string GetWriteExpression(ClassInfo field)
        {
            string p = field.PropertyName;
            int n = field.FieldNumber;

            if (field.PropertyType == "System.Guid")
                return "ProtoHelper.WriteField(output, " + n + ", " + p + ".ToString())";
            if (field.PropertyType == "System.Guid?")
                return "ProtoHelper.WriteField(output, " + n + ", " + p + ".Value.ToString())";
            if (IsListType(field.PropertyType))
                return "ProtoHelper.WriteRepeatedField(output, " + n + ", " + p + ")";
            if (IsProtoSerializableType(field.PropertyType))
                return "ProtoHelper.WriteField(output, " + n + ", " + p + ")";
            if (field.PropertyType.StartsWith("long?") || field.PropertyType.StartsWith("int?"))
                return "ProtoHelper.WriteField(output, " + n + ", " + p + ".Value)";
            return "ProtoHelper.WriteField(output, " + n + ", " + p + ")";
        }

        private static string GetReadExpression(ClassInfo field)
        {
            var type = field.PropertyType;
            var name = field.PropertyName;

            if (type == "System.Guid")
                return name + " = global::System.Guid.Parse(input.ReadString())";
            if (type == "System.Guid?")
                return name + " = global::System.Guid.Parse(input.ReadString())";

            if (IsListType(type))
            {
                var elemType = GetListElementType(type);
                if (elemType == "string")
                    return name + ".Add(input.ReadString())";
                if (elemType == "bool")
                    return name + ".Add(input.ReadBool())";
                if (IsProtoSerializableType(elemType))
                {
                    string ct = StripNullable(elemType);
                    return "{ var _m = new " + ct + "(); var _b = input.ReadBytes().ToByteArray(); using var _ms = new global::System.IO.MemoryStream(_b); using var _cs = new global::Google.Protobuf.CodedInputStream(_ms); _m.MergeFrom(_cs); " + name + ".Add(_m); }";
                }
                return name + ".Add(input.ReadString())";
            }

            if (IsProtoSerializableType(type))
            {
                string ct = StripNullable(type);
                return "{ var _m = new " + ct + "(); var _b = input.ReadBytes().ToByteArray(); using var _ms = new global::System.IO.MemoryStream(_b); using var _cs = new global::Google.Protobuf.CodedInputStream(_ms); _m.MergeFrom(_cs); " + name + " = _m; }";
            }

            if (type == "long?" || type == "int?" || type == "double?" || type == "float?")
            {
                var baseType = type.TrimEnd('?');
                return name + " = input.Read" + GetProtoReadMethod(baseType) + "()";
            }

            return name + " = input.Read" + GetProtoReadMethod(type) + "()";
        }

        private static string GetProtoReadMethod(string type)
        {
            if (type == "ByteString" || type.EndsWith(".ByteString"))
                return "Bytes";

            switch (type)
            {
                case "string": return "String";
                case "int": return "Int32";
                case "long": return "Int64";
                case "bool": return "Bool";
                case "float": return "Float";
                case "double": return "Double";
                case "byte[]": return "Bytes";
                default: return "String";
            }
        }

        private static bool IsByteStringType(string type)
        {
            return type == "ByteString" || type.EndsWith(".ByteString");
        }

        private static bool IsListType(string type)
        {
            return type.StartsWith("System.Collections.Generic.List<") ||
                   type.StartsWith("List<") ||
                   type.StartsWith("global::System.Collections.Generic.List<");
        }

        private static string GetListElementType(string type)
        {
            int start = type.IndexOf('<') + 1;
            int end = type.LastIndexOf('>');
            if (start <= 0 || end < 0 || end <= start) return "string";
            return type.Substring(start, end - start);
        }

        private static bool IsProtoSerializableType(string typeName)
        {
            var simpleTypes = new HashSet<string>
            {
                "string", "int", "long", "bool", "float", "double",
                "byte[]",
                "System.Guid", "System.Guid?",
            };

            string cleanName = StripNullable(typeName);
            if (simpleTypes.Contains(cleanName)) return false;
            if (IsByteStringType(cleanName)) return false;
            if (IsListType(typeName))
                return IsProtoSerializableType(GetListElementType(typeName));
            return true;
        }

        private static string StripNullable(string typeName)
        {
            if (typeName.EndsWith("?") && typeName.Length > 1)
                return typeName.Substring(0, typeName.Length - 1);
            return typeName;
        }

        private static readonly string AttributesSource = @"
// <auto-generated/>
#nullable enable

[global::System.AttributeUsage(global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class ProtoFieldAttribute : global::System.Attribute
{
    public int FieldNumber { get; }
    public object? DefaultValue { get; init; }

    public ProtoFieldAttribute(int fieldNumber)
    {
        FieldNumber = fieldNumber;
    }
}
";

        private sealed class ClassInfo
        {
            public string ClassName { get; set; } = "";
            public string ClassNamespace { get; set; } = "";
            public ImmutableArray<string> ContainingTypeNames { get; set; }
            public string FullTypeName { get; set; } = "";
            public int FieldNumber { get; set; }
            public string PropertyName { get; set; } = "";
            public string PropertyType { get; set; } = "";
            public bool IsNullable { get; set; }
            public bool IsReadOnly { get; set; }
            public object? DefaultValue { get; set; }
        }
    }
}
