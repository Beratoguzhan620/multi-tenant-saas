namespace Helpdesk.Core.Tickets;

/// <summary>A file on a ticket. Its bytes live in the file store; its size counts against the plan's storage.</summary>
public sealed class Attachment
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int FileNameMaxLength = 150;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "application/pdf", "text/plain", "text/csv",
        "application/zip", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    private Attachment()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TicketId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Where the bytes are, relative to the store; never derived from the uploaded name.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public Guid? UploadedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Attachment Create(Ticket ticket, string fileName, string contentType, long sizeBytes, Guid? uploadedBy, DateTimeOffset now)
    {
        if (sizeBytes is <= 0 or > MaxBytes)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A file has 1 byte to {MaxBytes / (1024 * 1024)} MB.");
        }

        var type = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(type))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Attach images, PDFs, text, CSV, ZIP, Word or Excel files.");
        }

        var id = Guid.CreateVersion7(now);
        return new Attachment
        {
            Id = id,
            TenantId = ticket.TenantId,
            TicketId = ticket.Id,
            FileName = CleanName(fileName),
            ContentType = type,
            SizeBytes = sizeBytes,
            StorageKey = $"{ticket.TenantId:N}/{id:N}",
            UploadedBy = uploadedBy,
            CreatedAt = now,
        };
    }

    /// <summary>Keeps the name for display only: no folders, no control characters, a sensible length.</summary>
    public static string CleanName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c is not '"' and not '/' and not ':').ToArray()).Trim().TrimStart('.');
        if (name.Length == 0)
        {
            name = "file";
        }

        return name.Length <= FileNameMaxLength ? name : name[..FileNameMaxLength];
    }
}
