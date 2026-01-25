using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaseLib.Extensions
{
    public static class SemaphoreSlimExtensions
    {
        public static IDisposable Enter(this SemaphoreSlim semaphore)
        {
            semaphore.Wait();
            return new Releaser(semaphore);
        }

        public static async ValueTask<IAsyncDisposable> EnterAsync(
            this SemaphoreSlim semaphore,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new AsyncReleaser(semaphore);
        }

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim _semaphore;

            public Releaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                _semaphore?.Release();
                _semaphore = null;
            }
        }

        private sealed class AsyncReleaser : IAsyncDisposable
        {
            private SemaphoreSlim _semaphore;

            public AsyncReleaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public ValueTask DisposeAsync()
            {
                _semaphore?.Release();
                _semaphore = null;
                return ValueTask.CompletedTask;
            }
        }
    }
}
