using Microsoft.EntityFrameworkCore;
using Worker.Models;

namespace Worker.Data;

/// <summary>
/// EF Core database context for the Background Worker.
/// Intentionally kept minimal — the Worker only reads/updates document records,
/// it does not create or delete them.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("documents");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
            entity.Property(e => e.OriginalName).HasColumnName("original_name").IsRequired();

            // Enum stored as lowercase string for human-readable DB values
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion(
                    v => v.ToString().ToLower(),
                    v => Enum.Parse<DocumentStatus>(v, true)
                );

            entity.Property(e => e.FileSize).HasColumnName("file_size");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.ExtractedText).HasColumnName("extracted_text");
            entity.Property(e => e.PageCount).HasColumnName("page_count");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
