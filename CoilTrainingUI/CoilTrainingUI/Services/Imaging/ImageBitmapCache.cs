using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CoilTrainingUI.Services.Imaging;

/// <summary>
/// Loads full-resolution images away from the UI thread and keeps a small,
/// bounded cache for nearby images in the review list.
/// </summary>
public sealed class ImageBitmapCache : IDisposable
{
    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _leastRecentlyUsed = new();
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private bool _disposed;

    public ImageBitmapCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public Task<BitmapSource> LoadCachedAsync(string imagePath, CancellationToken cancellationToken)
        => LoadAsync(imagePath, cacheResult: true, cancellationToken);

    public Task<BitmapSource> LoadUncachedAsync(string imagePath, CancellationToken cancellationToken)
        => LoadAsync(imagePath, cacheResult: false, cancellationToken);

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _leastRecentlyUsed.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _decodeGate.Dispose();
        _disposed = true;
    }

    private async Task<BitmapSource> LoadAsync(
        string imagePath,
        bool cacheResult,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path is empty.", nameof(imagePath));

        string fullPath = Path.GetFullPath(imagePath);
        if (cacheResult && TryGetValidEntry(fullPath, out BitmapSource cached))
            return cached;

        await _decodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cacheResult && TryGetValidEntry(fullPath, out cached))
                return cached;

            cancellationToken.ThrowIfCancellationRequested();
            BitmapSource bitmap = await Task.Run(
                    () => DecodeFrozenBitmap(fullPath),
                    cancellationToken)
                .ConfigureAwait(false);

            if (cacheResult)
                AddOrUpdate(fullPath, bitmap);

            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private bool TryGetValidEntry(string fullPath, out BitmapSource bitmap)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(fullPath, out CacheEntry? entry))
            {
                bitmap = null!;
                return false;
            }

            var info = new FileInfo(fullPath);
            if (!info.Exists ||
                info.Length != entry.FileLength ||
                info.LastWriteTimeUtc != entry.LastWriteTimeUtc)
            {
                RemoveEntry(entry);
                bitmap = null!;
                return false;
            }

            _leastRecentlyUsed.Remove(entry.Node);
            _leastRecentlyUsed.AddLast(entry.Node);
            bitmap = entry.Bitmap;
            return true;
        }
    }

    private void AddOrUpdate(string fullPath, BitmapSource bitmap)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("Image file was removed while it was loading.", fullPath);

        lock (_sync)
        {
            if (_entries.TryGetValue(fullPath, out CacheEntry? existing))
                RemoveEntry(existing);

            var node = _leastRecentlyUsed.AddLast(fullPath);
            _entries[fullPath] = new CacheEntry(
                fullPath,
                bitmap,
                info.Length,
                info.LastWriteTimeUtc,
                node);

            while (_entries.Count > _capacity && _leastRecentlyUsed.First != null)
            {
                string oldestPath = _leastRecentlyUsed.First.Value;
                if (_entries.TryGetValue(oldestPath, out CacheEntry? oldest))
                    RemoveEntry(oldest);
                else
                    _leastRecentlyUsed.RemoveFirst();
            }
        }
    }

    private void RemoveEntry(CacheEntry entry)
    {
        _entries.Remove(entry.Path);
        _leastRecentlyUsed.Remove(entry.Node);
    }

    private static BitmapSource DecodeFrozenBitmap(string fullPath)
    {
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private sealed record CacheEntry(
        string Path,
        BitmapSource Bitmap,
        long FileLength,
        DateTime LastWriteTimeUtc,
        LinkedListNode<string> Node);
}
