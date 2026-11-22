using System;
using System.Collections.Generic;
using System.IO;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RogueStars.Client.Tests
{
    internal static class TestUtil
    {
        /// <summary>2026-03-02 12:00 UTC: a fixed, deterministic "now".</summary>
        public const long T0 = 1_772_452_800_000L;

        public static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rs_client_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void DeleteDir(string dir)
        {
            try { if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    /// <summary>Records requests and answers them synchronously from a handler.</summary>
    internal sealed class FakeTransport : IHttpTransport
    {
        public readonly List<HttpRequest> Requests = new List<HttpRequest>();
        public Func<HttpRequest, HttpResponse> Handler = _ => new HttpResponse { Status = 404, Body = "{\"ok\":false,\"error\":\"not_found\"}" };

        public void Send(HttpRequest request, Action<HttpResponse> done)
        {
            Requests.Add(request);
            done(Handler(request));
        }

        public static HttpResponse Json(long status, string body) => new HttpResponse { Status = status, Body = body };
    }

    /// <summary>A ClientApp on a fresh canvas with an offline service in a temp directory.</summary>
    internal sealed class AppFixture : IDisposable
    {
        public readonly string Dir = TestUtil.TempDir();
        public readonly ManualClock Clock = new ManualClock(TestUtil.T0);
        public readonly RectTransform Canvas;
        public readonly ClientApp App;
        public readonly LocalGameService Service;

        public AppFixture(bool start = true)
        {
            Canvas = ClientApp.CreateCanvas("TestCanvas");
            App = ClientApp.Build(Canvas, new ClientSettings { DeviceId = "test", DisplayName = "Tester", ServerUrl = "http://localhost:1" }, Dir);
            Service = new LocalGameService(Dir, "local_test", "Tester", Clock, 42);
            App.ServiceFactory = _ => Service;
            if (start) App.Start(Service);
        }

        public void Dispose()
        {
            if (Canvas != null) UnityEngine.Object.DestroyImmediate(Canvas.gameObject);
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);
            TestUtil.DeleteDir(Dir);
        }
    }
}
