using AlphaLedger.Core.Abstractions;

namespace AlphaLedger.Infrastructure.Storage;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool FileExists(string path) => File.Exists(path);

    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);

    public bool IsPathWritable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".al-write-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IEnumerable<string> GetFileSystemEntries(string path)
        => Directory.Exists(path) ? Directory.GetFileSystemEntries(path) : [];
}
