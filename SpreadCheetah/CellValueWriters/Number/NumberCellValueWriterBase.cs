using SpreadCheetah.CellWriters;
using SpreadCheetah.Helpers;
using SpreadCheetah.Styling;

namespace SpreadCheetah.CellValueWriters.Number;

internal abstract class NumberCellValueWriterBase : CellValueWriter
{
    protected abstract int GetStyleId(StyleId styleId);

    protected static ReadOnlySpan<byte> BeginDataCell => "<c><v>"u8;
    protected static ReadOnlySpan<byte> EndStyleBeginValue => "\"><v>"u8; // TODO: Rename
    protected static ReadOnlySpan<byte> EndDefaultCell => "</v></c>"u8;

    public static bool TryWriteFormulaCellStart(int? styleId, Span<byte> bytes, out int bytesWritten)
    {
        bytesWritten = 0;
        var written = 0;

        if (styleId is null)
        {
            return FormulaCellHelper.BeginNumberFormulaCell.TryCopyTo(bytes, ref bytesWritten);
        }

        if (!StyledCellHelper.BeginStyledNumberCell.TryCopyTo(bytes, ref written)) return false;
        if (!SpanHelper.TryWrite(styleId.Value, bytes, ref written)) return false;
        if (!FormulaCellHelper.EndStyleBeginFormula.TryCopyTo(bytes, ref written)) return false;

        bytesWritten = written;
        return true;
    }

    public static bool TryWriteFormulaCellStartWithReference(int? styleId, CellWriterState state, Span<byte> bytes, out int bytesWritten)
    {
        bytesWritten = 0;
        var written = 0;

        if (styleId is null)
        {
            if (!TryWriteCellStartWithReference(state, bytes, ref written)) return false;
            if (!"\"><f>"u8.TryCopyTo(bytes, ref written)) return false;

            bytesWritten = written;
            return true;
        }

        if (!TryWriteCellStartWithReference(state, bytes, ref written)) return false;
        if (!StyledCellHelper.EndReferenceBeginStyleId.TryCopyTo(bytes, ref written)) return false;
        if (!SpanHelper.TryWrite(styleId.Value, bytes, ref written)) return false;
        if (!FormulaCellHelper.EndStyleBeginFormula.TryCopyTo(bytes, ref written)) return false;

        bytesWritten = written;
        return true;
    }

    public override bool WriteStartElement(SpreadsheetBuffer buffer)
    {
        var bytes = buffer.GetSpan();
        var written = 0;

        if (!BeginDataCell.TryCopyTo(bytes, ref written)) return false;

        buffer.Advance(written);
        return true;
    }

    public override bool WriteStartElement(StyleId styleId, SpreadsheetBuffer buffer)
    {
        var bytes = buffer.GetSpan();
        var written = 0;

        if (!StyledCellHelper.BeginStyledNumberCell.TryCopyTo(bytes, ref written)) return false;
        if (!SpanHelper.TryWrite(GetStyleId(styleId), bytes, ref written)) return false;
        if (!EndStyleBeginValue.TryCopyTo(bytes, ref written)) return false;

        buffer.Advance(written);
        return true;
    }

    public override bool WriteStartElementWithReference(CellWriterState state)
    {
        var buffer = state.Buffer;
        var bytes = buffer.GetSpan();
        var written = 0;

        if (!TryWriteCellStartWithReference(state, bytes, ref written)) return false;
        if (!"\"><v>"u8.TryCopyTo(bytes, ref written)) return false;

        buffer.Advance(written);
        return true;
    }

    public override bool WriteStartElementWithReference(StyleId styleId, CellWriterState state)
    {
        var buffer = state.Buffer;
        var bytes = buffer.GetSpan();
        var written = 0;

        if (!TryWriteCellStartWithReference(state, bytes, ref written)) return false;
        if (!StyledCellHelper.EndReferenceBeginStyleId.TryCopyTo(bytes, ref written)) return false;
        if (!SpanHelper.TryWrite(GetStyleId(styleId), bytes, ref written)) return false;
        if (!EndStyleBeginValue.TryCopyTo(bytes, ref written)) return false;

        buffer.Advance(written);
        return true;
    }

    protected static bool WriteFormulaStartElement(int? styleId, SpreadsheetBuffer buffer)
    {
        var bytes = buffer.GetSpan();
        var written = 0;

        if (styleId is null)
        {
            if (!FormulaCellHelper.BeginNumberFormulaCell.TryCopyTo(bytes, ref written)) return false;
            buffer.Advance(written);
            return true;
        }

        if (!StyledCellHelper.BeginStyledNumberCell.TryCopyTo(bytes, ref written)) return false;
        if (!SpanHelper.TryWrite(styleId.Value, bytes, ref written)) return false;
        if (!FormulaCellHelper.EndStyleBeginFormula.TryCopyTo(bytes, ref written)) return false;

        buffer.Advance(written);
        return true;
    }

    protected static bool WriteFormulaStartElementWithReference(int? styleId, CellWriterState state)
    {
        var buffer = state.Buffer;
        var bytes = buffer.GetSpan();
        var written = 0;

        if (styleId is null)
        {
            if (!TryWriteCellStartWithReference(state, bytes, ref written)) return false;
            if (!"\"><f>"u8.TryCopyTo(bytes, ref written)) return false;

            buffer.Advance(written);
            return true;
        }

        if (!TryWriteCellStartWithReference(state, bytes, ref written)) return false;
        if (!StyledCellHelper.EndReferenceBeginStyleId.TryCopyTo(bytes, ref written)) return false;
        if (!SpanHelper.TryWrite(styleId.Value, bytes, ref written)) return false;
        if (!FormulaCellHelper.EndStyleBeginFormula.TryCopyTo(bytes, ref written)) return false;

        buffer.Advance(written);
        return true;
    }

    public override bool CanWriteValuePieceByPiece(in DataCell cell) => true;

    public override bool TryWriteEndElement(SpreadsheetBuffer buffer)
    {
        var bytes = buffer.GetSpan();
        if (!EndDefaultCell.TryCopyTo(bytes))
            return false;

        buffer.Advance(EndDefaultCell.Length);
        return true;
    }

    public override bool TryWriteEndElement(in Cell cell, SpreadsheetBuffer buffer)
    {
        if (cell.Formula is null)
            return TryWriteEndElement(buffer);

        var bytes = buffer.GetSpan();
        if (!FormulaCellHelper.EndCachedValueEndCell.TryCopyTo(bytes))
            return false;

        buffer.Advance(FormulaCellHelper.EndCachedValueEndCell.Length);
        return true;
    }
}
