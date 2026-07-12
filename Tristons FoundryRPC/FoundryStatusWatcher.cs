// =============================================================================
//  Triston's FoundryRPC  —  FoundryStatusWatcher.cs
//  Polls the configured Foundry servers every N seconds and raises StatusChanged
//  only when something meaningful changes (world id / active / user count).
//  Owns the locally-detected "world launch" timestamp used for Discord's elapsed
//  timer (Foundry's own "uptime" is process uptime and is never used), and
//  applies hysteresis so a transient server blip does not clear an active
//  presence.
//
//  NOTE: StatusChanged is raised on a background thread; subscribers that touch
//  UI must marshal to the UI thread themselves.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TristonsFoundryRPC;

public sealed class WorldStatusChangedEventArgs : EventArgs
{
    public WorldStatus Status { get; }

    /// <summary>Locally-detected launch time (UTC) for the elapsed timer; null when idle.</summary>
    public DateTime? LaunchedUtc { get; }

    public WorldStatusChangedEventArgs(WorldStatus status, DateTime? launchedUtc)
    {
        Status = status;
        LaunchedUtc = launchedUtc;
    }
}

public sealed class FoundryStatusWatcher : IDisposable
{
    private readonly Config _config;
    private readonly FoundryHttpClient _foundry;
    private readonly Logger _log;

    private WorldStatus _current = WorldStatus.Idle;
    private DateTime? _launchedUtc;
    private int _missCount;
    private bool _reportedInitial;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    // Lets the tray force an immediate poll (e.g. after the user re-enables RPC).
    private volatile TaskCompletionSource<bool> _wake =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler<WorldStatusChangedEventArgs>? StatusChanged;

    public FoundryStatusWatcher(Config config, FoundryHttpClient foundry, Logger log)
    {
        _config = config;
        _foundry = foundry;
        _log = log;
    }

    public void Start()
    {
        if (_loop is not null)
            return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
        _log.Info($"Watcher started (poll every {_config.PollIntervalSeconds}s, " +
                  $"idle after {_config.IdleAfterMissedPolls} missed polls).");
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;
        try
        {
            _cts.Cancel();
            _wake.TrySetResult(true);
            if (_loop is not null)
                await _loop.ConfigureAwait(false);
        }
        catch { /* shutdown best-effort */ }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _loop = null;
        }
    }

    /// <summary>Wake the loop now instead of waiting for the next interval.</summary>
    public void RequestImmediatePoll() => _wake.TrySetResult(true);

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error("Watcher poll threw", ex);
            }

            // Wait for the interval, or until someone requests an immediate poll.
            try
            {
                var delay = Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _config.PollIntervalSeconds)), ct);
                var completed = await Task.WhenAny(delay, _wake.Task).ConfigureAwait(false);
                if (completed == _wake.Task)
                    _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
        _log.Info("Watcher loop exited.");
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        // The client already handles per-server errors and returns Idle on any
        // failure, so this stays simple.
        var next = await _foundry.GetActiveWorldAsync(ct).ConfigureAwait(false);

        if (next.Active)
            OnActive(next);
        else
            OnNotActive();
    }

    private void OnActive(WorldStatus next)
    {
        _missCount = 0;

        bool worldChanged = !string.Equals(next.WorldId, _current.WorldId, StringComparison.Ordinal);
        bool wasIdle = !_current.Active;

        // A fresh session (idle->active, or the world id changed) restarts the timer.
        if (wasIdle || worldChanged)
        {
            _launchedUtc = DateTime.UtcNow;
            _log.Info($"World active: \"{next.Title ?? next.WorldId}\" ({next.WorldId}), " +
                      $"{next.UsersActive} online. Session timer started.");
        }

        bool shouldRaise = !_reportedInitial || next.IsMeaningfulChangeFrom(_current);
        _current = next;
        _reportedInitial = true;

        if (shouldRaise)
            Raise(_current, _launchedUtc);
    }

    private void OnNotActive()
    {
        if (_current.Active)
        {
            // Guard an active presence against transient blips (hysteresis).
            _missCount++;
            if (_missCount >= Math.Max(1, _config.IdleAfterMissedPolls))
            {
                _log.Info($"World went idle after {_missCount} missed polls. Clearing presence.");
                _current = WorldStatus.Idle;
                _launchedUtc = null;
                _missCount = 0;
                _reportedInitial = true;
                Raise(_current, null);
            }
            // else: keep showing the current presence during the grace window.
        }
        else if (!_reportedInitial)
        {
            // First poll and nothing is running — report idle once so the tray
            // moves off its "starting…" state immediately.
            _reportedInitial = true;
            _current = WorldStatus.Idle;
            Raise(_current, null);
        }
    }

    private void Raise(WorldStatus status, DateTime? launchedUtc)
    {
        try
        {
            StatusChanged?.Invoke(this, new WorldStatusChangedEventArgs(status, launchedUtc));
        }
        catch (Exception ex)
        {
            _log.Error("StatusChanged handler threw", ex);
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
    }
}
