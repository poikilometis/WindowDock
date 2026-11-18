using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed partial class MainWindow
{
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly Button updateButton;
    private DateTimeOffset nextReleaseCheck = DateTimeOffset.UtcNow.AddSeconds(3), updateReminder;
    private ReleaseOffer? availableRelease;
    private string? dismissedRelease;
    private UpdateCard? updateCard;
    private Border? releaseNotesPage;
    private StackPanel? releaseNotesTab;
    private Button? releaseNotesLabel;
    private bool checkingUpdate, installingUpdate, releaseNotesSelected;

    private static bool UpdatesAvailable => OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
        System.Runtime.InteropServices.Architecture.X64;

    private void InitializeUpdates()
    {
        if (!UpdatesAvailable || Program.IsSelfTest || Program.PerformanceReport is not null || Program.CrashTarget != 0) return;
        try
        {
            if (PortableUpdate.TakeFailure()) SetStatus(L10n.Get("Update.Failed"));
            if (PortableUpdate.TakeNotes() is { } notes)
            {
                Session.Poll(forceRestore: true);
                ShowReleaseNotes(notes);
            }
        }
        catch (Exception exception) { Store.Log(exception.Message); }
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000).ConfigureAwait(false);
            try { PortableUpdate.CleanCompletedJobs(); }
            catch (IOException exception) { Store.Log(exception.Message); }
            catch (UnauthorizedAccessException exception) { Store.Log(exception.Message); }
        });
    }

    private void PollUpdates()
    {
        if (!UpdatesAvailable || Program.IsSelfTest || Program.PerformanceReport is not null || Program.CrashTarget != 0) return;
        if (DateTimeOffset.UtcNow >= nextReleaseCheck) _ = CheckForUpdateAsync(manual: false);
        ShowUpdateCardIfNeeded();
    }

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (closing || !UpdatesAvailable || checkingUpdate || installingUpdate) return;
        if (manual && availableRelease is not null)
        {
            dismissedRelease = null;
            ShowUpdateCardIfNeeded();
            return;
        }
        checkingUpdate = true;
        nextReleaseCheck = DateTimeOffset.UtcNow.AddHours(4);
        if (manual) SetStatus(L10n.Get("Update.Checking"));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var offer = await ReleaseUpdates.LatestAsync(timeout.Token);
            if (closing) return;
            if (availableRelease?.Version != offer?.Version) HideUpdateCard();
            availableRelease = offer;
            updateButton.Classes.Set("primary", offer is not null);
            if (updateButton.Content is StackPanel row && row.Children.OfType<DockIcon>().FirstOrDefault() is { } icon)
            {
                icon.InkResource = offer is null ? null : "DockAccentInk";
                icon.InvalidateVisual();
            }
            if (manual) SetStatus(offer is null ? L10n.Get("Update.Current") : "");
            ShowUpdateCardIfNeeded();
        }
        catch (Exception exception)
        {
            if (closing) return;
            Store.Log("Update check: " + exception.Message);
            if (manual) SetStatus(L10n.Get("Update.CheckFailed"));
        }
        finally { checkingUpdate = false; }
    }

    private void ShowUpdateCardIfNeeded()
    {
        if (closing || !IsVisible || WindowState == WindowState.Minimized || dialogDepth > 0)
        {
            HideUpdateCard();
            return;
        }
        if (updateCard is not null || availableRelease is not { } offer || installingUpdate ||
            dismissedRelease == offer.Version && DateTimeOffset.UtcNow < updateReminder) return;
        var card = new UpdateCard(offer, () => _ = InstallUpdateAsync(offer), () => DismissUpdate(offer.Version));
        updateCard = card;
        card.Closed += (_, _) =>
        {
            if (updateCard != card) return;
            updateCard = null;
            dismissedRelease = offer.Version;
            updateReminder = DateTimeOffset.UtcNow.AddHours(24);
        };
        card.SizeChanged += (_, _) => PositionUpdateCard();
        card.Position = UpdateCardPosition(card.Width, 120);
        card.Show(this);
        RaiseUpdateCard();
        PositionUpdateCard();
    }

    private void RaiseUpdateCard()
    {
        if (updateCard?.TryGetPlatformHandle() is { } handle) Session.Backend.RaiseOverlay(handle.Handle);
    }

    private void DismissUpdate(string version)
    {
        dismissedRelease = version;
        updateReminder = DateTimeOffset.UtcNow.AddHours(24);
        HideUpdateCard();
    }

    private void HideUpdateCard()
    {
        var card = updateCard;
        updateCard = null;
        card?.Close();
    }

    private PixelPoint UpdateCardPosition(double width, double height) => this.PointToScreen(new Point(
        ClientSize.Width - width - 14, ClientSize.Height - height - 14));

    private void PositionUpdateCard()
    {
        if (updateCard is { } card && IsVisible)
            card.Position = UpdateCardPosition(card.Width, card.ClientSize.Height > 0 ? card.ClientSize.Height : 120);
    }

    private async Task InstallUpdateAsync(ReleaseOffer offer)
    {
        if (installingUpdate || closing || availableRelease?.Version != offer.Version) return;
        installingUpdate = true;
        updateCard?.SetInstalling(true);
        SetStatus(L10n.Get("Update.Downloading"));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(4));
            await PortableUpdate.PrepareAsync(offer, timeout.Token);
            if (!closing) Close();
        }
        catch (Exception exception)
        {
            if (closing) return;
            Store.Log("Update installation: " + exception.Message);
            SetStatus(L10n.Get("Update.Failed"));
            updateCard?.SetInstalling(false, failed: true);
        }
        finally { installingUpdate = false; }
    }

    private void ShowReleaseNotes(UpdateNotes notes)
    {
        var title = new TextBlock { Text = L10n.Format("Update.WhatsNewVersion", notes.Version), FontSize = 26,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { MaxWidth = 760, Spacing = 12, Margin = new(28, 24), HorizontalAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(title);
        foreach (var line in notes.Notes.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).Take(160))
        {
            var heading = line.StartsWith('#');
            var text = line.TrimStart('#').TrimStart().Replace("**", "").Replace("`", "");
            if (text.StartsWith("- ")) text = "• " + text[2..];
            content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
                FontSize = heading ? 17 : 13, FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal });
        }
        var link = DockControls.Button(L10n.Get("Update.Changelog"), "detach", () => UpdateCard.OpenRelease(notes.Version));
        link.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(link);
        releaseNotesPage = new Border { Background = DockTheme.Ground, Child = new ScrollViewer { Content = content } };
        Grid.SetRow(releaseNotesPage, 1);
        workspace.Children.Add(releaseNotesPage);

        releaseNotesLabel = DockControls.Button(L10n.Get("Update.WhatsNew"), "", () => SelectReleaseNotes(true));
        releaseNotesLabel.Classes.Add("tab");
        var close = DockControls.Button("", "close", CloseReleaseNotes, tip: L10n.Get("MainWindowCommands.CloseTab"));
        close.MinWidth = close.Width = 24;
        releaseNotesTab = new StackPanel { Orientation = Orientation.Horizontal, Children = { releaseNotesLabel, close } };
        tabs.Children.Add(releaseNotesTab);
        SelectReleaseNotes(true);
    }

    private void SelectReleaseNotes(bool selected)
    {
        if (releaseNotesSelected == selected || releaseNotesPage is null) return;
        releaseNotesSelected = selected;
        releaseNotesPage.IsVisible = selected;
        Refresh();
        if (selected && releaseNotesTab is not null) releaseNotesTab.BringIntoView();
    }

    private void CloseReleaseNotes()
    {
        SelectReleaseNotes(false);
        if (releaseNotesTab is not null) tabs.Children.Remove(releaseNotesTab);
        if (releaseNotesPage is not null) workspace.Children.Remove(releaseNotesPage);
        releaseNotesTab = null;
        releaseNotesPage = null;
        releaseNotesLabel = null;
    }
}
