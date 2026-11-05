Build the standalone Windows installer by running:

  powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\build-installer.ps1

The script publishes the x64 application as a self-contained, single-file executable
and packages it into Release\AlphaLedger-Setup.exe using Inno Setup 6. The recipient
needs only this setup executable; .NET and application files are not separate downloads.

The installer is per-user and does not require administrator access. It creates Start
Menu and optional desktop shortcuts and provides a standard Windows uninstall entry.
Uninstalling the application does not remove its database, archive, or backup data.

Online updates use public GitHub Releases for MedoZahran0/AlphaLedger. To publish a
release, update the Version in src\AlphaLedger.App\AlphaLedger.App.csproj, push a tag
matching that version (for example v1.0.1), and the GitHub Actions release workflow
will build and attach AlphaLedger-Setup.exe and AlphaLedger-Update.exe. Installed
copies check for a newer release at startup, ask before downloading, show download
progress, verify the update SHA-256 digest, then ask before restarting into the
updated application. The setup executable is only needed for the initial install.
