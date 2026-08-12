using System.Collections.Concurrent;

namespace PerDeviceMixer.Audio;

internal sealed class AudioThreadDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly ManualResetEventSlim _started = new();
    private readonly Thread _thread;
    private Exception? _startupException;
    private int _threadId;
    private bool _disposed;

    public AudioThreadDispatcher(Action initialize)
    {
        _thread = new Thread(() => Run(initialize))
        {
            IsBackground = true,
            Name = "PerDeviceMixer Core Audio"
        };
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _started.Wait();

        if (_startupException is not null)
        {
            throw new InvalidOperationException("Core Audio initialization failed.", _startupException);
        }
    }

    public bool IsAudioThread => Environment.CurrentManagedThreadId == _threadId;

    public void Post(Action action)
    {
        if (_disposed || _queue.IsAddingCompleted) return;
        try
        {
            _queue.Add(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Invoke(Action action)
    {
        if (IsAudioThread)
        {
            action();
            return;
        }

        Invoke(() =>
        {
            action();
            return true;
        });
    }

    public T Invoke<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsAudioThread) return action();

        using var completed = new ManualResetEventSlim();
        T? result = default;
        Exception? failure = null;
        _queue.Add(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });
        completed.Wait();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    private void Run(Action initialize)
    {
        _threadId = Environment.CurrentManagedThreadId;
        try
        {
            initialize();
        }
        catch (Exception exception)
        {
            _startupException = exception;
        }
        finally
        {
            _started.Set();
        }

        if (_startupException is not null) return;

        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch
            {
                // Invoke transports failures to its caller. Posted notification work is
                // intentionally isolated so one disappearing endpoint cannot stop the pump.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        if (!IsAudioThread && _thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
        _queue.Dispose();
        _started.Dispose();
    }
}
