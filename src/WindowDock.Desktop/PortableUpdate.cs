using System.Diagnostics;
using System.Text.Json;

namespace WindowDock;

internal sealed record UpdateNotes(string Version, string Notes);

internal static class PortableUpdate
{
    private const string JobPrefix = ".windowdock-update-";
    private const string Archive = "release.zip";
    private const string LockName = ".windowdock-update.lock";
    private static string NotesPath(string profile) => Path.Combine(profile, "update-notes.json");
    private static string FailurePath(string profile) => Path.Combine(profile, "update-failed.txt");
    private sealed record Job(int ProcessId, long StartedAt, string Executable, string Profile, string? Language,
        string Digest, UpdateNotes Notes);
    private sealed record Change(string Target, string? Backup);

    internal static async Task PrepareAsync(ReleaseOffer offer, CancellationToken cancellation)
    {
        var exe = RuntimePaths.Executable;
        var root = Path.GetDirectoryName(exe)!;
        RequirePlainPath(root);
        var folder = Directory.CreateDirectory(Path.Combine(root, JobPrefix + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            using var owner = Process.GetCurrentProcess();
            var profile = Store.DirectoryPath;
            var language = Program.ExplicitLanguage;
            var digest = await ReleaseUpdates.DownloadAsync(offer, Path.Combine(folder, Archive), cancellation).ConfigureAwait(false);
            var worker = Path.Combine(folder, "worker");
            Directory.CreateDirectory(worker);
            File.Copy(exe, Path.Combine(worker, "WindowDock.exe"));
            foreach (var path in Directory.EnumerateFiles(root, "*.dll").Concat(Directory.EnumerateFiles(root, "WindowDock.*.json")))
                File.Copy(path, Path.Combine(worker, Path.GetFileName(path)));
            var runtime = Path.Combine(root, ".runtime");
            if (Directory.Exists(runtime)) CopyDirectory(runtime, Path.Combine(worker, ".runtime"));
            var request = new Job(owner.Id, owner.StartTime.ToUniversalTime().Ticks, Path.GetFileName(exe),
                profile, language, digest, new(offer.Version, offer.Notes));
            File.WriteAllText(Path.Combine(folder, "job.json"), JsonSerializer.Serialize(request));
            cancellation.ThrowIfCancellationRequested();
            using var launched = Process.Start(LaunchInfo(Path.Combine(worker, "WindowDock.exe"), worker, "--apply-update"))
                ?? throw new IOException("The update worker did not start.");
        }
        catch
        {
            TryDeleteFolder(folder);
            throw;
        }
    }

    internal static int Apply()
    {
        var worker = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var jobFolder = Directory.GetParent(worker)?.FullName;
        if (jobFolder is null || Path.GetFileName(worker) != "worker" ||
            !Path.GetFileName(jobFolder).StartsWith(JobPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(jobFolder)[JobPrefix.Length..], "N", out _)) return 2;
        var root = Directory.GetParent(jobFolder)!.FullName;
        RequirePlainPath(root);
        RequirePlainPath(jobFolder);
        RequirePlainPath(worker);
        var request = JsonSerializer.Deserialize<Job>(File.ReadAllText(Path.Combine(jobFolder, "job.json")))
            ?? throw new InvalidDataException("Invalid WindowDock update job.");
        if (request.Executable != Path.GetFileName(request.Executable) ||
            !request.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !ReleaseUpdates.TryVersion(request.Notes.Version, out _)) return 2;

        var executable = Path.Combine(root, request.Executable);
        using var installLock = new FileStream(Path.Combine(root, LockName), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        Process? replacement = null;
        var applied = new List<Change>();
        var exitConfirmed = false;
        var profileBackup = Path.Combine(jobFolder, "saved-profile");
        try
        {
            WaitForOldHost(request);
            exitConfirmed = true;

            var payload = Path.Combine(jobFolder, "incoming");
            Directory.CreateDirectory(payload);
            ReleaseUpdates.ExtractVerified(Path.Combine(jobFolder, Archive), payload, request.Digest);
            var newExe = Path.Combine(payload, "WindowDock.exe");
            if (!File.Exists(newExe) || !File.Exists(Path.Combine(payload, "WindowDock.dll")) ||
                FileVersionInfo.GetVersionInfo(newExe).ProductVersion?.Split('+')[0] != request.Notes.Version)
                throw new InvalidDataException("The update archive does not contain the requested WindowDock build.");

            Directory.CreateDirectory(profileBackup);
            foreach (var name in new[] { "settings.json", "groups.json", "settings.json.bak", "groups.json.bak" })
            {
                var path = Path.Combine(request.Profile, name);
                if (File.Exists(path)) File.Copy(path, Path.Combine(profileBackup, name));
            }

            Deploy(root, payload, Path.Combine(jobFolder, "saved-program"), request.Executable, applied);
            Store.WriteBytes(NotesPath(request.Profile), JsonSerializer.SerializeToUtf8Bytes(request.Notes), null);

            replacement = StartOriginal();
            WaitForWindow(replacement);
            File.WriteAllText(Path.Combine(jobFolder, "complete"), "");
            return 0;
        }
        catch (Exception exception)
        {
            try { File.WriteAllText(FailurePath(request.Profile), exception.Message); } catch (IOException) { }
            if (!exitConfirmed) return 1;
            if (replacement is not null && !replacement.HasExited &&
                (!replacement.CloseMainWindow() || !replacement.WaitForExit(15000))) return 1;
            foreach (var change in applied.AsEnumerable().Reverse())
            {
                if (change.Backup is not null) Replace(change.Backup, change.Target);
                else File.Delete(change.Target);
            }
            if (Directory.Exists(profileBackup))
                foreach (var saved in Directory.EnumerateFiles(profileBackup))
                    Replace(saved, Path.Combine(request.Profile, Path.GetFileName(saved)));
            File.Delete(NotesPath(request.Profile));
            using var restarted = StartOriginal();
            File.WriteAllText(Path.Combine(jobFolder, "complete"), "");
            return 1;
        }
        finally { replacement?.Dispose(); }

        Process StartOriginal()
        {
            var info = LaunchInfo(executable, root, "--data-dir", request.Profile, "--update-restart");
            if (!string.IsNullOrWhiteSpace(request.Language))
            {
                info.ArgumentList.Add("--language");
                info.ArgumentList.Add(request.Language);
            }
            return Process.Start(info) ?? throw new IOException("WindowDock did not restart.");
        }
    }

    private static void WaitForOldHost(Job request)
    {
        Process owner;
        try { owner = Process.GetProcessById(request.ProcessId); }
        catch (ArgumentException) { return; }
        using (owner)
        {
            if (owner.HasExited) return;
            if (owner.StartTime.ToUniversalTime().Ticks != request.StartedAt || !owner.WaitForExit(30000))
                throw new IOException("The previous WindowDock instance did not finish shutting down.");
        }
    }

    private static void WaitForWindow(Process process)
    {
        var timeout = Stopwatch.StartNew();
        do
        {
            process.Refresh();
            if (process.HasExited) break;
            if (process.MainWindowHandle != 0 && process.Responding)
            {
                var probe = LaunchInfo(process.StartInfo.FileName, process.StartInfo.WorkingDirectory, "--agent-status", "probe",
                    "--hwnd", process.MainWindowHandle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
                using var child = Process.Start(probe);
                if (child is not null && child.WaitForExit(4000) && child.ExitCode == 0) return;
            }
            Thread.Sleep(180);
        } while (timeout.Elapsed < TimeSpan.FromSeconds(20));
        throw new IOException("The updated WindowDock did not finish opening.");
    }

    private static void Deploy(string root, string payload, string backup, string executable, List<Change> applied)
    {
        var files = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories)
            .OrderBy(file => Path.GetRelativePath(payload, file) == "WindowDock.exe");
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(payload, file);
            var target = Path.Combine(root, relative == "WindowDock.exe" ? executable : relative);
            for (var path = target; !path.Equals(root, StringComparison.OrdinalIgnoreCase); path = Path.GetDirectoryName(path)!)
                if (File.Exists(path) || Directory.Exists(path)) RequirePlainPath(path);
            var saved = File.Exists(target) ? Path.Combine(backup, relative) : null;
            if (saved is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(target, saved);
            }
            Replace(file, target);
            applied.Add(new(target, saved));
        }
    }

    private static void Replace(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            for (var attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, target, true); break; }
                catch (IOException) when (attempt < 12) { Thread.Sleep(200); }
            }
        }
        finally { File.Delete(temporary); }
    }

    private static void CopyDirectory(string source, string target)
    {
        RequirePlainPath(source);
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        foreach (var file in Directory.EnumerateFiles(source))
        {
            RequirePlainPath(file);
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
    }

    private static void RequirePlainPath(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The portable update path contains a link.");
    }

    private static ProcessStartInfo LaunchInfo(string executable, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    internal static UpdateNotes? TakeNotes()
    {
        var path = NotesPath(Store.DirectoryPath);
        if (!File.Exists(path)) return null;
        var notes = JsonSerializer.Deserialize<UpdateNotes>(File.ReadAllText(path));
        File.Delete(path);
        return notes?.Version == RuntimePaths.ProductVersion ? notes : null;
    }

    internal static bool InProgress()
    {
        var path = Path.Combine(Path.GetDirectoryName(RuntimePaths.Executable)!, LockName);
        if (!File.Exists(path)) return false;
        try
        {
            using var lockCheck = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static bool TakeFailure()
    {
        var path = FailurePath(Store.DirectoryPath);
        if (!File.Exists(path)) return false;
        Store.Log("Portable update: " + File.ReadAllText(path));
        File.Delete(path);
        return true;
    }

    internal static void CleanCompletedJobs()
    {
        var root = Path.GetDirectoryName(RuntimePaths.Executable)!;
        foreach (var path in Directory.EnumerateDirectories(root, JobPrefix + "*"))
            if (Guid.TryParseExact(Path.GetFileName(path).AsSpan(JobPrefix.Length), "N", out _) &&
                File.Exists(Path.Combine(path, "complete"))) TryDeleteFolder(path);
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            RequirePlainPath(path);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
