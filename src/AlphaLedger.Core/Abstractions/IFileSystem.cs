namespace AlphaLedger.Core.Abstractions;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    bool FileExists(string path);
    void MoveDirectory(string source, string destination);
    bool IsPathWritable(string path);
    IEnumerable<string> GetFileSystemEntries(string path);
}
