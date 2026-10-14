using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Imortais.LinuxClient.Services;

public sealed record LinuxReleaseUpdate(int BuildNumber, string Version, string PackageName, Uri PackageUrl, Uri ChecksumUrl);

/// <summary>Explicit opt-in updater. Uses only prereleases from the official IMORTAIS GitHub repository.</summary>
public sealed class LinuxUpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/cocoputiz-sudo/imortais-combat-client/releases?per_page=50";
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private static readonly Regex TagPattern = new(@"^v0\.2\.0-linux-alpha\.(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DebPattern = new(@"^IMORTAIS-Combat-Client-v(0\.2\.0-alpha\.\d+)-linux-x64\.deb$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public LinuxUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("IMORTAIS-Combat-Client-Linux/0.2");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static int CurrentBuildNumber
    {
        get
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "linux-build.json")));
                return doc.RootElement.GetProperty("buildNumber").GetInt32();
            }
            catch { return 0; }
        }
    }

    public async Task<LinuxReleaseUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        checkTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _http.GetAsync(ReleasesApi, checkTimeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(checkTimeout.Token);
        using var releases = await JsonDocument.ParseAsync(stream, cancellationToken: checkTimeout.Token);
        if (releases.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("A resposta de releases não é uma lista.");

        return SelectLatestRelease(releases.RootElement, CurrentBuildNumber);
    }

    public static LinuxReleaseUpdate? SelectLatestRelease(JsonElement releases, int installedBuild)
    {
        if (releases.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Formato das releases inválido.");
        LinuxReleaseUpdate? newest = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || !release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var match = TagPattern.Match(tag);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var build) || build <= installedBuild) continue;

            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            foreach (var asset in assets)
            {
                var package = asset.GetProperty("name").GetString() ?? "";
                var packageMatch = DebPattern.Match(package);
                if (!packageMatch.Success) continue;
                var checksum = assets.FirstOrDefault(x => (x.GetProperty("name").GetString() ?? "") == package + ".sha256");
                if (checksum.ValueKind == JsonValueKind.Undefined) continue;
                var packageUrl = TryOfficialUrl(asset.GetProperty("browser_download_url").GetString());
                var shaUrl = TryOfficialUrl(checksum.GetProperty("browser_download_url").GetString());
                if (packageUrl is null || shaUrl is null) continue;
                if (newest is null || build > newest.BuildNumber)
                    newest = new LinuxReleaseUpdate(build, packageMatch.Groups[1].Value, package, packageUrl, shaUrl);
            }
        }
        return newest;
    }

    private static Uri? TryOfficialUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !url.AbsolutePath.StartsWith("/cocoputiz-sudo/imortais-combat-client/releases/download/", StringComparison.Ordinal))
            return null;
        return url;
    }

    public static string ParseExpectedSha256(string checksumText, string packageName)
    {
        var line = checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(x => x.EndsWith(packageName, StringComparison.Ordinal));
        if (line is null) throw new InvalidDataException("Checksum do pacote não encontrado.");
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit) ||
            parts[1].TrimStart('*') != packageName)
            throw new InvalidDataException("Arquivo SHA-256 inválido.");
        return parts[0];
    }

    public async Task<string> DownloadVerifiedAsync(LinuxReleaseUpdate release, CancellationToken cancellationToken = default)
    {
        var checksumText = await _http.GetStringAsync(release.ChecksumUrl, cancellationToken);
        var expectedSha256 = ParseExpectedSha256(checksumText, release.PackageName);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) throw new IOException("Pasta pessoal não encontrada.");
        var folder = Path.Combine(home, "Downloads", "IMORTAIS-Updates");
        Directory.CreateDirectory(folder);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var target = Path.Combine(folder, release.PackageName);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await _http.GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxPackageBytes)
                throw new InvalidDataException("Pacote acima do limite de segurança.");

            long total = 0;
            await using (var dest = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await using var src = await response.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[65536];
                int count;
                while ((count = await src.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (total > MaxPackageBytes) throw new InvalidDataException("O pacote excede o limite de segurança.");
                    await dest.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                await dest.FlushAsync(cancellationToken);
            }

            await using (var read = File.OpenRead(temp))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(read, cancellationToken));
                if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 diferente do publicado. Instalação bloqueada.");
            }
            File.Move(temp, target, overwrite: true);
            return target;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Invokes Ubuntu's graphical PolicyKit authentication; never runs the whole GUI as root.</summary>
    public static async Task InstallWithAuthorizationAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Atualização disponível apenas no Linux.");
        if (!Path.IsPathFullyQualified(packagePath) || !File.Exists(packagePath) ||
            !packagePath.EndsWith(".deb", StringComparison.Ordinal))
            throw new InvalidDataException("Caminho do pacote .deb inválido.");

        var start = new ProcessStartInfo("pkexec")
        {
            UseShellExecute = false,
            CreateNoWindow = false
        };
        start.ArgumentList.Add("/usr/bin/apt-get");
        start.ArgumentList.Add("install");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("--no-install-recommends");
        start.ArgumentList.Add(packagePath);
        using var proc = Process.Start(start) ?? throw new IOException("Não foi possível iniciar a autenticação do Ubuntu.");
        await proc.WaitForExitAsync(cancellationToken);
        if (proc.ExitCode != 0)
            throw new IOException("A instalação foi cancelada ou falhou. O pacote verificado continua salvo em " + packagePath);
    }
}
