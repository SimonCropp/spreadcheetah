using ClosedXML.Excel;
using SpreadCheetah.Test.Helpers;
using Xunit;

namespace SpreadCheetah.Test.Tests;

public class SpreadsheetMergeCellsTests
{
    [Theory]
    [InlineData("A1:A2")]
    [InlineData("A1:F1")]
    [InlineData("A1:XY10000")]
    public async Task Spreadsheet_MergeCells_ValidRange(string cellRange)
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var spreadsheet = await Spreadsheet.CreateNewAsync(stream);
        await spreadsheet.StartWorksheetAsync("Sheet");

        // Act
        spreadsheet.MergeCells(cellRange);
        await spreadsheet.FinishAsync();

        // Assert
        SpreadsheetAssert.Valid(stream);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.Single();
        var actualMergedRange = Assert.Single(worksheet.MergedRanges);
        Assert.Equal(cellRange, actualMergedRange.RangeAddress.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("A1")]
    [InlineData("A1:A")]
    [InlineData("A1:AAAA2")]
    [InlineData("$A$1:$A$2")]
    public async Task Spreadsheet_MergeCells_InvalidRange(string cellRange)
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var spreadsheet = await Spreadsheet.CreateNewAsync(stream);
        await spreadsheet.StartWorksheetAsync("Sheet");

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => spreadsheet.MergeCells(cellRange));
    }
}
