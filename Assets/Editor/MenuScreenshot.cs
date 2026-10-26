// Opens the Daggerfall Unity startup scene, lets it run, and captures the screen.
// Used to prove that rendering, shaders and UI actually work on Unity 6.
//
// Entering play mode triggers a domain reload, which drops static callbacks.
// The pending flag therefore lives in SessionState and the update hook is
// re-attached from [InitializeOnLoadMethod] after every reload.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FantasyRealms.EditorTools
{
    [InitializeOnLoad]
    public static class MenuScreenshot
    {
        const string PendingKey = "fr_shot_pending";
        const string StartKey = "fr_shot_start";

        static MenuScreenshot()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/DaggerfallUnityStartup.unity");
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            Debug.Log("[shot] play mode requested");
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(PendingKey, false))
                return;
            if (!EditorApplication.isPlaying)
                return;

            double start = SessionState.GetFloat(StartKey, 0f);
            double t = EditorApplication.timeSinceStartup - start;
            int stage = SessionState.GetInt("fr_shot_stage", 0);

            if (t > 15 && stage < 1)
            {
                SessionState.SetInt("fr_shot_stage", 1);
                ScreenCapture.CaptureScreenshot("/tmp/dfu_menu_1.png");
                Debug.Log("[shot] capture #1 at " + t.ToString("F1") + "s");
            }
            if (t > 25 && stage < 2)
            {
                SessionState.SetInt("fr_shot_stage", 2);
                ScreenCapture.CaptureScreenshot("/tmp/dfu_menu_2.png");
                Debug.Log("[shot] capture #2 at " + t.ToString("F1") + "s");
            }
            if (t > 32)
            {
                SessionState.SetBool(PendingKey, false);
                File.WriteAllText("/tmp/dfu_menu_status.txt",
                    $"played {t:F1}s shots={(File.Exists("/tmp/dfu_menu_1.png") ? 1 : 0) + (File.Exists("/tmp/dfu_menu_2.png") ? 1 : 0)}");
                EditorApplication.Exit(0);
            }
        }
    }
}
