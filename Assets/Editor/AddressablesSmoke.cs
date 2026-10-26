// Headless check of the Addressables content build, which every Daggerfall Unity
// build depends on. Proves the asset pipeline works before we spend time on a
// full player build.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FantasyRealms.EditorTools
{
    public static class AddressablesSmoke
    {
        public static void Run()
        {
            var sb = new System.Text.StringBuilder();
            bool ok = true;
            try
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                    throw new Exception("AddressableAssetSettings not found");

                sb.AppendLine($"settings '{settings.name}' groups={settings.groups.Count}");
                foreach (var g in settings.groups)
                    if (g != null)
                        sb.AppendLine($"  group {g.Name}: {g.entries.Count} entries");

                AddressableAssetSettings.BuildPlayerContent(out var result);
                sb.AppendLine($"build status {result}");
                if (!string.IsNullOrEmpty(result.Error))
                {
                    ok = false;
                    sb.AppendLine("error: " + result.Error);
                }
            }
            catch (Exception e)
            {
                ok = false;
                sb.AppendLine($"FAIL {e.GetType().Name}: {e.Message}");
            }

            sb.AppendLine(ok ? "RESULT OK" : "RESULT FAIL");
            File.WriteAllText("/tmp/dfu_addressables.txt", sb.ToString());
            Debug.Log("[addressables-smoke]\n" + sb);
        }
    }
}
