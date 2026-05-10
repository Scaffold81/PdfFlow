using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Worker.Models;

namespace Worker.Services;

/// <summary>
/// Abstraction for PDF text extraction.
/// Allows swapping the underlying library without touching the consumer logic.
/// </summary>
public interface IPdfExtractor
{
    ExtractionResult Extract(string filePath);
}

/// <summary>
/// PDF text extractor using UglyToad.PdfPig — a pure .NET library.
/// No native dependencies, works on any OS including Alpine Linux containers.
///
/// Strategy:
/// - Iterates each page and collects Word objects with bounding box coordinates.
/// - Groups words into lines using vertical position (Y) with a tolerance based
///   on average word height, then sorts words left-to-right within each line.
/// - This approach reconstructs natural reading order for most standard PDFs.
/// </summary>
public class PdfPigExtractor(ILogger<PdfPigExtractor> logger) : IPdfExtractor
{
    public ExtractionResult Extract(string filePath)
    {
        logger.LogInformation("Extracting text from '{FilePath}'", filePath);

        using var document = PdfDocument.Open(filePath);

        var sb = new StringBuilder();
        var pageCount = document.NumberOfPages;

        for (var i = 1; i <= pageCount; i++)
        {
            var page = document.GetPage(i);
            var pageText = ExtractPageText(page);

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                sb.AppendLine($"--- Page {i} ---");
                sb.AppendLine(pageText);
                sb.AppendLine();
            }
        }

        var extractedText = sb.ToString().Trim();

        logger.LogInformation(
            "Extracted {CharCount} characters from {PageCount} pages in '{FilePath}'",
            extractedText.Length, pageCount, filePath
        );

        return new ExtractionResult(extractedText, pageCount);
    }

    /// <summary>
    /// Reconstructs readable text from a single PDF page using word coordinates.
    /// Groups words by their Y position (bottom edge) into lines,
    /// then joins each line left-to-right.
    /// </summary>
    private static string ExtractPageText(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0) return string.Empty;

        // Use half the average word height as the tolerance for same-line grouping.
        // This handles slight vertical misalignments between words on the same line.
        var avgHeight = words.Average(w => w.BoundingBox.Height);
        var lineTolerance = avgHeight * 0.5;

        var lines = words
            // Round Y position to nearest tolerance bucket to group words on the same line
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / lineTolerance) * lineTolerance)
            // Higher Y = closer to top of page in PDF coordinate system
            .OrderByDescending(g => g.Key)
            .Select(g =>
                string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text))
            );

        return string.Join(Environment.NewLine, lines);
    }
}
