using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace RogueStars.Client.Services
{
    public sealed class HttpRequest
    {
        public string Method = "GET";
        public string Url;
        /// <summary>JSON body or null.</summary>
        public string Body;
        /// <summary>Request timeout; 0 = the transport's default.</summary>
        public int TimeoutSeconds;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
        public override string ToString() => Method + " " + Url;
    }

    public sealed class HttpResponse
    {
        /// <summary>HTTP status; 0 when the request never reached the server.</summary>
        public long Status;
        public string Body;
        /// <summary>Connection/DNS/timeout error text, or null.</summary>
        public string NetworkError;
    }

    /// <summary>Pluggable HTTP layer so RemoteGameService can be tested with a fake.</summary>
    public interface IHttpTransport
    {
        void Send(HttpRequest request, Action<HttpResponse> done);
    }

    /// <summary>UnityWebRequest transport; the completion callback runs on the main thread.</summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        public int TimeoutSeconds = 20;

        public void Send(HttpRequest request, Action<HttpResponse> done)
        {
            var uwr = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : TimeoutSeconds,
            };
            if (request.Body != null)
            {
                uwr.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
                uwr.SetRequestHeader("Content-Type", "application/json");
            }
            foreach (var kv in request.Headers) uwr.SetRequestHeader(kv.Key, kv.Value);

            var op = uwr.SendWebRequest();
            op.completed += _ =>
            {
                var resp = new HttpResponse
                {
                    Status = uwr.responseCode,
                    Body = uwr.downloadHandler != null ? uwr.downloadHandler.text : null,
                    NetworkError = uwr.result == UnityWebRequest.Result.ConnectionError ? uwr.error : null,
                };
                uwr.Dispose();
                done(resp);
            };
        }
    }
}
