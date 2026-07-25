using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoilTrainingUI.Services.Automation;

public sealed class TrainingAutomationUpdate
{
    public BatchReconcileResult? BatchResult { get; init; }
    public ActivationResult? ActivationResult { get; init; }
    public Exception? Error { get; init; }
}

public sealed class TrainingAutomationCoordinator : IDisposable
{
    private readonly AutomationSettings _settings;
    private readonly BatchInboxReconciler _batchReconciler;
    private readonly ActivationResultSynchronizer _activationSynchronizer;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private readonly FileSystemWatcher _batchWatcher;
    private readonly FileSystemWatcher _resultWatcher;
    private readonly Timer _periodicTimer;
    private readonly Timer _debounceTimer;
    private bool _disposed;

    public TrainingAutomationCoordinator(
        AutomationSettings settings,
        BatchInboxReconciler batchReconciler,
        ActivationResultSynchronizer activationSynchronizer)
    {
        _settings = settings;
        _batchReconciler = batchReconciler;
        _activationSynchronizer = activationSynchronizer;
        AutomationPaths.EnsureLayout(settings.ExchangeRoot);

        _batchWatcher = new FileSystemWatcher(AutomationPaths.Outbox(settings.ExchangeRoot), "DONE.flag")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
        };
        _batchWatcher.Created += WatcherChanged;
        _batchWatcher.Changed += WatcherChanged;
        _batchWatcher.Renamed += WatcherChanged;

        _resultWatcher = new FileSystemWatcher(AutomationPaths.Control(settings.ExchangeRoot), "activation_result.json")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
        };
        _resultWatcher.Created += WatcherChanged;
        _resultWatcher.Changed += WatcherChanged;
        _resultWatcher.Renamed += WatcherChanged;

        _debounceTimer = new Timer(_ => _ = ReconcileAsync(), null, Timeout.Infinite, Timeout.Infinite);
        int intervalMilliseconds = settings.ReconcileIntervalSeconds * 1000;
        _periodicTimer = new Timer(_ => _ = ReconcileAsync(), null, intervalMilliseconds, intervalMilliseconds);
    }

    public event EventHandler<TrainingAutomationUpdate>? Updated;

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TrainingAutomationCoordinator));
        _batchWatcher.EnableRaisingEvents = true;
        _resultWatcher.EnableRaisingEvents = true;
        _ = ReconcileAsync();
    }

    public Task ReconcileNowAsync() => ReconcileAsync();

    private void WatcherChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed)
            return;
        _debounceTimer.Change(500, Timeout.Infinite);
    }

    private async Task ReconcileAsync()
    {
        if (_disposed || !await _reconcileGate.WaitAsync(0).ConfigureAwait(false))
            return;
        try
        {
            BatchReconcileResult? batchResult = null;
            if (_settings.AutoImportBatches)
                batchResult = await Task.Run(_batchReconciler.Reconcile).ConfigureAwait(false);
            ActivationResult? activationResult = await Task.Run(_activationSynchronizer.Reconcile).ConfigureAwait(false);
            Updated?.Invoke(this, new TrainingAutomationUpdate
            {
                BatchResult = batchResult,
                ActivationResult = activationResult
            });
        }
        catch (Exception ex)
        {
            Updated?.Invoke(this, new TrainingAutomationUpdate { Error = ex });
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _batchWatcher.EnableRaisingEvents = false;
        _resultWatcher.EnableRaisingEvents = false;
        _batchWatcher.Dispose();
        _resultWatcher.Dispose();
        _periodicTimer.Dispose();
        _debounceTimer.Dispose();
        _reconcileGate.Dispose();
    }
}
