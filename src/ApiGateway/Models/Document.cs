namespace ApiGateway.Models;

/// <summary>
/// Represents a PDF document stored in the system.
/// Tracks the full lifecycle from upload to text extraction completion.
/// </summary>
public class Document
{
    /// <summary>Unique identifier generated on creation.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Internal file name used for storage (UUID-based to avoid collisions).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Original file name provided by the client on upload.</summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>Current processing status of the document.</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    /// <summary>Size of the uploaded file in bytes.</summary>
    public long FileSize { get; set; }

    /// <summary>MIME type of the uploaded file.</summary>
    public string ContentType { get; set; } = "application/pdf";

    /// <summary>
    /// Full text extracted from the PDF.
    /// Null until the Worker finishes processing.
    /// </summary>
    public string? ExtractedText { get; set; }

    /// <summary>Total number of pages in the PDF. Populated after processing.</summary>
    public int? PageCount { get; set; }

    /// <summary>
    /// Contains the error message if processing failed.
    /// Null when status is Pending, Processing, or Completed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the processing lifecycle of a document.
/// Transitions: Pending → Processing → Completed | Failed
/// </summary>
public enum DocumentStatus
{
    /// <summary>Document uploaded, waiting in the queue.</summary>
    Pending,

    /// <summary>Worker has picked up the message and is extracting text.</summary>
    Processing,

    /// <summary>Text extraction finished successfully.</summary>
    Completed,

    /// <summary>Text extraction failed. See ErrorMessage for details.</summary>
    Failed
}
