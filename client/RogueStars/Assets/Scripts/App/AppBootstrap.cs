using RogueStars.Client.Services;
using UnityEngine;

namespace RogueStars.Client.App
{
    /// <summary>The only object in Main.unity: builds the whole UI from code and drives the app.</summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        public static ClientApp Current { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            var canvas = ClientApp.CreateCanvas();
            Current = ClientApp.Build(canvas, ClientSettings.Load());
            Current.Start();
        }

        private void Update()
        {
            if (Current == null) return;
            Current.Update(Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.Escape)) Current.HandleBack();
        }

        private void OnApplicationPause(bool paused)
        {
            if (Current == null) return;
            if (paused) (Current.Session.Service as LocalGameService)?.Save();
            else Current.Session.Refresh();
        }

        private void OnApplicationQuit() => (Current?.Session.Service as LocalGameService)?.Save();
    }
}
