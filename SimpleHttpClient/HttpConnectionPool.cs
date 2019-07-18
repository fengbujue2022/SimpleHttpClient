using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Runtime.CompilerServices;
using System.IO;
using System.Collections.Concurrent;

namespace SimpleHttpClient
{
    internal class HttpConnectionPool
    {
        private readonly List<CachedConnection> _idleConnections = new List<CachedConnection>();

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (connection, isNewConnection, failureResponse) = await GetConnectionAsync(request, cancellationToken);

            return await connection.SendAsync(request, cancellationToken); ;
        }

        private async ValueTask<(HttpConnection connection, bool isNewConnection, HttpResponseMessage failureResponse)>
            GetConnectionAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            //that's so complex,let me look some time
            if (false)
            {
                var connection = await GetOrReserveHttpConnectionAsync(cancellationToken);
                if (connection != null)
                {
                    return (connection, false, null);
                }
            }

            var (sokect, stream, failureResponse) = await ConnectAsync(request, cancellationToken);

            return (ConstructHttpConnection(sokect, stream), true, null);
        }

        private async ValueTask<HttpConnection> GetOrReserveHttpConnectionAsync(CancellationToken cancellationToken)
        {
            var conn = _idleConnections.FirstOrDefault()._connection;

            return conn;
        }

        private async ValueTask<(Socket, Stream, HttpResponseMessage)> ConnectAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            //TODO: this object need pooling
            var saea = new SocketAsyncEventArgs();

            var taskBuilder = new AsyncTaskMethodBuilder();
            _ = taskBuilder.Task;

            saea.Completed += (s, args) =>
            {
                switch (args.SocketError)
                {
                    case SocketError.Success:
                        taskBuilder.SetResult();
                        break;
                    case SocketError.OperationAborted:
                    case SocketError.ConnectionAborted:
                        if (cancellationToken.IsCancellationRequested)
                        {
                            taskBuilder.SetException(new OperationCanceledException("JOJO 我不做人啦！"));
                            break;
                        }
                        goto default;
                    default:
                        taskBuilder.SetException(new SocketException((int)args.SocketError));
                        break;
                }
            };

            saea.RemoteEndPoint = new DnsEndPoint(request.RequestUri.IdnHost, request.RequestUri.Port);

            if (Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, saea))
            {
                using (cancellationToken.Register((s) => Socket.CancelConnectAsync((SocketAsyncEventArgs)s), saea))
                {
                    //waiting for Completed event emit
                    await taskBuilder.Task.ConfigureAwait(false);
                }
            }

            var socket = saea.ConnectSocket;
            var stream = new NetworkStream(socket);

            return (socket, stream, null);
        }

        private HttpConnection ConstructHttpConnection(Socket socket, Stream stream)
        {
            return new HttpConnection(this, socket, stream);
        }
    }
}
