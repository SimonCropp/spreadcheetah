using SpreadCheetah.Helpers;

namespace SpreadCheetah.Styling;

/// <summary>
/// Format that defines how a number or <see cref="DateTime"/> cell should be displayed.
/// May be a custom format, initialised by <see cref="Custom"/>, or a standard format, initialised by <see cref="Standard"/>
/// </summary>
public readonly record struct NumberFormat : IEquatable<NumberFormat>
{
    private NumberFormat(string? customFormat, StandardNumberFormat? standardFormat)
    {
        CustomFormat = customFormat.WithEnsuredMaxLength(255);
        StandardFormat = (standardFormat is not { } format || EnumPolyfill.IsDefined(format))
            ? standardFormat
            : throw new ArgumentOutOfRangeException(nameof(standardFormat));
    }

    internal string? CustomFormat { get; }
    internal StandardNumberFormat? StandardFormat { get; }

    /// <summary>
    /// Creates a custom number format. The <paramref name="formatString"/> must be an <see href="https://support.microsoft.com/en-us/office/number-format-codes-5026bbd6-04bc-48cd-bf33-80f18b4eae68">Excel Format Code</see>
    /// </summary>
    /// <param name="formatString">The custom format string for this number format</param>
    /// <returns>A <see cref="NumberFormat"/> representing this custom format string</returns>
    public static NumberFormat Custom(string formatString)
    {
        return new NumberFormat(formatString, null);
    }

    /// <summary>
    /// Creates a standard number format.
    /// </summary>
    /// <param name="format">The standard format to use for this number format</param>
    /// <returns>A <see cref="NumberFormat"/> representing this standard format</returns>
    public static NumberFormat Standard(StandardNumberFormat format)
    {
        return new NumberFormat(null, format);
    }

    /// <summary>
    /// Creates a number format from a string which may be custom or standard.
    /// For backwards compatibility purposes only.
    /// </summary>
    /// <param name="formatString">The custom or standard string to use for this number format</param>
    /// <returns>A <see cref="NumberFormat"/> representing this format</returns>
    internal static NumberFormat FromLegacyString(string formatString)
    {
        var standardNumberFormat = (StandardNumberFormat?)NumberFormats.GetStandardNumberFormatId(formatString);
        return standardNumberFormat is not null
            ? new NumberFormat(null, standardNumberFormat)
            : new NumberFormat(formatString, null);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (CustomFormat is not null) return CustomFormat;
        if (StandardFormat is { } format) return format.ToString();
        return string.Empty;
    }
}
