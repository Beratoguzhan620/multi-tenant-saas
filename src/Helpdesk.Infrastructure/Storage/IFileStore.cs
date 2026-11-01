namespace Helpdesk.Infrastructure.Storage;

/// <summary>Where attachment bytes live. Keys come from the server (<c>tenant/attachment</c>), never from a file name.</summary>
public interface IFileStore
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);

    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}
