using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpreadCheetah.SourceGenerator.Helpers;
using System.Collections.Immutable;
using System.Text;

namespace SpreadCheetah.SourceGenerator;

[Generator]
public class RowCellsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var filtered = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (s, _) => IsSyntaxTargetForGeneration(s),
                static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static x => x is not null);

        var source = context.CompilationProvider.Combine(filtered.Collect());

        context.RegisterSourceOutput(source, static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode syntaxNode) => syntaxNode is InvocationExpressionSyntax
    {
        ArgumentList.Arguments.Count: <= 3,
        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AddAsRowAsync" }
    };

    private static ExpressionSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        return context.Node is not InvocationExpressionSyntax invocation
            ? null
            : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    }

    private static void Execute(Compilation compilation, ImmutableArray<ExpressionSyntax?> classes, SourceProductionContext context)
    {
        if (classes.IsDefaultOrEmpty)
            return;

        var distinctClasses = classes.Distinct();

        var classPropertiesInfos = GetClassPropertiesInfo(compilation, distinctClasses, context.CancellationToken);

        if (classPropertiesInfos.Count > 0)
        {
            var sb = new StringBuilder();
            GenerateValidator(sb, classPropertiesInfos);
            context.AddSource("SpreadsheetExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

            ReportDiagnostics(context, classPropertiesInfos);
        }
    }

    private static void ReportDiagnostics(SourceProductionContext context, IEnumerable<ClassPropertiesInfo> infos)
    {
        foreach (var info in infos)
        {
            if (info.PropertyNames.Count == 0)
                context.ReportDiagnostics(Diagnostics.NoPropertiesFound, info.Locations, info.ClassType);

            if (info.UnsupportedPropertyNames.FirstOrDefault() is { } unsupportedProperty)
                context.ReportDiagnostics(Diagnostics.UnsupportedTypeForCellValue, info.Locations, info.ClassType, unsupportedProperty);
        }
    }

    private static void GenerateValidator(StringBuilder sb, ICollection<ClassPropertiesInfo> infos)
    {
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("namespace SpreadCheetah");
        sb.AppendLine("{");
        sb.AppendLine("    public static class SpreadsheetExtensions");
        sb.AppendLine("    {");

        const int indent = 2;
        if (infos.Count == 0)
            WriteStub(sb, indent);
        else
            WriteMethods(sb, indent, infos);

        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static void WriteSummary(StringBuilder sb, int indent)
    {
        sb.AppendLine(indent, "/// <summary>");
        sb.AppendLine(indent, "/// Add object as a row in the active worksheet.");
        sb.AppendLine(indent, "/// Each property with a public getter on the object will be added as a cell in the row.");
        sb.AppendLine(indent, "/// The method is generated by a source generator.");
        sb.AppendLine(indent, "/// </summary>");
    }

    private static void WriteStub(StringBuilder sb, int indent)
    {
        WriteSummary(sb, indent);
        sb.AppendLine(indent, "public static ValueTask AddAsRowAsync(this Spreadsheet spreadsheet, object obj, CancellationToken token = default)");
        sb.AppendLine(indent, "{");
        sb.AppendLine(indent, "    // This will be filled in by the generator once you call AddAsRowAsync()");
        sb.AppendLine(indent, "    return new ValueTask();");
        sb.AppendLine(indent, "}");
    }

    private static void WriteMethods(StringBuilder sb, int indent, IEnumerable<ClassPropertiesInfo> infos)
    {
        foreach (var info in infos)
        {
            WriteSummary(sb, indent);

            if (info.PropertyNames.Count == 0)
                GenerateEmptyArrayMethod(sb, indent, info);
            else
                GenerateMethod(sb, indent, info);
        }
    }

    private static void GenerateEmptyArrayMethod(StringBuilder sb, int indent, ClassPropertiesInfo info)
    {
        sb.AppendLine(indent, $"public static ValueTask AddAsRowAsync(this Spreadsheet spreadsheet, {info.ClassType} obj, CancellationToken token = default)");
        sb.AppendLine(indent, "{");
        sb.AppendLine(indent, "    var cells = System.Array.Empty<DataCell>();");
        sb.AppendLine(indent, "    return spreadsheet.AddRowAsync(cells, token);");
        sb.AppendLine(indent, "}");
    }

    private static void GenerateMethod(StringBuilder sb, int indent, ClassPropertiesInfo info)
    {
        sb.AppendLine(indent, $"public static ValueTask AddAsRowAsync(this Spreadsheet spreadsheet, {info.ClassType} obj, CancellationToken token = default)");
        sb.AppendLine(indent, "{");
        sb.AppendLine(indent, "    var cells = new[]");
        sb.AppendLine(indent, "    {");

        foreach (var propertyName in info.PropertyNames)
        {
            sb.AppendLine(indent + 2, $"new DataCell(obj.{propertyName}),");
        }

        sb.AppendLine(indent, "    };");
        sb.AppendLine(indent, "    return spreadsheet.AddRowAsync(cells, token);");
        sb.AppendLine(indent, "}");
    }

    private static ICollection<ClassPropertiesInfo> GetClassPropertiesInfo(Compilation compilation, IEnumerable<SyntaxNode?> argumentsToValidate, CancellationToken token)
    {
        var foundTypes = new Dictionary<ITypeSymbol, ClassPropertiesInfo>(SymbolEqualityComparer.Default);

        foreach (var argument in argumentsToValidate)
        {
            token.ThrowIfCancellationRequested();

            if (argument is null) continue;

            var semanticModel = compilation.GetSemanticModel(argument.SyntaxTree);

            var classType = semanticModel.GetTypeInfo(argument, token).Type;
            if (classType is null) continue;

            if (!foundTypes.TryGetValue(classType, out var info))
            {
                info = ClassPropertiesInfo.CreateFrom(compilation, classType);
                foundTypes.Add(classType, info);
            }

            info.Locations.Add(argument.GetLocation());
        }

        return foundTypes.Values;
    }
}
