namespace ApiGateway.Models;

/// <summary>
/// Response returned immediately after a successful PDF upload (HTTP 202 Accepted).
/// The document is queued for background processing at this point.
/// </summary>
public record DocumentUploadResponse(
    Guid Id,
    string OriginalName,
    long FileSize,
    string Status,
    DateTime CreatedAt
);

/// <summary>
/// Summary item returned in the paginated document list.
/// Does not include extracted text to keep the response lightweight.
/// </summary>
public record DocumentListItem(
    Guid Id,
    string OriginalName,
    long FileSize,
    string Status,
    int? PageCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// Full document response including extracted text content.
/// Returned by GET /api/documents/{id}/content.
/// ExtractedText is null while status is Pending or Processing.
/// </summary>
public record DocumentContentResponse(
    Guid Id,
    string OriginalName,
    string Status,
    string? ExtractedText,
    int? PageCount,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// Generic wrapper for paginated list responses.
/// </summary>
public record PaginatedResponse<T>(
    IEnumerable<T> Items,
    int Total,
    int Page,
    int PageSize
);

/// <summary>
/// Message published to RabbitMQ after a PDF is uploaded.
/// Consumed by the Background Worker to trigger text extraction.
/// </summary>
public record ProcessingMessage(
    Guid DocumentId,
    string FilePath,
    string OriginalName
);
