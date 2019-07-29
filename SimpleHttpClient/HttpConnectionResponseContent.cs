using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    internal sealed class HttpConnectionResponseContent : HttpContent
    {
        private Stream _stream;
        private bool _consumedStream; // separate from _stream so that Dispose can drain _stream

        public void SetStream(Stream stream)
        {
            _stream = stream;
        }

        private Stream ConsumeStream()
        {
            if (_consumedStream || _stream == null)
            {
                throw new InvalidOperationException("stream已经被读取了");
            }
            _consumedStream = true;

            return _stream;
        }

        protected sealed override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            using (Stream contentStream = ConsumeStream())
            {
                const int BufferSize = 8192;
                await contentStream.CopyToAsync(stream, BufferSize).ConfigureAwait(false);
            }
        }

        protected sealed override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected sealed override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(ConsumeStream());


        protected sealed override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_stream != null)
                {
                    _stream.Dispose();
                    _stream = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
