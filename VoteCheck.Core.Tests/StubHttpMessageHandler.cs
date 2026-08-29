using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VoteCheck.Core.Tests
{
    // Minimal mock transport: constructor-injected HttpClient means no reflection is
    // needed to substitute it (unlike the legacy VoteCollectorTests mocks).
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public string? RequestedUrl { get; private set; }
        public string? RequestBody { get; private set; }
        public string? RequestUserAgent { get; private set; }

        public StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrl = request.RequestUri?.ToString();
            RequestBody = request.Content?.ReadAsStringAsync(cancellationToken).Result;
            RequestUserAgent = request.Headers.UserAgent.Count > 0
                ? request.Headers.UserAgent.ToString()
                : null;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            });
        }
    }
}
