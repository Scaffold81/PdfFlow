namespace Worker.Models;

/// <summary>
/// Represents a PDF document record as stored in the database.
/// Mirrors the ApiGateway Document entity — both services share the same table.
/// </summary>
public class Document
{
    public Guid Id { get; set; }

    /// <summary>UUID-based file name used on disk (prevents collisions).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Original file name as uploaded by the client.</summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>Current processing status. Updated by the Worker during processing.</summary>
    public DocumentStatus Status { get; set; }

    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Full text extracted from all pages. Set after successful processing.</summary>
    public string? ExtractedText { get; set; }

    /// <summary>Total page count. Set after successful processing.</summary>
    public int? PageCount { get; set; }

    /// <summary>Error details if processing failed. Null on success.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Processing lifecycle. Transitions: Pending → Processing → Completed | Failed
/// </summary>
public enum DocumentStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

/// <summary>
/// Deserialized from the RabbitMQ message published by the API Gateway.
/// Contains everything the Worker needs to locate and process the file.
/// </summary>
public record ProcessingMessage(
    Guid DocumentId,
    string FilePath,
    string OriginalName
);

/// <summary>
/// Result returned by the PDF extractor after processing a file.
/// </summary>
public record ExtractionResult(
    string Text,
    int PageCount
);
