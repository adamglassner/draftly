using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Draftly.Dialogs;
using Draftly.Services;

namespace Draftly;

/// <summary>Checking for and installing Draftly updates from inside the app.</summary>
public partial class MainWindow
{
    private bool _updating;
    private string _relaunchArgs;   // set when Draftly should start again after closing (portable update)

    /// <summary>On startup: tidy up after the last update, say that one happened, and check now and then.</summary>
    private void StartupUpdates()
    {
        if (App.IsSelfTest) return;
        UpdateService.CleanUp(UpdateService.ExeFolder);
        if (App.StartedAfterUpdate && !(_infoIsWindowLevel && InfoBar.IsVisible))
            ShowInfo($"Draftly was updated to version {UpdateService.CurrentDisplay}.", "What's new", () => OpenSettings("About"));
        _ = AutoCheckForUpdatesAsync();
    }

    private async Task AutoCheckForUpdatesAsync()
    {
        if (!S.CheckForUpdates || (DateTime.UtcNow - S.LastUpdateCheck).TotalHours < 20) return;
        await Task.Delay(TimeSpan.FromSeconds(4));   // let the window settle first
        UpdateService.UpdateInfo info;
        try { info = await UpdateService.CheckAsync(); }
        catch { return; }   // offline: try again next time
        S.LastUpdateCheck = DateTime.UtcNow;
        SettingsService.Save();
        if (info == null || info.Tag == S.SkippedUpdate || _updating) return;
        if (_infoIsWindowLevel && InfoBar.IsVisible) return;   // don't cover another prompt (recovery, default app)
        ShowUpdateAvailable(info);
    }

    private void ShowUpdateAvailable(UpdateService.UpdateInfo info) =>
        ShowInfo($"Draftly {info.Display} is available. You have {UpdateService.CurrentDisplay}.", "See what's new", () => OfferUpdate(info));

    /// <summary>File › Check for updates, Help › Check for updates, Settings › About.</summary>
    internal async void CheckForUpdatesNow()
    {
        if (_updating) return;
        UpdateService.UpdateInfo info;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.AppStarting;
        try { info = await UpdateService.CheckAsync(); }
        catch (Exception ex)
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
            var why = ex is HttpRequestException or TaskCanceledException ? "Check your internet connection and try again." : ex.Message;
            if (MessageDialog.Ask(this, "Check for updates", $"Draftly couldn't check for updates. {why}\n\nOpen the Draftly download page instead?"))
                OpenUrl(UpdateService.ReleasesPage);
            return;
        }
        finally { System.Windows.Input.Mouse.OverrideCursor = null; }
        S.LastUpdateCheck = DateTime.UtcNow;
        SettingsService.Save();
        if (info == null)
        {
            MessageDialog.Show(this, "Check for updates", $"You have the latest version of Draftly ({UpdateService.CurrentDisplay}).", MessageDialog.Kind.Info);
            return;
        }
        OfferUpdate(info);
    }

    private void OfferUpdate(UpdateService.UpdateInfo info)
    {
        var d = new UpdateDialog(info);
        d.ShowDialog();
        switch (d.Result)
        {
            case UpdateDialog.Choice.Update:
                HideInfo();
                _ = InstallUpdateAsync(info);
                break;
            case UpdateDialog.Choice.Skip:
                S.SkippedUpdate = info.Tag;
                SettingsService.Save();
                HideInfo();
                break;
        }
        Editor.Focus();
    }

    private async Task InstallUpdateAsync(UpdateService.UpdateInfo info)
    {
        if (_updating) return;
        var folder = UpdateService.ExeFolder;
        var kind = UpdateService.DetectKind(folder);
        var asset = kind == UpdateService.InstallKind.Setup ? info.Setup : info.Zip;
        if (asset == null || (kind == UpdateService.InstallKind.Portable && !UpdateService.CanWriteTo(folder)))
        {
            if (MessageDialog.Ask(this, "Update Draftly",
                    "Draftly can't update itself in the folder it's running from. Open the download page to get the new version?"))
                OpenUrl(info.Page ?? UpdateService.ReleasesPage);
            return;
        }

        // Ask about unsaved work now, before Draftly closes for the update.
        if (!ConfirmCloseAll()) return;

        _updating = true;
        _skipClosePrompts = true;
        try
        {
            ShowInfo($"Downloading Draftly {info.Display}…", "Cancel", () => _updateCancel?.Cancel());
            _updateCancel = new CancellationTokenSource();
            var progress = new Progress<double>(p => InfoText.Text = $"Downloading Draftly {info.Display}… {p:P0}");
            var file = await UpdateService.DownloadAsync(asset, progress, _updateCancel.Token);

            InfoText.Text = $"Installing Draftly {info.Display}…";
            if (kind == UpdateService.InstallKind.Setup)
            {
                try { UpdateService.RunSetup(file); }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)   // the user said no to Windows' permission prompt
                {
                    throw new OperationCanceledException();
                }
                // The setup closes and reopens Draftly; close now so it doesn't have to wait.
            }
            else
            {
                await Task.Run(() => UpdateService.ApplyPortable(file, folder));
                _relaunchArgs = "--updated";
            }
            Close();
        }
        catch (OperationCanceledException)
        {
            _updating = false;
            _skipClosePrompts = false;
            ShowInfo("The update was cancelled.");
        }
        catch (Exception ex)
        {
            _updating = false;
            _skipClosePrompts = false;
            HideInfo();
            if (MessageDialog.Ask(this, "Update Draftly", $"The update couldn't be installed.\n\n{ex.Message}\n\nOpen the download page instead?"))
                OpenUrl(info.Page ?? UpdateService.ReleasesPage);
        }
    }

    private CancellationTokenSource _updateCancel;

    /// <summary>Starts the updated Draftly once this window has saved its session (portable updates).</summary>
    private void RelaunchIfUpdated()
    {
        if (_relaunchArgs == null) return;
        try { Process.Start(new ProcessStartInfo(UpdateService.ExePath, _relaunchArgs) { UseShellExecute = false }); }
        catch { }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
