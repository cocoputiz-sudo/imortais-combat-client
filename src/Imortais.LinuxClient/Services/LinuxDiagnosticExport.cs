using System.Security.Cryptography;
using System.Text;

namespace Imortais.LinuxClient.Services;

/// <summary>Writes diagnostics without depending on the desktop file portal.</summary>
public static class LinuxDiagnosticExport
{
    public static async Task<string> SaveFallbackAsync(string json, string? homeDirectory = null)
    {
        var home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            throw new IOException("Pasta pessoal do usuário não encontrada.");

        var downloads = Path.Combine(home, "Downloads");
        var parent = Directory.Exists(downloads) ? downloads : home;
        var folder = Path.Combine(parent, "IMORTAIS-Diagnostics");
        Directory.CreateDirectory(folder);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var filename = $"imortais-linux-photon-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(folder, filename);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var data = Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(data);
            await stream.FlushAsync();
        }
        return path;
    }
}
