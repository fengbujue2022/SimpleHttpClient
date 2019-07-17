using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    public class HttpClient
    {
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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

            //TODO: raw request parser

            //await stream.WriteAsync();
            //await  stream.ReadAsync();

            //TODO: raw response resolver

            return null;
        }
    }
}
