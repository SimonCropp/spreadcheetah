using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using SpreadCheetah.Test.Helpers;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SpreadCheetah.Test.Tests
{
    public class SpreadsheetTests
    {
        [Fact]
        public async Task Spreadsheet_CreateNew_EmptyIsValid()
        {
            // Arrange
            using var stream = new MemoryStream();
            var validator = new OpenXmlValidator();

            // Act
            using (var spreadsheet = await Spreadsheet.CreateNewAsync(stream))
            {
                await spreadsheet.PutNextWorksheetAsync("Name");
                await spreadsheet.FinishAsync();
            }

            // Assert
            stream.Position = 0;
            using var actual = SpreadsheetDocument.Open(stream, true);
            Assert.Empty(validator.Validate(actual));
        }

        [Theory]
        [InlineData(false)]
        public async Task Spreadsheet_CreateNew_EmptyToWriteOnlyStream(bool asyncOnly)
        {
            // Arrange
            using var stream = asyncOnly
                ? new AsyncWriteOnlyMemoryStream()
                : new WriteOnlyMemoryStream();

            // Act
            using (var spreadsheet = await Spreadsheet.CreateNewAsync(stream))
            {
                await spreadsheet.PutNextWorksheetAsync("Name");
                await spreadsheet.FinishAsync();
            }

            // Assert
            Assert.True(stream.Position > 0);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Spreadsheet_Finish_ThrowsWhenNoWorksheet(bool hasWorksheet)
        {
            // Arrange
            using var spreadsheet = await Spreadsheet.CreateNewAsync(Stream.Null);

            if (hasWorksheet)
                await spreadsheet.PutNextWorksheetAsync("Book 1");

            // Act
            var exception = await Record.ExceptionAsync(async () => await spreadsheet.FinishAsync());

            // Assert
            Assert.Equal(hasWorksheet, exception == null);
        }

        [Theory]
        [InlineData("OneWord")]
        [InlineData("With whitespace")]
        [InlineData("With trailing whitespace ")]
        [InlineData(" With leading whitespace")]
        [InlineData("With-Special-Characters!#¤%&")]
        [InlineData("With'Single'Quotes")]
        [InlineData("With\"Quotation\"Marks")]
        [InlineData("WithNorwegianCharactersÆØÅ")]
        public async Task Spreadsheet_PutNextWorksheet_CorrectName(string name)
        {
            // Arrange
            using var stream = new MemoryStream();

            using (var spreadsheet = await Spreadsheet.CreateNewAsync(stream))
            {
                // Act
                await spreadsheet.PutNextWorksheetAsync(name);
                await spreadsheet.FinishAsync();
            }

            // Assert
            stream.Position = 0;
            using var actual = SpreadsheetDocument.Open(stream, true);
            var sheets = actual.WorkbookPart.Workbook.Sheets.Cast<Sheet>();
            var sheet = Assert.Single(sheets);
            Assert.Equal(name, sheet?.Name.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("This name is over the 31 character limit in Excel")]
        [InlineData("'Starting with single quote")]
        [InlineData("Ending with single quote'")]
        [InlineData("With / forward slash")]
        [InlineData("With \\ backward slash")]
        [InlineData("With ? question mark")]
        [InlineData("With * asterisk")]
        [InlineData("With [ left square bracket")]
        [InlineData("With ] right square bracket")]
        public async Task Spreadsheet_PutNextWorksheet_InvalidName(string name)
        {
            // Arrange
            using var spreadsheet = await Spreadsheet.CreateNewAsync(Stream.Null);

            // Act
            var exception = await Record.ExceptionAsync(async () => await spreadsheet.PutNextWorksheetAsync(name));

            // Assert
            Assert.NotNull(exception);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Spreadsheet_PutNextWorksheet_ThrowsOnDuplicateName(bool duplicateName)
        {
            // Arrange
            const string name = "Sheet";
            using var spreadsheet = await Spreadsheet.CreateNewAsync(Stream.Null);
            await spreadsheet.PutNextWorksheetAsync(name);
            var nextName = duplicateName ? name : "Sheet 2";

            // Act
            var exception = await Record.ExceptionAsync(async () => await spreadsheet.PutNextWorksheetAsync(nextName));

            // Assert
            Assert.Equal(duplicateName, exception != null);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(10)]
        [InlineData(1000)]
        public async Task Spreadsheet_PutNextWorksheet_MultipleWorksheets(int count)
        {
            // Arrange
            var sheetNames = Enumerable.Range(1, count).Select(x => "Sheet " + x).ToList();
            using var stream = new MemoryStream();
            using (var spreadsheet = await Spreadsheet.CreateNewAsync(stream))
            {
                // Act
                foreach (var name in sheetNames)
                {
                    await spreadsheet.PutNextWorksheetAsync(name);
                    await spreadsheet.AddRowAsync(new Cell(name));
                }

                await spreadsheet.FinishAsync();
            }

            // Assert
            stream.Position = 0;
            using var actual = SpreadsheetDocument.Open(stream, true);
            var sheets = actual.WorkbookPart.Workbook.Sheets.Cast<Sheet>().ToList();
            var worksheets = actual.WorkbookPart.WorksheetParts.Select(x => x.Worksheet);
            var cells = worksheets.Select(x => x.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().Single());
            Assert.Equal(count, sheets.Count);
            Assert.Equal(sheetNames, sheets.Select(x => x.Name.Value));
            Assert.Equal(sheetNames, cells.Select(x => x.InnerText));
        }
    }
}
