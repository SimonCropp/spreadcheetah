using SpreadCheetah.Helpers;
using SpreadCheetah.Styling;
using SpreadCheetah.Styling.Internal;
using System.Collections.Immutable;
using System.Drawing;
using System.IO.Compression;
using System.Text;

namespace SpreadCheetah.MetadataXml;

internal struct StylesXml
{
    private const string XmlPart1 =
        "<cellStyleXfs count=\"1\">" +
        "<xf numFmtId=\"0\" fontId=\"0\"/>" +
        "</cellStyleXfs>" +
        "<cellXfs count=\"";

    private const string XmlPart2 =
        "</cellXfs>" +
        "<cellStyles count=\"1\">" +
        "<cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/>" +
        "</cellStyles>" +
        "<dxfs count=\"0\"/>" +
        "</styleSheet>";

    public static async ValueTask WriteAsync(
        ZipArchive archive,
        CompressionLevel compressionLevel,
        SpreadsheetBuffer buffer,
        Dictionary<ImmutableStyle, int> styles,
        CancellationToken token)
    {
        var stream = archive.CreateEntry("xl/styles.xml", compressionLevel).Open();
#if NETSTANDARD2_0
        using (stream)
#else
        await using (stream.ConfigureAwait(false))
#endif
        {
            var writer = new StylesXml(styles);
            var done = false;

            do
            {
                done = writer.TryWrite(buffer.GetSpan(), out var bytesWritten);
                buffer.Advance(bytesWritten);
                await buffer.FlushToStreamAsync(stream, token).ConfigureAwait(false);
            } while (!done);

            await WriteAsync(stream, buffer, styles, writer.CustomNumberFormats, writer.Fills, writer.Fonts, token).ConfigureAwait(false);
        }
    }

    private static ReadOnlySpan<byte> Header =>
        """<?xml version="1.0" encoding="utf-8"?>"""u8 +
        """<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">"""u8;

    private readonly StyleNumberFormatsXml _numberFormatsXml;
    private readonly StyleFillsXml _fillsXml;
    private readonly StyleFontsXml _fontsXml;
    private Element _next;

    public Dictionary<string, int>? CustomNumberFormats { get; }
    public Dictionary<ImmutableFill, int> Fills { get; }
    public Dictionary<ImmutableFont, int> Fonts { get; }

    private StylesXml(Dictionary<ImmutableStyle, int> styles)
    {
        CustomNumberFormats = CreateCustomNumberFormatDictionary(styles);
        Fills = CreateFillDictionary(styles);
        Fonts = CreateFontDictionary(styles);
        _numberFormatsXml = new StyleNumberFormatsXml(CustomNumberFormats?.ToList());
        _fillsXml = new StyleFillsXml(Fills.Keys.ToList());
        _fontsXml = new StyleFontsXml(Fonts.Keys.ToList());
    }

    private static Dictionary<string, int>? CreateCustomNumberFormatDictionary(Dictionary<ImmutableStyle, int> styles)
    {
        Dictionary<string, int>? dictionary = null;
        var numberFormatId = 165; // Custom formats start sequentially from this ID

        foreach (var style in styles.Keys)
        {
            var numberFormat = style.NumberFormat;
            if (numberFormat is null) continue;
            if (NumberFormats.GetPredefinedNumberFormatId(numberFormat) is not null) continue;

            dictionary ??= new Dictionary<string, int>(StringComparer.Ordinal);
            if (dictionary.TryAdd(numberFormat, numberFormatId))
                ++numberFormatId;
        }

        return dictionary;
    }

    private static Dictionary<ImmutableFill, int> CreateFillDictionary(Dictionary<ImmutableStyle, int> styles)
    {
        var defaultFill = new ImmutableFill();
        const int defaultCount = 2;

        var uniqueFills = new Dictionary<ImmutableFill, int> { { defaultFill, 0 } };
        var fillIndex = defaultCount;

        foreach (var style in styles.Keys)
        {
            var fill = style.Fill;
            if (!uniqueFills.ContainsKey(fill)) // TODO: Use CollectionsMarshal
            {
                uniqueFills[fill] = fillIndex;
                ++fillIndex;
            }
        }

        return uniqueFills;
    }

    private static Dictionary<ImmutableFont, int> CreateFontDictionary(Dictionary<ImmutableStyle, int> styles)
    {
        var defaultFont = new ImmutableFont { Size = Font.DefaultSize };
        const int defaultCount = 1;

        var uniqueFonts = new Dictionary<ImmutableFont, int> { { defaultFont, 0 } };
        var fontIndex = defaultCount;

        foreach (var style in styles.Keys)
        {
            var font = style.Font;
            if (!uniqueFonts.ContainsKey(font)) // TODO: Use CollectionsMarshal
            {
                uniqueFonts[font] = fontIndex;
                ++fontIndex;
            }
        }

        return uniqueFonts;
    }

    public bool TryWrite(Span<byte> bytes, out int bytesWritten)
    {
        bytesWritten = 0;

        if (_next == Element.Header && !Advance(Header.TryCopyTo(bytes, ref bytesWritten))) return false;
        if (_next == Element.NumberFormats && !Advance(_numberFormatsXml.TryWrite(bytes, ref bytesWritten))) return false;
        if (_next == Element.Fonts && !Advance(_fontsXml.TryWrite(bytes, ref bytesWritten))) return false;
        if (_next == Element.Fills && !Advance(_fillsXml.TryWrite(bytes, ref bytesWritten))) return false;

        return true;
    }

    private bool Advance(bool success)
    {
        if (success)
            ++_next;

        return success;
    }

    private static async ValueTask WriteAsync(
        Stream stream,
        SpreadsheetBuffer buffer,
        Dictionary<ImmutableStyle, int> styles,
        Dictionary<string, int>? customNumberFormatLookup,
        Dictionary<ImmutableFill, int> fillLookup,
        Dictionary<ImmutableFont, int> fontLookup,
        CancellationToken token)
    {
        var borderLookup = await WriteBordersAsync(stream, buffer, styles, token).ConfigureAwait(false);

        await buffer.WriteAsciiStringAsync(XmlPart1, stream, token).ConfigureAwait(false);

        // Must at least have the built-in default style
        var styleCount = styles.Count;
        if (styleCount.GetNumberOfDigits() > buffer.FreeCapacity)
            await buffer.FlushToStreamAsync(stream, token).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.Append(styleCount).Append("\">");

        // Assuming here that the built-in default style is part of 'styles'
        foreach (var style in styles.Keys)
        {
            AppendStyle(sb, style, customNumberFormatLookup, fontLookup, fillLookup, borderLookup);
            await buffer.WriteAsciiStringAsync(sb.ToString(), stream, token).ConfigureAwait(false);
            sb.Clear();
        }

        await buffer.WriteAsciiStringAsync(XmlPart2, stream, token).ConfigureAwait(false);
        await buffer.FlushToStreamAsync(stream, token).ConfigureAwait(false);
    }

    private static void AppendStyle(StringBuilder sb,
        in ImmutableStyle style,
        IReadOnlyDictionary<string, int>? customNumberFormats,
        IReadOnlyDictionary<ImmutableFont, int> fonts,
        IReadOnlyDictionary<ImmutableFill, int> fills,
        IReadOnlyDictionary<ImmutableBorder, int> borders)
    {
        var numberFormatId = GetNumberFormatId(style.NumberFormat, customNumberFormats);
        sb.Append("<xf numFmtId=\"").Append(numberFormatId).Append('"');
        if (numberFormatId > 0) sb.Append(" applyNumberFormat=\"1\"");

        var fontIndex = fonts[style.Font];
        sb.Append(" fontId=\"").Append(fontIndex).Append('"');
        if (fontIndex > 0) sb.Append(" applyFont=\"1\"");

        var fillIndex = fills[style.Fill];
        sb.Append(" fillId=\"").Append(fillIndex).Append('"');
        if (fillIndex > 1) sb.Append(" applyFill=\"1\"");

        if (borders.TryGetValue(style.Border, out var borderIndex) && borderIndex > 0)
            sb.Append(" borderId=\"").Append(borderIndex).Append("\" applyBorder=\"1\"");

        sb.Append(" xfId=\"0\"");

        var defaultAlignment = new ImmutableAlignment();
        if (style.Alignment == defaultAlignment)
            sb.Append("/>");
        else
        {
            sb.Append(" applyAlignment=\"1\">");
            AppendAlignment(sb, style.Alignment);
            sb.Append("</xf>");
        }
    }

    private static int GetNumberFormatId(string? numberFormat, IReadOnlyDictionary<string, int>? customNumberFormats)
    {
        if (numberFormat is null) return 0;
        return NumberFormats.GetPredefinedNumberFormatId(numberFormat)
            ?? customNumberFormats?.GetValueOrDefault(numberFormat)
            ?? 0;
    }

    private static async ValueTask<Dictionary<ImmutableBorder, int>> WriteBordersAsync(
        Stream stream,
        SpreadsheetBuffer buffer,
        Dictionary<ImmutableStyle, int> styles,
        CancellationToken token)
    {
        var defaultBorder = new ImmutableBorder();
        const int defaultCount = 1;

        var uniqueBorders = new Dictionary<ImmutableBorder, int> { { defaultBorder, 0 } };
        foreach (var style in styles.Keys)
        {
            var border = style.Border;
            uniqueBorders[border] = 0;
        }

        var sb = new StringBuilder();
        var totalCount = uniqueBorders.Count + defaultCount - 1;
        sb.Append("<borders count=\"").Append(totalCount).Append("\">");

        // The default border must be the first one (index 0)
        sb.Append("<border><left/><right/><top/><bottom/><diagonal/></border>");
        await buffer.WriteAsciiStringAsync(sb.ToString(), stream, token).ConfigureAwait(false);

        var borderIndex = defaultCount;
#if NET5_0_OR_GREATER // https://github.com/dotnet/runtime/issues/34606
        foreach (var border in uniqueBorders.Keys)
#else
        foreach (var border in uniqueBorders.Keys.ToArray())
#endif
        {
            if (border.Equals(defaultBorder)) continue;

            sb.Clear();
            AppendBorder(sb, border);
            await buffer.WriteAsciiStringAsync(sb.ToString(), stream, token).ConfigureAwait(false);

            uniqueBorders[border] = borderIndex;
            ++borderIndex;
        }

        await buffer.WriteAsciiStringAsync("</borders>", stream, token).ConfigureAwait(false);
        return uniqueBorders;
    }

    private static void AppendBorder(StringBuilder sb, in ImmutableBorder border)
    {
        sb.Append("<border");

        if (border.Diagonal.Type.HasFlag(DiagonalBorderType.DiagonalUp))
            sb.Append(" diagonalUp=\"true\"");
        if (border.Diagonal.Type.HasFlag(DiagonalBorderType.DiagonalDown))
            sb.Append(" diagonalDown=\"true\"");

        sb.Append('>');
        AppendEdgeBorder(sb, "left", border.Left);
        AppendEdgeBorder(sb, "right", border.Right);
        AppendEdgeBorder(sb, "top", border.Top);
        AppendEdgeBorder(sb, "bottom", border.Bottom);
        AppendBorderPart(sb, "diagonal", border.Diagonal.BorderStyle, border.Diagonal.Color);

        sb.Append("</border>");
    }

    private static void AppendEdgeBorder(StringBuilder sb, string borderName, ImmutableEdgeBorder border)
        => AppendBorderPart(sb, borderName, border.BorderStyle, border.Color);

    private static void AppendBorderPart(StringBuilder sb, string borderName, BorderStyle style, Color? color)
    {
        sb.Append('<').Append(borderName);

        if (style == BorderStyle.None)
        {
            sb.Append("/>");
            return;
        }

        sb.Append(" style=\"");
        AppendStyleAttributeValue(sb, style);

        if (color is null)
        {
            sb.Append("\"/>");
            return;
        }

        sb.Append("\"><color rgb=\"")
            .Append(HexString(color.Value))
            .Append("\"/></")
            .Append(borderName)
            .Append('>');
    }

    private static void AppendStyleAttributeValue(StringBuilder sb, BorderStyle style)
    {
        var value = style switch
        {
            BorderStyle.DashDot => "dashDot",
            BorderStyle.DashDotDot => "dashDotDot",
            BorderStyle.Dashed => "dashed",
            BorderStyle.Dotted => "dotted",
            BorderStyle.DoubleLine => "double",
            BorderStyle.Hair => "hair",
            BorderStyle.Medium => "medium",
            BorderStyle.MediumDashDot => "mediumDashDot",
            BorderStyle.MediumDashDotDot => "mediumDashDotDot",
            BorderStyle.MediumDashed => "mediumDashed",
            BorderStyle.SlantDashDot => "slantDashDot",
            BorderStyle.Thick => "thick",
            BorderStyle.Thin => "thin",
            _ => ""
        };

        sb.Append(value);
    }

    private static StringBuilder AppendAlignment(StringBuilder sb, ImmutableAlignment alignment)
    {
        sb.Append("<alignment");
        AppendHorizontalAlignment(sb, alignment.Horizontal);
        AppendVerticalAlignment(sb, alignment.Vertical);

        if (alignment.WrapText)
            sb.Append(" wrapText=\"1\"");

        if (alignment.Indent > 0)
            sb.Append(" indent=\"").Append(alignment.Indent).Append('"');

        return sb.Append("/>");
    }

    private static StringBuilder AppendHorizontalAlignment(StringBuilder sb, HorizontalAlignment alignment) => alignment switch
    {
        HorizontalAlignment.Left => sb.Append(" horizontal=\"left\""),
        HorizontalAlignment.Center => sb.Append(" horizontal=\"center\""),
        HorizontalAlignment.Right => sb.Append(" horizontal=\"right\""),
        _ => sb
    };

    private static StringBuilder AppendVerticalAlignment(StringBuilder sb, VerticalAlignment alignment) => alignment switch
    {
        VerticalAlignment.Center => sb.Append(" vertical=\"center\""),
        VerticalAlignment.Top => sb.Append(" vertical=\"top\""),
        _ => sb
    };

    private static string HexString(Color c) => $"{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private enum Element
    {
        Header,
        NumberFormats,
        Fonts,
        Fills,
        Done
    }
}
