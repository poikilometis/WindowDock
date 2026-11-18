using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WindowDock;

namespace WindowDock.Tests;

internal static partial class Program
{
    private static void AppearanceAndUpdates()
    {
        var common = new SurfaceStyle { Fill = "flow", StartColor = 0xff2c6470, EndColor = 0xff423253, TitleColor = 0xfff6e0b1, Opacity = 72 };
        var local = new SurfaceStyle { Opacity = 55 };
        var config = AppConfig.Default();
        config.WindowStyle = common;
        config.HeaderStyle = new() { Opacity = 0 };
        config.TabStyle = common with { Fill = "gradient" };
        config.Workspaces[0].TabStyle = new() { Fill = "theme" };
        config.SleepMode = "agents";
        var loaded = GroupPersistence.Clone(config);
        loaded.Validate();
        var group = GroupPersistence.Clone(new GroupState { Panels = [new() { Style = local }] });
        var effective = SurfaceStyle.Resolve(loaded.WindowStyle, group.Panels[0].Style);
        Check(effective == common with { Opacity = 55 }, "A saved window's opacity override should inherit its colors and gradient.");
        Check(loaded.HeaderStyle.OpacityPercent() == 0 && loaded.SleepMode == "agents", "Transparent chrome or sleep preference did not persist.");
        Check(!SurfaceStyle.Resolve(loaded.TabStyle, loaded.Workspaces[0].TabStyle).HasFill, "A tab cannot explicitly restore theme colors.");

        using (var session = Session(new FakeBackend()))
        {
            session.Attach(Candidate(120));
            var handle = session.Panels[0].Record.Handle;
            session.ReceiveAgentSignal(new() { Handle = handle, SessionKey = "codex:sleep-fixture", Status = "working" });
            Check(session.HasWorkingHookSession(), "A working hook session should prevent automatic sleep.");
            session.ReceiveAgentSignal(new() { Handle = handle, SessionKey = "codex:sleep-fixture", Status = "waiting" });
            session.ReceiveAgentSignal(new() { Handle = handle, Status = "working" });
            Check(!session.HasWorkingHookSession(), "A hook awaiting input or an unkeyed status should allow automatic sleep.");
            session.ReceiveAgentSignal(new() { Handle = handle, SessionKey = "codex:sleep-fixture", Status = "working" });
            session.ReleasePanels([session.Panels[0]]);
            Check(!session.HasWorkingHookSession(), "An undocked agent retained the sleep request.");
        }

        const string name = "WindowDock-0.2.2-win-x64.zip";
        var digest = new string('a', 64);
        byte[] Release(bool prerelease = false, bool checksum = true) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            draft = false, prerelease, tag_name = "v0.2.2", body = "## What's new",
            assets = new[] { new { name, size = 128, digest = "sha256:" + digest },
                new { name = checksum ? name + ".sha256" : "source.zip", size = 94, digest = "" } }
        });
        var offer = ReleaseUpdates.Parse(Release(), "0.2.1");
        Check(offer is { Version: "0.2.2", PackageLength: 128 } && offer.PublishedDigest == digest &&
            offer.Asset(name).Host == "github.com", "The stable Windows release metadata was not recognized.");
        Check(ReleaseUpdates.Parse(Release(), "0.2.2") is null && ReleaseUpdates.Parse(Release(), "0.2.3") is null &&
            ReleaseUpdates.Parse(Release(prerelease: true), "0.2.1") is null &&
            ReleaseUpdates.Parse(Release(checksum: false), "0.2.1") is null, "Updater offered an unsupported or already installed release.");

        var folder = Directory.CreateDirectory(Path.Combine(root, "update-fixtures")).FullName;
        var zip = Path.Combine(folder, name);
        var target = Path.Combine(folder, "payload");
        string Package(string entry)
        {
            using (var archive = new ZipArchive(File.Create(zip), ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("fixture");
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
        }
        var validDigest = Package(".runtime/fixture.dll");
        ReleaseUpdates.ExtractVerified(zip, target, validDigest);
        Check(File.ReadAllText(Path.Combine(target, ".runtime", "fixture.dll")) == "fixture", "Portable package extraction excluded its bundled runtime.");
        void RejectPackage(string invalidDigest)
        {
            var rejected = false;
            try { ReleaseUpdates.ExtractVerified(zip, target, invalidDigest); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "The updater accepted a mismatched digest or a path outside its program scope.");
        }
        RejectPackage(digest);
        RejectPackage(Package("../outside.txt"));
        RejectPackage(Package("data/settings.json"));
        report.Add("PASS surface inheritance and persistence, hook-aware sleep, release eligibility, and verified archive extraction.");
    }
}
