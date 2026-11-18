using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace WindowDock;

internal sealed record ReleaseOffer(string Version, string Notes, long PackageLength, string? PublishedDigest = null)
{
    internal string PackageName => $"WindowDock-{Version}-win-x64.zip";
    internal Uri Asset(string name) => new($"https://github.com/poikilometis/WindowDock/releases/download/v{Version}/{name}");
}

internal static class ReleaseUpdates
{
    private const long PackageLimit = 192L * 1024 * 1024;
    private const long ExtractLimit = 420L * 1024 * 1024;
    private static readonly Uri LatestRelease = new("https://api.github.com/repos/poikilometis/WindowDock/releases/latest");
    private static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    static ReleaseUpdates() => client.DefaultRequestHeaders.UserAgent.ParseAdd("WindowDock/" + RuntimePaths.ProductVersion);

    internal static bool TryVersion(string? value, out Version version)
    {
        if (Version.TryParse(value, out var parsed) && parsed.Build >= 0 && parsed.Revision < 0 && parsed.ToString() == value)
        {
            version = parsed;
            return true;
        }
        version = new();
        return false;
    }

    internal static async Task<ReleaseOffer?> LatestAsync(CancellationToken cancellation)
    {
        using var response = await GetAsync(LatestRelease, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await CopyBoundedAsync(content, buffer, 1024 * 1024, cancellation).ConfigureAwait(false);
        return Parse(buffer.ToArray(), RuntimePaths.ProductVersion);
    }

    internal static ReleaseOffer? Parse(byte[] payload, string currentVersion)
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString();
        if (tag is null || !tag.StartsWith('v') || !TryVersion(tag[1..], out var offered)) return null;
        if (!Version.TryParse(currentVersion.Split('-')[0], out var current) ||
            offered < current || offered == current && !currentVersion.Contains('-')) return null;

        var filename = $"WindowDock-{offered}-win-x64.zip";
        long size = 0;
        string? digest = null;
        var checksum = false;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name == filename)
            {
                size = asset.GetProperty("size").GetInt64();
                if (asset.TryGetProperty("digest", out var published) && published.ValueKind == JsonValueKind.String &&
                    published.GetString() is { } hash && hash.StartsWith("sha256:", StringComparison.Ordinal) && IsDigest(hash[7..]))
                    digest = hash[7..];
            }
            if (name == filename + ".sha256") checksum = true;
        }
        if (!checksum || size <= 0 || size > PackageLimit) return null;

        var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
        return new(offered.ToString(), notes[..Math.Min(notes.Length, 32000)], size, digest);
    }

    internal static async Task<string> DownloadAsync(ReleaseOffer release, string destination, CancellationToken cancellation)
    {
        using var checksum = await GetAsync(release.Asset(release.PackageName + ".sha256"), cancellation).ConfigureAwait(false);
        checksum.EnsureSuccessStatusCode();
        await using var checksumStream = await checksum.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var text = new MemoryStream();
        await CopyBoundedAsync(checksumStream, text, 4096, cancellation).ConfigureAwait(false);
        var digest = System.Text.Encoding.UTF8.GetString(text.ToArray()).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (digest.Length != 2 || digest[1] != release.PackageName || !IsDigest(digest[0]))
            throw new InvalidDataException("Invalid WindowDock release checksum.");

        using var zip = await GetAsync(release.Asset(release.PackageName), cancellation).ConfigureAwait(false);
        zip.EnsureSuccessStatusCode();
        await using (var output = File.Create(destination))
        await using (var input = await zip.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await CopyBoundedAsync(input, output, release.PackageLength, cancellation).ConfigureAwait(false);
        if (new FileInfo(destination).Length != release.PackageLength) throw new InvalidDataException("The update archive is incomplete.");
        await using var staged = File.OpenRead(destination);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(staged, cancellation).ConfigureAwait(false));
        if (!actual.Equals(digest[0], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update archive failed its checksum.");
        if (release.PublishedDigest is { } published && !actual.Equals(published, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update archive disagrees with the GitHub asset digest.");
        return actual;
    }

    private static async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellation)
    {
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
                uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("The release download attempted to leave GitHub.");
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var next = response.Headers.Location;
            response.Dispose();
            if (next is null) break;
            uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
        }
        throw new HttpRequestException("The release download redirected too many times.");
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken cancellation)
    {
        var buffer = new byte[81920];
        long total = 0;
        int size;
        while ((size = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            total += size;
            if (total > limit) throw new InvalidDataException("The update download is larger than expected.");
            await output.WriteAsync(buffer.AsMemory(0, size), cancellation).ConfigureAwait(false);
        }
    }

    internal static bool IsDigest(string digest) => digest.Length == 64 && digest.All(Uri.IsHexDigit);

    internal static void ExtractVerified(string archive, string directory, string digest)
    {
        using var file = File.OpenRead(archive);
        if (file.Length > PackageLimit || !IsDigest(digest) ||
            !Convert.ToHexString(SHA256.HashData(file)).Equals(digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update archive failed its checksum.");
        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var segments = name.TrimEnd('/').Split('/');
            if (segments.Any(segment => segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.Contains(':')) ||
                segments[0].Equals("data", StringComparison.OrdinalIgnoreCase) ||
                segments[0].StartsWith(".windowdock-update-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update archive contains an invalid path.");
            if (name.EndsWith('/')) continue;
            if (!paths.Add(name)) throw new InvalidDataException("The update archive contains a duplicate file.");
            if (entry.Length > ExtractLimit - total) throw new InvalidDataException("The extracted update exceeds its size limit.");
            total += entry.Length;
            var path = Path.Combine(directory, Path.Combine(segments));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path);
        }
    }
}
