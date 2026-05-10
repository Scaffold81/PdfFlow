using ApiGateway.Models;
using ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers;

/// <summary>
/// REST API controller for PDF document management.
/// Handles upload, listing, and retrieval of extracted text content.
///
/// All routes are under: /api/documents
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DocumentsController(
    IDocumentService documentService,
    ILogger<DocumentsController> logger) : ControllerBase
{
    /// <summary>
    /// Upload a PDF file for asynchronous text extraction.
    /// The file is saved and a processing job is queued immediately.
    /// Returns 202 Accepted — processing happens in the background.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DocumentUploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        try
        {
            var result = await documentService.UploadAsync(file, ct);

            // 202 Accepted: request received, processing will complete asynchronously
            return Accepted(result);
        }
        catch (ArgumentException ex)
        {
            // Validation errors (wrong file type, size, etc.) are handled here.
            // Other exceptions bubble up to ExceptionHandlingMiddleware.
            logger.LogWarning("Upload validation failed: {Message}", ex.Message);
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>
    /// Get a paginated list of all uploaded documents.
    /// Results are ordered by creation date (newest first).
    /// Does not include extracted text — use /content for that.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<DocumentListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await documentService.GetListAsync(page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get the extracted text content of a specific document.
    /// Poll this endpoint after upload until status is "Completed" or "Failed".
    /// </summary>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(typeof(DocumentContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken ct)
    {
        var result = await documentService.GetContentAsync(id, ct);

        if (result is null)
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Document {id} not found."
            });

        return Ok(result);
    }
}
