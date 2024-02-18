using Microsoft.CodeAnalysis;
using SpreadCheetah.SourceGenerator.Helpers;
using System.Diagnostics.CodeAnalysis;

namespace SpreadCheetah.SourceGenerator.Extensions;

internal static class AttributeDataExtensions
{
    public static bool TryParseWorksheetRowAttribute(
        this AttributeData attribute,
        CancellationToken token,
        [NotNullWhen(true)] out INamedTypeSymbol? typeSymbol,
        [NotNullWhen(true)] out Location? location)
    {
        typeSymbol = null;
        location = null;

        var args = attribute.ConstructorArguments;
        if (args is not [{ Value: INamedTypeSymbol symbol }])
            return false;

        if (symbol.Kind == SymbolKind.ErrorType)
            return false;

        var syntaxReference = attribute.ApplicationSyntaxReference;
        if (syntaxReference is null)
            return false;

        location = syntaxReference.GetSyntax(token).GetLocation();
        typeSymbol = symbol;
        return true;
    }

    public static bool TryParseOptionsAttribute(
        this AttributeData attribute,
        [NotNullWhen(true)] out GeneratorOptions? options)
    {
        options = null;

        if (!string.Equals(Attributes.GenerationOptions, attribute.AttributeClass?.ToDisplayString(), StringComparison.Ordinal))
            return false;

        if (attribute.NamedArguments.IsDefaultOrEmpty)
            return false;

        foreach (var (key, value) in attribute.NamedArguments)
        {
            if (!string.Equals(key, "SuppressWarnings", StringComparison.Ordinal))
                continue;

            if (value.Value is bool suppressWarnings)
            {
                options = new GeneratorOptions(suppressWarnings);
                return true;
            }
        }

        return false;
    }

    public static bool TryParseColumnHeaderAttribute(
        this AttributeData attribute,
        out TypedConstant attributeArg)
    {
        attributeArg = default;

        if (!string.Equals(Attributes.ColumnHeader, attribute.AttributeClass?.ToDisplayString(), StringComparison.Ordinal))
            return false;

        var args = attribute.ConstructorArguments;
        if (args is not [{ Value: string } arg])
            return false;

        attributeArg = arg;
        return true;
    }

    public static bool TryParseColumnOrderAttribute(
        this AttributeData attribute,
        out int order)
    {
        order = 0;

        if (!string.Equals(Attributes.ColumnOrder, attribute.AttributeClass?.ToDisplayString(), StringComparison.Ordinal))
            return false;

        var args = attribute.ConstructorArguments;
        if (args is not [{ Value: int attributeValue }])
            return false;

        order = attributeValue;
        return true;
    }
}
