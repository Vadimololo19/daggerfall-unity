// Smoke test for the Daggerfall data layer: does the ARENA2 install parse?
// Run headless:  Unity -batchmode -nographics -projectPath <proj> -executeMethod FantasyRealms.EditorTools.Arena2SmokeTest.Run -quit
using System;
using System.IO;
using System.Text;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using UnityEditor;
using UnityEngine;

namespace FantasyRealms.EditorTools
{
    public static class Arena2SmokeTest
    {
        public static void Run()
        {
            string arena = Environment.GetEnvironmentVariable("DFU_ARENA2");
            if (string.IsNullOrEmpty(arena))
                arena = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "",
                                     "Games/daggerfall/raw/game/ARENA2");

            var sb = new StringBuilder();
            bool ok = true;
            try
            {
                var maps = new MapsFile(Path.Combine(arena, "MAPS.BSA"), FileUsage.UseMemory, true);
                sb.AppendLine($"maps_loaded regions={maps.RegionCount}");

                long total = 0;
                for (int i = 0; i < maps.RegionCount; i++)
                {
                    var region = maps.GetRegion(i);
                    total += region.LocationCount;
                    if (i < 3)
                        sb.AppendLine($"  region[{i}] {maps.GetRegionName(i)}: {region.LocationCount} locations");
                }
                sb.AppendLine($"total_locations {total}");

                var loc = maps.GetLocation(0, 0);
                sb.AppendLine($"sample_location '{loc.Name}'");
            }
            catch (Exception e)
            {
                ok = false;
                sb.AppendLine($"FAIL {e.GetType().Name}: {e.Message}");
            }

            sb.AppendLine(ok ? "RESULT OK" : "RESULT FAIL");
            File.WriteAllText("/tmp/dfu_data_smoke.txt", sb.ToString());
            Debug.Log("[arena2-smoke]\n" + sb);
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
