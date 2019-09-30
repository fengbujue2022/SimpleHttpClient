using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleHttpClient
{
    internal abstract class DecompressedContent : HttpContent
    {
        HttpContent _originalContent;
        bool _contentConsumed;

        public DecompressedContent(HttpContent originalContent)
        {
            _originalContent = originalContent;
            _contentConsumed = false;

            // Copy original response headers, but with the following changes:
            //   Content-Length is removed, since it no longer applies to the decompressed content
            //   The last Content-Encoding is removed, since we are processing that here.
            foreach (var header in originalContent.Headers)
            {
                Headers.Add(header.Key, header.Value);
            }
            Headers.ContentLength = null;
            Headers.ContentEncoding.Clear();
            string prevEncoding = null;
            foreach (string encoding in originalContent.Headers.ContentEncoding)
            {
                if (prevEncoding != null)
                {
                    Headers.ContentEncoding.Add(prevEncoding);
                }
                prevEncoding = encoding;
            }
        }

        protected abstract Stream GetDecompressedStream(Stream originalStream);

        protected async override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            using (Stream decompressedStream = await CreateContentReadStreamAsync().ConfigureAwait(false))
            {
                await decompressedStream.CopyToAsync(stream).ConfigureAwait(false);
            }
        }

        protected override async Task<Stream> CreateContentReadStreamAsync()
        {
            if (_contentConsumed)
            {
                throw new InvalidOperationException("net_http_content_stream_already_read");
            }

            _contentConsumed = true;

            Stream originalStream = await _originalContent.ReadAsStreamAsync().ConfigureAwait(false);
            return GetDecompressedStream(originalStream);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _originalContent.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class GZipDecompressedContent : DecompressedContent
    {
        public GZipDecompressedContent(HttpContent originalContent)
            : base(originalContent)
        { }

        protected override Stream GetDecompressedStream(Stream originalStream) =>
            new GZipStream(originalStream, CompressionMode.Decompress);
    }

    internal sealed class DeflateDecompressedContent : DecompressedContent
    {
        public DeflateDecompressedContent(HttpContent originalContent)
            : base(originalContent)
        { }

        protected override Stream GetDecompressedStream(Stream originalStream) =>
            new DeflateStream(originalStream, CompressionMode.Decompress);
    }

    internal sealed class BrotliDecompressedContent : DecompressedContent
    {
        public BrotliDecompressedContent(HttpContent originalContent) :
            base(originalContent)
        {
        }

        protected override Stream GetDecompressedStream(Stream originalStream) =>
            new BrotliStream(originalStream, CompressionMode.Decompress);
    }
}

