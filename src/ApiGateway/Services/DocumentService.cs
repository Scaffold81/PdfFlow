using ApiGateway.Data;
using ApiGateway.Messaging;
using ApiGateway.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Services;

/// <summary>
/// Core business logic for managing PDF documents.
/// </summary>
public interface IDocumentService
{
    Task<DocumentUploadResponse> UploadAsync(IFormFile file, CancellationToken ct = default);
    Task<PaginatedResponse<DocumentListItem>> GetListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<DocumentContentResponse?> GetContentAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Handles the full upload flow:
/// 1. Validates the file (type, size)
/// 2. Saves it to the shared storage directory
/// 3. Creates a DB record with status Pending
/// 4. Publishes a message to RabbitMQ for the Worker to pick up
/// </summary>
public class DocumentService(
    AppDbContext db,
    IMessagePublisher publisher,
    IWebHostEnvironment env,
    IConfiguration config,
    ILogger<DocumentService> logger) : IDocumentService
{
    // Accept both standard and alternative PDF MIME types
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/x-pdf"
    };

    // Must match the queue name the Worker is listening on
    private const string QueueName = "pdf.processing";

    /// <summary>
    /// Validates, stores, and queues a PDF file for background processing.
    /// Returns 202 Accepted — the document is not yet processed at this point.
    /// </summary>
    public async Task<DocumentUploadResponse> UploadAsync(IFormFile file, CancellationToken ct = default)
    {
        // --- Validation ---
        if (file.Length == 0)
            throw new ArgumentException("File is empty.");

        if (!AllowedContentTypes.Contains(file.ContentType) &&
            !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only PDF files are allowed.");

        var maxSize = long.Parse(config["Upload:MaxFileSizeMb"] ?? "50") * 1024 * 1024;
        if (file.Length > maxSize)
            throw new ArgumentException($"File exceeds maximum allowed size of {maxSize / (1024 * 1024)} MB.");

        // --- Persist file to disk ---
        // Use a UUID-based name to avoid conflicts and not expose original names on disk.
        var storageDir = Path.Combine(env.ContentRootPath, "storage");
        Directory.CreateDirectory(storageDir);

        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(storageDir, storedFileName);

        await using (var stream = File.Create(filePath))
            await file.CopyToAsync(stream, ct);

        logger.LogInformation("File '{Original}' saved as '{Stored}'", file.FileName, storedFileName);

        // --- Save metadata to the database ---
        var document = new Document
        {
            FileName = storedFileName,
            OriginalName = file.FileName,
            FileSize = file.Length,
            ContentType = file.ContentType,
            Status = DocumentStatus.Pending
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        // --- Publish to RabbitMQ for async processing ---
        // The Worker will pick this up, extract text, and update the record.
        var message = new ProcessingMessage(document.Id, filePath, file.FileName);
        await publisher.PublishAsync(message, QueueName, ct);

        logger.LogInformation("Document {Id} queued for processing", document.Id);

        return new DocumentUploadResponse(
            document.Id,
            document.OriginalName,
            document.FileSize,
            document.Status.ToString(),
            document.CreatedAt
        );
    }

    /// <summary>
    /// Returns a paginated list of all documents, ordered by creation date descending.
    /// Page and pageSize values are clamped to safe bounds.
    /// </summary>
    public async Task<PaginatedResponse<DocumentListItem>> GetListAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await db.Documents.CountAsync(ct);

        var items = await db.Documents
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocumentListItem(
                d.Id,
                d.OriginalName,
                d.FileSize,
                d.Status.ToString(),
                d.PageCount,
                d.CreatedAt,
                d.UpdatedAt
            ))
            .ToListAsync(ct);

        return new PaginatedResponse<DocumentListItem>(items, total, page, pageSize);
    }

    /// <summary>
    /// Returns the full document details including extracted text.
    /// Returns null if the document does not exist (controller maps this to 404).
    /// </summary>
    public async Task<DocumentContentResponse?> GetContentAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await db.Documents.FindAsync([id], ct);
        if (doc is null) return null;

        return new DocumentContentResponse(
            doc.Id,
            doc.OriginalName,
            doc.Status.ToString(),
            doc.ExtractedText,
            doc.PageCount,
            doc.ErrorMessage,
            doc.CreatedAt,
            doc.UpdatedAt
        );
    }
}
