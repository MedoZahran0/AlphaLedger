namespace AlphaLedger.Core.Abstractions;

public interface IWindowsDataProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}