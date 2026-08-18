namespace ProxyManager.Certificates;

/// <summary>Stores PFX certificate files on disk under the data directory.</summary>
public sealed class CertificateFileStore
{
    private readonly string _directory;

    public CertificateFileStore(string dataDirectory)
    {
        _directory = Path.Combine(dataDirectory, "certs");
        Directory.CreateDirectory(_directory);
    }

    public string GetPath(Guid certificateId) => Path.Combine(_directory, $"{certificateId:n}.pfx");

    public async Task<string> SavePfxAsync(Guid certificateId, byte[] pfx, CancellationToken cancellationToken = default)
    {
        var path = GetPath(certificateId);
        await File.WriteAllBytesAsync(path, pfx, cancellationToken);
        return Path.GetFileName(path);
    }

    public void Delete(Guid certificateId)
    {
        try
        {
            File.Delete(GetPath(certificateId));
        }
        catch (IOException)
        {
            // Best effort — the file may already be gone.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
