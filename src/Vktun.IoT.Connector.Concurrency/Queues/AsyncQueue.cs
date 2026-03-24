using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Concurrency.Queues;

public class AsyncQueue<T> : IAsyncDisposable
{
    private readonly BlockingCollection<T> _queue;
    private readonly SemaphoreSlim _signal;
    private readonly int _capacity;
    private bool _isDisposed;

    public int Count => _queue.Count;
    public int Capacity => _capacity;
    public bool IsFull => _queue.Count >= _capacity;
    public bool IsEmpty => _queue.Count == 0;

    public AsyncQueue(int capacity = 10000)
    {
        _capacity = capacity;
        _queue = new BlockingCollection<T>(capacity);
        _signal = new SemaphoreSlim(0, capacity);
    }

    public bool TryEnqueue(T item)
    {
        if (_isDisposed || IsFull)
        {
            return false;
        }

        try
        {
            _queue.Add(item);
            _signal.Release();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnqueueAsync(T item, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return false;
        }

        try
        {
            _queue.Add(item);
            _signal.Release();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryDequeue(out T? item)
    {
        item = default;
        if (_isDisposed || IsEmpty)
        {
            return false;
        }

        return _queue.TryTake(out item);
    }

    public async Task<T?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return default;
        }

        await _signal.WaitAsync(cancellationToken);
        
        if (_queue.TryTake(out var item))
        {
            return item;
        }
        
        return default;
    }

    public IAsyncEnumerable<T> GetConsumingEnumerable(CancellationToken cancellationToken = default)
    {
        return GetConsumingEnumerableAsync(cancellationToken);
    }

    private async IAsyncEnumerable<T> GetConsumingEnumerableAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_isDisposed)
        {
            var item = await DequeueAsync(cancellationToken);
            if (item != null)
            {
                yield return item;
            }
        }
    }

    public void Clear()
    {
        while (_queue.TryTake(out _)) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _queue.CompleteAdding();
        _queue.Dispose();
        _signal.Dispose();
        
        GC.SuppressFinalize(this);
    }
}

public class PriorityAsyncQueue<T> where T : IComparable<T>
{
    private readonly ConcurrentPriorityQueue<T> _queue;
    private readonly SemaphoreSlim _signal;
    private readonly int _capacity;
    private bool _isDisposed;

    public int Count => _queue.Count;
    public int Capacity => _capacity;
    public bool IsFull => _queue.Count >= _capacity;
    public bool IsEmpty => _queue.Count == 0;

    public PriorityAsyncQueue(int capacity = 10000)
    {
        _capacity = capacity;
        _queue = new ConcurrentPriorityQueue<T>();
        _signal = new SemaphoreSlim(0, capacity);
    }

    public bool TryEnqueue(T item)
    {
        if (_isDisposed || IsFull)
        {
            return false;
        }

        _queue.Enqueue(item);
        _signal.Release();
        return true;
    }

    public async Task<T?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return default;
        }

        await _signal.WaitAsync(cancellationToken);
        
        if (_queue.TryDequeue(out var item))
        {
            return item;
        }
        
        return default;
    }

    public void Clear()
    {
        _queue.Clear();
    }
}

internal class ConcurrentPriorityQueue<T> where T : IComparable<T>
{
    private readonly object _lock = new();
    private readonly List<T> _data = new();

    public int Count
    {
        get { lock (_lock) return _data.Count; }
    }

    public void Enqueue(T item)
    {
        lock (_lock)
        {
            _data.Add(item);
            var ci = _data.Count - 1;
            while (ci > 0)
            {
                var pi = (ci - 1) / 2;
                if (_data[ci].CompareTo(_data[pi]) >= 0)
                    break;
                (_data[ci], _data[pi]) = (_data[pi], _data[ci]);
                ci = pi;
            }
        }
    }

    public bool TryDequeue(out T? item)
    {
        lock (_lock)
        {
            if (_data.Count == 0)
            {
                item = default;
                return false;
            }

            var li = _data.Count - 1;
            item = _data[0];
            _data[0] = _data[li];
            _data.RemoveAt(li);

            li--;
            var pi = 0;
            while (true)
            {
                var ci = pi * 2 + 1;
                if (ci > li) break;
                var rc = ci + 1;
                if (rc <= li && _data[rc].CompareTo(_data[ci]) < 0)
                    ci = rc;
                if (_data[pi].CompareTo(_data[ci]) <= 0) break;
                (_data[pi], _data[ci]) = (_data[ci], _data[pi]);
                pi = ci;
            }
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _data.Clear();
        }
    }
}
