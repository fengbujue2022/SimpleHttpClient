using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal class TaskCompletionSourceWithCancellation<T> : TaskCompletionSource<T>
    {
        private CancellationToken _cancellationToken;

        public TaskCompletionSourceWithCancellation() : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
        }

        private void OnCancellation()
        {
            TrySetCanceled(_cancellationToken);
        }

        public async Task<T> WaitWithCancellationAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            using (cancellationToken.Register(s => ((TaskCompletionSourceWithCancellation<T>)s).OnCancellation(), this))
            {
                var connection = await Task.ConfigureAwait(false);
                return connection;
            }
        }
    }
}
