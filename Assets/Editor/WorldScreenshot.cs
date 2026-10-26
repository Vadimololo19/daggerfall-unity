// Loads the actual game scene (not the menu) and captures frames, to prove the
// world renders: streaming terrain, location blocks, billboards.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FantasyRealms.EditorTools
{
    [InitializeOnLoad]
    public static class WorldScreenshot
    {
        const string PendingKey = "fr_world_pending";
        const string StartKey = "fr_world_start";

        static WorldScreenshot()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            string scene = System.Environment.GetEnvironmentVariable("FR_SCENE");
            if (string.IsNullOrEmpty(scene))
                scene = "Assets/Scenes/DaggerfallUnityGame.unity";
            Debug.Log("[world] opening " + scene);
            EditorSceneManager.OpenScene(scene);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt("fr_world_stage", 0);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying)
                return;

            double start = SessionState.GetFloat(StartKey, 0f);
            double t = EditorApplication.timeSinceStartup - start;
            int stage = SessionState.GetInt("fr_world_stage", 0);

            if (t > 12 && stage < 1)
            {
                SessionState.SetInt("fr_world_stage", 1);
                ScreenCapture.CaptureScreenshot("/tmp/dfu_world_1.png");
                Debug.Log("[world] shot #1 at " + t.ToString("F1"));
            }
            if (t > 22 && stage < 2)
            {
                SessionState.SetInt("fr_world_stage", 2);
                ScreenCapture.CaptureScreenshot("/tmp/dfu_world_2.png");
                Debug.Log("[world] shot #2 at " + t.ToString("F1"));
            }
            if (t > 30)
            {
                SessionState.SetBool(PendingKey, false);
                File.WriteAllText("/tmp/dfu_world_status.txt", "elapsed " + t.ToString("F1"));
                EditorApplication.Exit(0);
            }
        }
    }
}
