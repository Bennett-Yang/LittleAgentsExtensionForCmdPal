using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace LittleAgentsExtension.Tests;

public sealed partial class OpenAiChatClientTests
{
    internal sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;
        private readonly CancellationTokenSource? _requestCancellationSource;

        private FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync, CancellationTokenSource? requestCancellationSource = null)
        {
            _sendAsync = sendAsync;
            _requestCancellationSource = requestCancellationSource;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public CancellationToken LastRequestCancellationToken { get; private set; }
        public TaskCompletionSource ReadPending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static FakeHttpHandler SseChunks(int chunkCount) => new((request, cancellationToken) =>
        {
            string body = CreateSseBody(chunkCount) + "data: [DONE]\n\n";
            return Task.FromResult(CreateSseResponse(new MemoryStream(Encoding.UTF8.GetBytes(body))));
        });

        public static FakeHttpHandler CancellableAfterFirstChunk()
        {
            CancellationTokenSource requestCancellationSource = new();
            FakeHttpHandler? handler = null;
            handler = new FakeHttpHandler((request, cancellationToken) =>
            {
                return Task.FromResult(CreateSseResponse(new CancellableSseStream(requestCancellationSource, handler!.ReadPending)));
            }, requestCancellationSource);
            return handler;
        }

        public static FakeHttpHandler Status(HttpStatusCode statusCode) => new((request, cancellationToken) =>
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent("{\"error\":{\"message\":\"status failure\"}}", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        });

        public static FakeHttpHandler Throws(HttpRequestException exception) => new((request, cancellationToken) => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestCancellationToken = _requestCancellationSource?.Token ?? cancellationToken;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _sendAsync(request, cancellationToken);
        }

        private static HttpResponseMessage CreateSseResponse(Stream stream)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return response;
        }

        private static string CreateSseBody(int chunkCount)
        {
            StringBuilder builder = new();
            for (int index = 0; index < chunkCount; index++)
            {
                builder.Append("data: {\"choices\":[{\"delta\":{\"content\":\"foo\"}}]}\n\n");
            }

            return builder.ToString();
        }
    }

    private sealed class CancellableSseStream : Stream
    {
        private readonly CancellationTokenSource _requestCancellationSource;
        private readonly TaskCompletionSource _readPending;
        private readonly byte[] _firstChunk = Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"foo\"}}]}\n\n");
        private bool _sentFirstChunk;

        public CancellableSseStream(CancellationTokenSource requestCancellationSource, TaskCompletionSource readPending)
        {
            _requestCancellationSource = requestCancellationSource;
            _readPending = readPending;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_sentFirstChunk)
            {
                _sentFirstChunk = true;
                Array.Copy(_firstChunk, 0, buffer, offset, _firstChunk.Length);
                return _firstChunk.Length;
            }

            _readPending.TrySetResult();
            _requestCancellationSource.Token.WaitHandle.WaitOne();
            throw new OperationCanceledException(_requestCancellationSource.Token);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_sentFirstChunk)
            {
                _sentFirstChunk = true;
                Array.Copy(_firstChunk, 0, buffer, offset, _firstChunk.Length);
                return Task.FromResult(_firstChunk.Length);
            }

            _readPending.TrySetResult();
            return WaitForCancellationAsync(cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sentFirstChunk)
            {
                _sentFirstChunk = true;
                _firstChunk.CopyTo(buffer);
                return _firstChunk.Length;
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_requestCancellationSource.Token, cancellationToken);
            try
            {
                _readPending.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            }
            catch (OperationCanceledException)
            {
                _requestCancellationSource.Cancel();
                throw;
            }

            return 0;
        }

        private async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_requestCancellationSource.Token, cancellationToken);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            }
            catch (OperationCanceledException)
            {
                _requestCancellationSource.Cancel();
                throw;
            }

            return 0;
        }
    }
}
