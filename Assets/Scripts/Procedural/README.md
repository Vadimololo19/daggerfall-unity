# Procedural world generator (copied)

Do not edit these files here. The source of truth is
`unity/Assets/World/` in the main Fantasyrealms repository; changes are copied
in by `tools/sync_generator_to_fork.sh`.

The generator is plain C# (System only, no UnityEngine) so it compiles inside the
Daggerfall Unity project without adaptation. It produces heights, biomes, roads,
settlements and interiors; the adapter that turns that output into Daggerfall
Unity world data (regions, locations, RMB blocks, buildings) is ours and lives
outside this folder.
