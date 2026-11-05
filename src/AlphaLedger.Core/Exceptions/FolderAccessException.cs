namespace AlphaLedger.Core.Exceptions;

public class FolderAccessException : BusinessException
{
    public string? Path { get; }

    public FolderAccessException(string message, string? path = null, Exception? innerException = null)
        : base(message, innerException ?? new InvalidOperationException(message))
    {
        Path = path;
    }
}
