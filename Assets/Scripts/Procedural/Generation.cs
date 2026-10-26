using System;
using System.Collections.Generic;

namespace FantasyRealms.World
{
    // =========================================================================
    // World generation: a single ordered pipeline where every later step respects
    // what earlier steps reserved. Port of src/map/overmap_gen.cpp.
    // =========================================================================

    public partial class Overmap
    {
        private struct BuildingSpec
        {
            public BuildingType Type;
            public int W, H;
            public BuildingSpec(BuildingType t, int w, int h) { Type = t; W = w; H = h; }
        }

        // Sizes are chosen so a building fits in a 7x7 street block with room to
        // spare, and every building is at least 5x5 (GenerateBuildingInterior needs that).
        private static readonly BuildingSpec[] CITY_BUILDINGS =
        {
            new BuildingSpec(BuildingType.House, 6, 5),   new BuildingSpec(BuildingType.House, 7, 6),
            new BuildingSpec(BuildingType.House, 6, 5),   new BuildingSpec(BuildingType.Townhouse, 7, 6),
            new BuildingSpec(BuildingType.Shop, 7, 5),    new BuildingSpec(BuildingType.Shop, 6, 5),
            new BuildingSpec(BuildingType.Tavern, 7, 6),  new BuildingSpec(BuildingType.Smithy, 6, 5),
            new BuildingSpec(BuildingType.Library, 7, 6), new BuildingSpec(BuildingType.Temple, 7, 6),
            new BuildingSpec(BuildingType.Warehouse, 7, 6), new BuildingSpec(BuildingType.Barracks, 7, 6),
            new BuildingSpec(BuildingType.Stable, 7, 5),  new BuildingSpec(BuildingType.Barn, 6, 5),
            new BuildingSpec(BuildingType.TownHall, 7, 6), new BuildingSpec(BuildingType.Watchtower, 5, 5),
        };

        private static readonly BuildingSpec[] VILLAGE_BUILDINGS =
        {
            new BuildingSpec(BuildingType.House, 6, 5),  new BuildingSpec(BuildingType.House, 6, 5),
            new BuildingSpec(BuildingType.House, 7, 5),  new BuildingSpec(BuildingType.Barn, 7, 5),
            new BuildingSpec(BuildingType.Shop, 6, 5),   new BuildingSpec(BuildingType.Tavern, 7, 6),
            new BuildingSpec(BuildingType.Temple, 6, 6),
        };

        private const int STREET_SPACING = 8; // city streets every 8 tiles
        private const int BLOCK = STREET_SPACING - 1; // 7x7 buildable block
        private const int IMPASSABLE = 1 << 20;

        private static bool TileIsSettlementGround(OvermapTerrain t) =>
            t == OvermapTerrain.Town || t == OvermapTerrain.Field || t == OvermapTerrain.TownCenter;

        // Ground a standalone building (mage tower) may stand on.
        private static bool TileIsWildGround(OvermapTerrain t) =>
            t == OvermapTerrain.Field || t == OvermapTerrain.Forest ||
            t == OvermapTerrain.DenseForest || t == OvermapTerrain.Tundra ||
            t == OvermapTerrain.Desert || t == OvermapTerrain.Snow;

        // A growable binary min-heap of (cost, index) pairs; ties break on the
        // smaller index, matching std::priority_queue<pair<int,int>, ..., greater>.
        private sealed class MinHeap
        {
            private int[] _c = new int[1024];
            private int[] _i = new int[1024];
            private int _n = 0;

            public int Count => _n;

            private void Ensure(int cap)
            {
                if (cap <= _c.Length) return;
                int nc = Math.Max(cap, _c.Length * 2);
                Array.Resize(ref _c, nc);
                Array.Resize(ref _i, nc);
            }

            private static bool Less(int ca, int ia, int cb, int ib) => ca < cb || (ca == cb && ia < ib);

            private void Swap(int a, int b)
            {
                int t = _c[a]; _c[a] = _c[b]; _c[b] = t;
                t = _i[a]; _i[a] = _i[b]; _i[b] = t;
            }

            public void Push(int cost, int idx)
            {
                Ensure(_n + 1);
                int p = _n++;
                _c[p] = cost; _i[p] = idx;
                while (p > 0)
                {
                    int par = (p - 1) >> 1;
                    if (Less(_c[p], _i[p], _c[par], _i[par])) { Swap(p, par); p = par; }
                    else break;
                }
            }

            public void Pop(out int cost, out int idx)
            {
                cost = _c[0]; idx = _i[0];
                _n--;
                if (_n > 0)
                {
                    _c[0] = _c[_n]; _i[0] = _i[_n];
                    int p = 0;
                    while (true)
                    {
                        int l = 2 * p + 1, r = l + 1, s = p;
                        if (l < _n && Less(_c[l], _i[l], _c[s], _i[s])) s = l;
                        if (r < _n && Less(_c[r], _i[r], _c[s], _i[s])) s = r;
                        if (s == p) break;
                        Swap(p, s); p = s;
                    }
                }
            }
        }

        // Labels every road tile with the id of its 4-connected road component.
        private static int[] LabelRoadComponents(Overmap m)
        {
            const int S = SIZE;
            var comp = new int[S * S];
            Array.Fill(comp, -1);
            var stack = new List<int>();
            int id = 0;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int start = y * S + x;
                    if (comp[start] != -1 || !m.IsRoad(m.GetTerrain(x, y))) continue;
                    stack.Clear();
                    stack.Add(start);
                    comp[start] = id;
                    int[] dxs = { 0, 1, 0, -1 }, dys = { -1, 0, 1, 0 };
                    while (stack.Count > 0)
                    {
                        int cur = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);
                        int cx = cur % S, cy = cur / S;
                        for (int i = 0; i < 4; i++)
                        {
                            int nx = cx + dxs[i], ny = cy + dys[i];
                            if (!m.InBounds(nx, ny)) continue;
                            int nk = ny * S + nx;
                            if (comp[nk] != -1 || !m.IsRoad(m.GetTerrain(nx, ny))) continue;
                            comp[nk] = id;
                            stack.Add(nk);
                        }
                    }
                    id++;
                }
            }
            return comp;
        }

        // Road component touching a settlement, or -1 when the settlement has no road.
        private static int SettlementRoadComponent(Overmap m, int[] comp, City c)
        {
            const int S = SIZE;
            int rad = c.Size / 2 + 2;
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    int x = c.X + dx, y = c.Y + dy;
                    if (!m.InBounds(x, y)) continue;
                    if (m.IsRoad(m.GetTerrain(x, y))) return comp[y * S + x];
                }
            return -1;
        }

        // ===== ENTRY POINT =====

        public void Generate()
        {
            GenerateTerrain();
            GenerateRivers();
            PlaceCities();
            PlaceVillages();
            GenerateRoads();
            PlaceDungeons();
            PlaceCamps();
            PlaceBanditCamps();
            PlaceMageTowers();
            PlaceCaravans();
            PopulateSpecialTiles();
            SpawnWildlife();
        }

        // ===== TERRAIN =====

        // Thresholds calibrated to the distribution of Noise::fractalNoise2D:
        // roughly 9% water and a few % impassable rock.
        private void GenerateTerrain()
        {
            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float elevation = noise_.FractalNoise2D(x, y, 5, 0.55f, 48.0f);
                    float moisture = noise_.FractalNoise2D(x + 1000, y + 1000, 4, 0.5f, 32.0f);
                    float temperature = noise_.FractalNoise2D(x - 2000, y + 2000, 3, 0.5f, 80.0f);

                    OvermapTerrain t;
                    if (elevation < 0.30f)
                    {
                        t = OvermapTerrain.Water; // ~9%
                    }
                    else if (elevation < 0.35f)
                    {
                        t = (moisture > 0.60f) ? OvermapTerrain.Swamp : OvermapTerrain.Field; // coast
                    }
                    else if (elevation < 0.62f)
                    {
                        if (moisture > 0.66f) t = OvermapTerrain.Forest; // ~70% of the map
                        else if (moisture < 0.30f) t = OvermapTerrain.Desert;
                        else t = OvermapTerrain.Field;
                    }
                    else if (elevation < 0.70f)
                    {
                        t = (moisture > 0.58f) ? OvermapTerrain.DenseForest : OvermapTerrain.Forest;
                    }
                    else if (elevation < 0.76f)
                    {
                        t = (temperature > 0.60f) ? OvermapTerrain.Tundra : OvermapTerrain.Mountain;
                    }
                    else
                    {
                        t = (temperature > 0.65f) ? OvermapTerrain.Snow : OvermapTerrain.Mountain;
                    }
                    data_[y * SIZE + x] = t;
                }
            }
        }

        private void GenerateRivers()
        {
            for (int i = 0; i < 10; i++)
            {
                int startX = rng_.Range(10, SIZE - 10);
                int startY = rng_.Range(10, SIZE - 10);
                if (!IsWater(startX, startY)) continue;

                int x = startX, y = startY;
                int length = rng_.Range(40, 140);
                int dir = rng_.Range(0, 3);

                for (int step = 0; step < length; step++)
                {
                    if (rng_.OneIn(3)) dir = (dir + (rng_.OneIn(2) ? 1 : 3)) % 4;
                    int nx = x, ny = y;
                    switch (dir)
                    {
                        case 0: ny--; break;
                        case 1: nx++; break;
                        case 2: ny++; break;
                        case 3: nx--; break;
                    }
                    if (!InBounds(nx, ny)) break;
                    var t = GetTerrain(nx, ny);
                    if (t == OvermapTerrain.Mountain) break; // rivers run around rock
                    if (t == OvermapTerrain.Water) { x = nx; y = ny; continue; }
                    SetTerrain(nx, ny, OvermapTerrain.Water);
                    x = nx; y = ny;
                }
            }
        }

        // ===== RESERVATION AND SITE VALIDATION =====

        private void ReserveRect(int x, int y, int w, int h, byte kind)
        {
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (InBounds(px, py)) reserved_[py * SIZE + px] = kind;
                }
        }

        // A settlement square must be dry land, free of rock and other settlements.
        private bool SiteFits(int cx, int cy, int rad, int minDist)
        {
            if (cx - rad < 2 || cy - rad < 2 || cx + rad >= SIZE - 2 || cy + rad >= SIZE - 2)
                return false;
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    var t = GetTerrain(x, y);
                    if (t == OvermapTerrain.Water || t == OvermapTerrain.Mountain ||
                        t == OvermapTerrain.DenseForest)
                        return false;
                    if (reserved_[y * SIZE + x] != 0) return false;
                }
            foreach (var c in cities_)
            {
                int dx = cx - c.X, dy = cy - c.Y;
                int need = rad + c.Size / 2 + minDist;
                if (dx * dx + dy * dy < need * need) return false;
            }
            return true;
        }

        // ===== ROAD ROUTER =====

        // Cost of routing through a tile. Existing roads are free so routes merge;
        // buildings are impassable so a road can never cut through a house.
        private int RouteCost(int x, int y)
        {
            var t = GetTerrain(x, y);
            switch (t)
            {
                case OvermapTerrain.BuildingWall:
                case OvermapTerrain.BuildingFloor:
                case OvermapTerrain.BuildingDoor:
                    return IMPASSABLE;
                case OvermapTerrain.Water: return 40; // becomes a bridge
                case OvermapTerrain.Mountain: return 30; // becomes a mountain pass
                case OvermapTerrain.DenseForest: return 7;
                case OvermapTerrain.Forest:
                case OvermapTerrain.Swamp: return 4;
                case OvermapTerrain.CityWall: return 25; // prefer a gate over the wall
                case OvermapTerrain.TownGate: return 0;
                case OvermapTerrain.Dungeon:
                case OvermapTerrain.Shrine:
                case OvermapTerrain.MageTower: return 60;
                case OvermapTerrain.BanditCamp:
                case OvermapTerrain.Tent:
                case OvermapTerrain.Campfire: return 8;
                case OvermapTerrain.RoadHorizontal:
                case OvermapTerrain.RoadVertical:
                case OvermapTerrain.StoneRoadHorizontal:
                case OvermapTerrain.StoneRoadVertical:
                case OvermapTerrain.TrailHorizontal:
                case OvermapTerrain.TrailVertical:
                case OvermapTerrain.BridgeHorizontal:
                case OvermapTerrain.BridgeVertical:
                    return 0;
                default: return 1;
            }
        }

        // Least-cost route between two points. Carves roads, bridges over water and
        // passes through rock. Never writes over a building. Returns false only if
        // no route exists at all.
        private bool CarveRoute(int x1, int y1, int x2, int y2, bool stone)
        {
            const int S = SIZE;
            int[] dist = new int[S * S];
            int[] prev = new int[S * S];
            Array.Fill(dist, int.MaxValue);
            Array.Fill(prev, -1);

            var open = new MinHeap();
            dist[y1 * S + x1] = 0;
            open.Push(0, y1 * S + x1);

            int[] dxs = { 0, 1, 0, -1 }, dys = { -1, 0, 1, 0 };
            bool found = false;
            while (open.Count > 0)
            {
                open.Pop(out int d, out int cur);
                if (d != dist[cur]) continue;
                int cx = cur % S, cy = cur / S;
                if (cx == x2 && cy == y2) { found = true; break; }
                for (int i = 0; i < 4; i++)
                {
                    int nx = cx + dxs[i], ny = cy + dys[i];
                    if (!InBounds(nx, ny)) continue;
                    int c = RouteCost(nx, ny);
                    if (c >= IMPASSABLE) continue;
                    int nd = d + c;
                    int nk = ny * S + nx;
                    if (nd < dist[nk])
                    {
                        dist[nk] = nd;
                        prev[nk] = cur;
                        open.Push(nd, nk);
                    }
                }
            }
            if (!found) return false;

            var path = new List<int>();
            for (int cur = y2 * S + x2; cur != -1; cur = prev[cur])
            {
                path.Add(cur);
                if (cur == y1 * S + x1) break;
            }
            path.Reverse();

            for (int i = 0; i < path.Count; i++)
            {
                int cx = path[i] % S, cy = path[i] / S;
                int px = (i > 0) ? path[i - 1] % S : cx;
                int py = (i > 0) ? path[i - 1] / S : cy;
                bool horizontal = Math.Abs(cx - px) >= Math.Abs(cy - py);

                var t = GetTerrain(cx, cy);
                if (t == OvermapTerrain.BuildingWall || t == OvermapTerrain.BuildingFloor ||
                    t == OvermapTerrain.BuildingDoor)
                    continue; // never touch a building, even if the path grazes it
                if (t == OvermapTerrain.Water)
                {
                    SetTerrain(cx, cy, horizontal ? OvermapTerrain.BridgeHorizontal
                                                  : OvermapTerrain.BridgeVertical);
                }
                else if (t == OvermapTerrain.Mountain)
                {
                    SetTerrain(cx, cy, stone ? OvermapTerrain.StoneRoadHorizontal
                                             : OvermapTerrain.TrailHorizontal);
                }
                else if (IsRoad(t))
                {
                    // Keep the better road: a stone road is not downgraded by a trail.
                    RoadType existing = GetRoadType(t);
                    RoadType wanted = stone ? RoadType.StoneRoad : RoadType.Road;
                    if (existing >= wanted) continue;
                    SetTerrain(cx, cy, stone
                        ? (horizontal ? OvermapTerrain.StoneRoadHorizontal : OvermapTerrain.StoneRoadVertical)
                        : (horizontal ? OvermapTerrain.RoadHorizontal : OvermapTerrain.RoadVertical));
                }
                else
                {
                    SetTerrain(cx, cy, stone
                        ? (horizontal ? OvermapTerrain.StoneRoadHorizontal : OvermapTerrain.StoneRoadVertical)
                        : (horizontal ? OvermapTerrain.TrailHorizontal : OvermapTerrain.TrailVertical));
                }
            }
            return true;
        }

        private void ConnectFeature(int x, int y, bool stone)
        {
            int nearest = FindNearestSettlement(x, y);
            if (nearest < 0) return;
            CarveRoute(x, y, cities_[nearest].X, cities_[nearest].Y, stone);
        }

        // Roads are one tile wide, so nothing impassable may ever be placed on or
        // next to one: that would cut the network in two.
        private bool AreaHasRoad(int cx, int cy, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (InBounds(x, y) && IsRoad(GetTerrain(x, y))) return true;
                }
            return false;
        }

        // ===== SETTLEMENT LAYOUT =====

        private void LayStreetGrid(int cx, int cy, int rad, bool city)
        {
            for (int dy = -rad + 1; dy < rad; dy++)
            {
                for (int dx = -rad + 1; dx < rad; dx++)
                {
                    var t = GetTerrain(cx + dx, cy + dy);
                    // Streets are laid on the settlement's ground: Town for cities,
                    // Field for villages.
                    if (t != OvermapTerrain.Town && t != OvermapTerrain.TownCenter &&
                        t != OvermapTerrain.Field)
                        continue;
                    bool vertical = (dx % STREET_SPACING == 0);
                    bool horizontal = (dy % STREET_SPACING == 0);
                    if (!vertical && !horizontal) continue;
                    bool alongX = (horizontal && !vertical);
                    if (city)
                    {
                        SetTerrain(cx + dx, cy + dy, vertical
                            ? OvermapTerrain.StoneRoadVertical
                            : (alongX ? OvermapTerrain.StoneRoadHorizontal : OvermapTerrain.StoneRoadVertical));
                    }
                    else
                    {
                        SetTerrain(cx + dx, cy + dy, vertical
                            ? OvermapTerrain.RoadVertical
                            : (alongX ? OvermapTerrain.RoadHorizontal : OvermapTerrain.RoadVertical));
                    }
                }
            }
        }

        private int TryPlaceSettlementBuilding(int cx, int cy, int bw, int bh, BuildingType bt,
                                               bool wildGround = false)
        {
            if (bw < 5 || bh < 5) return -1;
            int ox = cx - bw / 2, oy = cy - bh / 2;
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    int wx = ox + x, wy = oy + y;
                    if (!InBounds(wx, wy)) return -1;
                    var t = GetTerrain(wx, wy);
                    if (wildGround ? !TileIsWildGround(t) : !TileIsSettlementGround(t)) return -1;
                    if (tileBuildingMap_.ContainsKey((wy << 9) | wx)) return -1; // already occupied
                }

            int bid = NextBuildingId();
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                    tileBuildingMap_[((oy + y) << 9) | (ox + x)] = bid;

            var bld = new Building();
            bld.Id = bid;
            bld.X = cx; bld.Y = cy;
            bld.Width = bw; bld.Height = bh;
            bld.Type = bt;
            bld.Name = BuildingName(bt);
            bld.EntranceOutsideX = cx;
            bld.EntranceOutsideY = oy + bh - 1;
            bld.EntranceInsideX = bw / 2;
            bld.EntranceInsideY = bh - 2;
            bld.NumFloors = 1;
            if (bt == BuildingType.Tavern || bt == BuildingType.Temple ||
                bt == BuildingType.TownHall || bt == BuildingType.Library)
                bld.NumFloors = 2;
            else if (bt == BuildingType.Townhouse || bt == BuildingType.Warehouse)
                bld.NumFloors = rng_.OneIn(2) ? 2 : 1;

            buildings_.Add(bld);
            PlaceBuilding(cx, cy, bw, bh, bt);
            GenerateBuildingInterior(bld);
            return bid;
        }

        private static string BuildingName(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.House: return "House";
                case BuildingType.Townhouse: return "Townhouse";
                case BuildingType.Shop: return "Shop";
                case BuildingType.Tavern: return "Tavern";
                case BuildingType.Temple: return "Temple";
                case BuildingType.Smithy: return "Smithy";
                case BuildingType.Warehouse: return "Warehouse";
                case BuildingType.TownHall: return "Town Hall";
                case BuildingType.Watchtower: return "Watchtower";
                case BuildingType.Barn: return "Barn";
                case BuildingType.Well: return "Well";
                case BuildingType.Stable: return "Stable";
                case BuildingType.Library: return "Library";
                case BuildingType.Barracks: return "Barracks";
                default: return "Building";
            }
        }

        private void PopulateSettlementBuilding(int bid, bool isCity)
        {
            Building bld = GetBuilding(bid);
            if (bld.Id < 0) return;
            int bx = bld.X, by = bld.Y;
            int spawnX = bx, spawnY = by;
            FindInteriorSpawn(bld, 0, ref spawnX, ref spawnY);

            bool isShop = (bld.Type == BuildingType.Shop || bld.Type == BuildingType.Tavern ||
                           bld.Type == BuildingType.Smithy);

            if (bld.Type == BuildingType.Barn)
            {
                for (int i = 0; i < rng_.Range(1, 3); i++)
                {
                    int sx = bx, sy = by;
                    if (!FindInteriorSpawn(bld, 0, ref sx, ref sy)) break;
                    var animal = new Entity();
                    animal.X = sx; animal.Y = sy;
                    animal.Symbol = 'm'; animal.Color = 0xaa8866;
                    animal.Name = "Cow"; animal.Title = "Farm Animal";
                    animal.Hp = 15; animal.MaxHp = 15;
                    animal.Floor = 0; animal.BuildingId = bid;
                    animal.Behavior = EntityBehavior.Idle;
                    entities_.Add(animal);
                    bld.EntityIndices.Add(entities_.Count - 1);
                }
                return;
            }

            if (!FindInteriorSpawn(bld, 0, ref spawnX, ref spawnY)) return;

            if (isShop)
            {
                string[] cityShops = { "Smithy", "Tailor", "Alchemist", "General Store",
                                       "Weaponry", "Armoury", "Baker", "Jeweler" };
                string shop = isCity ? cityShops[rng_.Range(0, 7)]
                                     : (rng_.OneIn(2) ? "General Store" : "Baker");
                var npc = SpawnNPC(spawnX, spawnY, '$', 0xffdd44,
                                   RandomName() + " " + RandomLastName(), shop,
                                   "Welcome! Take a look at my wares.");
                string s = shop;
                if (s == "Smithy")
                {
                    npc.ShopItems = new List<string> { "short_sword", "spear", "mace", "shield", "iron_helmet", "leather_armor", "bread" };
                    npc.ShopPrices = new List<int> { 15, 25, 50, 25, 35, 30, 2 };
                }
                else if (s == "Tailor")
                {
                    npc.ShopItems = new List<string> { "cloak", "leather_boots", "leather_gloves", "leather_helmet", "bread" };
                    npc.ShopPrices = new List<int> { 10, 8, 6, 12, 2 };
                }
                else if (s == "Alchemist")
                {
                    npc.ShopItems = new List<string> { "health_potion", "mana_potion", "stamina_potion", "antidote", "herb", "herb" };
                    npc.ShopPrices = new List<int> { 15, 20, 10, 10, 1, 1 };
                }
                else if (s == "Weaponry")
                {
                    npc.ShopItems = new List<string> { "dagger", "short_sword", "long_sword", "bow", "arrow", "bolt" };
                    npc.ShopPrices = new List<int> { 5, 15, 40, 30, 2, 3 };
                }
                else if (s == "Armoury")
                {
                    npc.ShopItems = new List<string> { "chainmail", "iron_helmet", "iron_boots", "shield", "cloak" };
                    npc.ShopPrices = new List<int> { 80, 35, 25, 25, 10 };
                }
                else if (s == "Baker")
                {
                    npc.ShopItems = new List<string> { "bread", "bread", "water_flask" };
                    npc.ShopPrices = new List<int> { 2, 2, 5 };
                }
                else if (s == "Jeweler")
                {
                    npc.ShopItems = new List<string> { "gold_coin", "magic_crystal", "gold_coin" };
                    npc.ShopPrices = new List<int> { 20, 100, 10 };
                }
                else
                {
                    npc.ShopItems = new List<string> { "bread", "water_flask", "torch", "rope", "herb", "stamina_potion" };
                    npc.ShopPrices = new List<int> { 2, 5, 3, 6, 1, 10 };
                }
                npc.ActionDesc = "Running the " + s;
                npc.Behavior = EntityBehavior.Work;
                npc.BuildingId = bid;
                npc.Floor = 0;
                bld.EntityIndices.Add(entities_.Count - 1);
                AddItem(bx + 1, by, "gold_coin", rng_.Range(5, 15), bid, 0);
                return;
            }

            string[] cityRoles = { "Resident", "Artisan", "Scholar", "Servant", "Apprentice", "Innkeeper" };
            string[] villageRoles = { "Farmer", "Woodcutter", "Herder", "Baker", "Miller", "Fisher", "Trapper", "Herbalist" };
            string role = isCity ? cityRoles[rng_.Range(0, 5)] : villageRoles[rng_.Range(0, 7)];

            var npc2 = SpawnNPC(spawnX, spawnY, '@', 0xccaa77,
                                RandomName() + " " + RandomLastName(), role,
                                "Good day, traveller.");
            npc2.ActionDesc = "Going about their business";
            npc2.Behavior = EntityBehavior.Wander;
            npc2.BuildingId = bid;
            npc2.Floor = 0;
            npc2.HomeX = bx; npc2.HomeY = by;
            npc2.PatrolX = bx; npc2.PatrolY = by;
            bld.EntityIndices.Add(entities_.Count - 1);

            // Occasionally a second occupant (family / colleague).
            if (rng_.OneIn(2))
            {
                int fx = bx, fy = by;
                if (FindInteriorSpawn(bld, 0, ref fx, ref fy))
                {
                    var fam = SpawnNPC(fx, fy, '@', 0xccaa77,
                                       RandomName() + " " + RandomLastName(), role,
                                       "Hello.");
                    fam.ActionDesc = "Helping out";
                    fam.Behavior = EntityBehavior.Wander;
                    fam.BuildingId = bid;
                    fam.Floor = 0;
                    fam.HomeX = bx; fam.HomeY = by;
                    fam.PatrolX = bx; fam.PatrolY = by;
                    bld.EntityIndices.Add(entities_.Count - 1);
                }
            }
            if (rng_.OneIn(3)) AddItem(bx + 1, by, "gold_coin", rng_.Range(1, 6), bid, 0);
        }

        // ===== CITIES AND VILLAGES =====

        private void PlaceCities()
        {
            int numCities = rng_.Range(2, 4);
            int attempts = 0;
            while (CountCities() < numCities && attempts < 400)
            {
                attempts++;
                int rad = rng_.Range(24, 34);
                int cx = rng_.Range(rad + 24, SIZE - rad - 24);
                int cy = rng_.Range(rad + 24, SIZE - rad - 24);
                if (!SiteFits(cx, cy, rad + 2, 30)) continue;

                var city = new City();
                city.X = cx; city.Y = cy;
                city.Size = rad * 2;
                city.Name = CITY_NAMES[rng_.Range(0, CITY_NAMES.Length - 1)];
                city.IsCity = true;
                cities_.Add(city);
                ReserveRect(cx - rad - 2, cy - rad - 2, 2 * rad + 5, 2 * rad + 5, 1);

                // Ground, plaza, wall and gates.
                PlaceRect(cx - rad, cy - rad, 2 * rad + 1, 2 * rad + 1, OvermapTerrain.Town);
                PlaceRect(cx - 3, cy - 3, 7, 7, OvermapTerrain.TownCenter);
                PlaceHollowRect(cx - rad, cy - rad, 2 * rad + 1, 2 * rad + 1,
                                OvermapTerrain.CityWall, OvermapTerrain.Town);
                SetTerrain(cx, cy - rad, OvermapTerrain.TownGate);
                SetTerrain(cx, cy + rad, OvermapTerrain.TownGate);
                SetTerrain(cx - rad, cy, OvermapTerrain.TownGate);
                SetTerrain(cx + rad, cy, OvermapTerrain.TownGate);

                // Streets first, then buildings inside the blocks they create.
                LayStreetGrid(cx, cy, rad, true);

                int numSpecs = CITY_BUILDINGS.Length;
                // Blocks span [k*8+1 .. k*8+7] and their centre is k*8+4.
                var centres = new List<int>();
                for (int o = -rad + 1; o + BLOCK <= rad; o++)
                {
                    int m = ((o % STREET_SPACING) + STREET_SPACING) % STREET_SPACING;
                    if (m == 1) centres.Add(o + BLOCK / 2);
                }
                foreach (int by in centres)
                {
                    foreach (int bx in centres)
                    {
                        // Stay clear of the central plaza.
                        if (Math.Abs(bx) <= 4 && Math.Abs(by) <= 4) continue;
                        for (int attempt = 0; attempt < 4; attempt++)
                        {
                            var spec = CITY_BUILDINGS[rng_.Range(0, numSpecs - 1)];
                            int w = Math.Min(spec.W, BLOCK), h = Math.Min(spec.H, BLOCK);
                            int bid = TryPlaceSettlementBuilding(cx + bx, cy + by, w, h, spec.Type);
                            if (bid > 0) { PopulateSettlementBuilding(bid, true); break; }
                        }
                    }
                }

                // Mayor and advisors in the plaza.
                var mayor = SpawnNPC(cx, cy, 'M', 0xffcc00, RandomName() + " " + RandomLastName(),
                                     "Mayor of " + city.Name,
                                     "Welcome to " + city.Name + "! A fine city, isn't it?");
                mayor.ActionDesc = "Administering " + city.Name;
                mayor.Behavior = EntityBehavior.Work;
                mayor.HomeX = cx; mayor.HomeY = cy;

                // Guards at the gates and on patrol.
                for (int g = 0; g < 4; g++)
                {
                    int gx = cx, gy = cy;
                    if (g == 0) gy = cy - rad + 1;
                    else if (g == 1) gy = cy + rad - 1;
                    else if (g == 2) gx = cx - rad + 1;
                    else gx = cx + rad - 1;
                    if (!IsWalkable(gx, gy)) continue;
                    if (NpcExistsAt(gx, gy)) continue;
                    var guard = SpawnNPC(gx, gy, 'g', 0x888866, RandomName() + " " + RandomLastName(),
                                         "Gate Guard", "Stop! State your business.");
                    guard.ActionDesc = "Standing watch at the gate";
                    guard.Behavior = EntityBehavior.Patrol;
                    guard.Hp = 25; guard.MaxHp = 25;
                    guard.DamageDice = 1; guard.DamageSides = 8; guard.Armor = 4; guard.Dodge = 1;
                    guard.HomeX = gx; guard.HomeY = gy;
                    guard.PatrolX = gx; guard.PatrolY = gy;
                    guard.BuildingId = 0;
                }
                for (int g = 0; g < 4; g++)
                {
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        int gx = cx + rng_.Range(-rad / 2, rad / 2);
                        int gy = cy + rng_.Range(-rad / 2, rad / 2);
                        if (!IsWalkable(gx, gy)) continue;
                        if (NpcExistsAt(gx, gy)) continue;
                        var guard = SpawnNPC(gx, gy, 'g', 0x888866, RandomName() + " " + RandomLastName(),
                                             "Patrol Guard", "Keeping the peace.");
                        guard.ActionDesc = "Patrolling the streets";
                        guard.Behavior = EntityBehavior.Patrol;
                        guard.Hp = 22; guard.MaxHp = 22;
                        guard.DamageDice = 1; guard.DamageSides = 6; guard.Armor = 3;
                        guard.HomeX = gx; guard.HomeY = gy;
                        guard.PatrolX = cx + rng_.Range(-rad / 2, rad / 2);
                        guard.PatrolY = cy + rng_.Range(-rad / 2, rad / 2);
                        guard.BuildingId = 0;
                        break;
                    }
                }

                // Market goods on the streets.
                for (int si = 0; si < rng_.Range(3, 7); si++)
                {
                    int sx = cx + rng_.Range(-rad + 2, rad - 2);
                    int sy = cy + rng_.Range(-rad + 2, rad - 2);
                    if (!InBounds(sx, sy) || !IsRoad(GetTerrain(sx, sy))) continue;
                    AddItem(sx, sy, rng_.OneIn(2) ? "bread" : "herb", rng_.Range(1, 3));
                    if (rng_.OneIn(3)) AddItem(sx, sy, "gold_coin", rng_.Range(1, 5));
                }
                // Citizens on the streets and the plaza.
                int numCitizens = rng_.Range(6, 12);
                for (int i = 0; i < numCitizens; i++)
                {
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        int rx = cx + rng_.Range(-rad + 2, rad - 2);
                        int ry = cy + rng_.Range(-rad + 2, rad - 2);
                        if (!InBounds(rx, ry)) continue;
                        var t = GetTerrain(rx, ry);
                        if (!IsRoad(t) && t != OvermapTerrain.TownCenter) continue;
                        if (!IsWalkable(rx, ry)) continue;
                        if (NpcExistsAt(rx, ry)) continue;
                        var npc = SpawnNPC(rx, ry, '@', 0xcc88ff, RandomName() + " " + RandomLastName(),
                                           "Citizen", "Hello there.");
                        npc.ActionDesc = (t == OvermapTerrain.TownCenter)
                            ? "Walking through the square" : "Going about their day";
                        npc.Behavior = EntityBehavior.Wander;
                        npc.HomeX = rx; npc.HomeY = ry;
                        npc.PatrolX = cx + rng_.Range(-rad / 2, rad / 2);
                        npc.PatrolY = cy + rng_.Range(-rad / 2, rad / 2);
                        npc.BuildingId = 0;
                        break;
                    }
                }

                AddItem(cx + 1, cy, "gold_coin", rng_.Range(5, 15));
                AddItem(cx - 1, cy, "bread", 3);
            }
        }

        private int CountCities()
        {
            int n = 0;
            foreach (var c in cities_) if (c.IsCity) n++;
            return n;
        }

        private void PlaceVillages()
        {
            int numVillages = rng_.Range(8, 14);
            int attempts = 0;
            int placed = 0;
            while (placed < numVillages && attempts < 600)
            {
                attempts++;
                int rad = rng_.Range(10, 16);
                int cx = rng_.Range(rad + 14, SIZE - rad - 14);
                int cy = rng_.Range(rad + 14, SIZE - rad - 14);
                if (!SiteFits(cx, cy, rad + 2, 12)) continue;

                var village = new City();
                village.X = cx; village.Y = cy;
                village.Size = rad * 2;
                village.Name = VILLAGE_NAMES[rng_.Range(0, VILLAGE_NAMES.Length - 1)];
                village.IsCity = false;
                cities_.Add(village);
                ReserveRect(cx - rad - 1, cy - rad - 1, 2 * rad + 3, 2 * rad + 3, 1);
                placed++;

                // Village ground is a field clearing; the cross roads come first.
                for (int dy = -rad; dy <= rad; dy++)
                    for (int dx = -rad; dx <= rad; dx++)
                        if (InBounds(cx + dx, cy + dy) && !IsWater(cx + dx, cy + dy))
                            SetTerrain(cx + dx, cy + dy, OvermapTerrain.Field);
                LayStreetGrid(cx, cy, rad, false);
                // Well at the crossroads.
                SetTerrain(cx + 2, cy + 2, OvermapTerrain.TownCenter);

                int numSpecs = VILLAGE_BUILDINGS.Length;
                var centres = new List<int>();
                for (int o = -rad + 1; o + BLOCK <= rad; o++)
                {
                    int m = ((o % STREET_SPACING) + STREET_SPACING) % STREET_SPACING;
                    if (m == 1) centres.Add(o + BLOCK / 2);
                }
                foreach (int by in centres)
                {
                    foreach (int bx in centres)
                    {
                        if (Math.Abs(bx) <= 2 && Math.Abs(by) <= 2) continue; // crossroads
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            var spec = VILLAGE_BUILDINGS[rng_.Range(0, numSpecs - 1)];
                            int w = Math.Min(spec.W, BLOCK), h = Math.Min(spec.H, BLOCK);
                            int bid = TryPlaceSettlementBuilding(cx + bx, cy + by, w, h, spec.Type);
                            if (bid > 0) { PopulateSettlementBuilding(bid, false); break; }
                        }
                    }
                }

                var elder = SpawnNPC(cx, cy, 'E', 0xffaa44, RandomName() + " " + RandomLastName(),
                                     "Elder of " + village.Name,
                                     "Welcome to " + village.Name + ". Rest a while.");
                elder.ActionDesc = "Overseeing " + village.Name;
                elder.Behavior = EntityBehavior.Work;
                elder.HomeX = cx; elder.HomeY = cy;

                int numMilitia = rng_.Range(2, 4);
                for (int i = 0; i < numMilitia; i++)
                {
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        int mx = cx + rng_.Range(-rad + 1, rad - 1);
                        int my = cy + rng_.Range(-rad + 1, rad - 1);
                        if (!InBounds(mx, my) || !IsWalkable(mx, my)) continue;
                        if (NpcExistsAt(mx, my)) continue;
                        var mil = SpawnNPC(mx, my, 'm', 0x88aa66, RandomName() + " " + RandomLastName(),
                                           "Militia", "Keeping the peace.");
                        mil.ActionDesc = "Patrolling the village";
                        mil.Hp = 12; mil.MaxHp = 12;
                        mil.DamageDice = 1; mil.DamageSides = 4; mil.Armor = 1; mil.Dodge = 1;
                        mil.Behavior = EntityBehavior.Patrol;
                        mil.HomeX = mx; mil.HomeY = my;
                        mil.PatrolX = cx + rng_.Range(-rad, rad);
                        mil.PatrolY = cy + rng_.Range(-rad, rad);
                        mil.BuildingId = 0;
                        break;
                    }
                }
                AddItem(cx + 1, cy, "gold_coin", rng_.Range(3, 10));
                AddItem(cx - 1, cy, "bread", 2);
            }
        }

        // ===== ROADS =====

        private void GenerateRoads()
        {
            if (cities_.Count < 2) return;

            var cityIdx = new List<int>();
            var villageIdx = new List<int>();
            for (int i = 0; i < cities_.Count; i++)
            {
                if (cities_[i].IsCity) cityIdx.Add(i);
                else villageIdx.Add(i);
            }

            // Cities form a ring, plus one shortcut, so the trunk network is a loop.
            for (int i = 0; i + 1 < cityIdx.Count; i++)
                CarveRoute(cities_[cityIdx[i]].X, cities_[cityIdx[i]].Y,
                           cities_[cityIdx[i + 1]].X, cities_[cityIdx[i + 1]].Y, true);
            if (cityIdx.Count > 2)
                CarveRoute(cities_[cityIdx[0]].X, cities_[cityIdx[0]].Y,
                           cities_[cityIdx[cityIdx.Count - 1]].X,
                           cities_[cityIdx[cityIdx.Count - 1]].Y, true);

            // Every village is linked to the nearest city.
            foreach (int vi in villageIdx)
            {
                int best = -1, bestD = int.MaxValue;
                for (int i = 0; i < cities_.Count; i++)
                {
                    if (i == vi) continue;
                    if (!cities_[i].IsCity) continue;
                    int dx = cities_[i].X - cities_[vi].X;
                    int dy = cities_[i].Y - cities_[vi].Y;
                    int d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) // no cities at all: fall back to the nearest settlement
                {
                    for (int i = 0; i < cities_.Count; i++)
                    {
                        if (i == vi) continue;
                        int dx = cities_[i].X - cities_[vi].X;
                        int dy = cities_[i].Y - cities_[vi].Y;
                        int d = dx * dx + dy * dy;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                }
                if (best < 0) continue;
                CarveRoute(cities_[vi].X, cities_[vi].Y, cities_[best].X, cities_[best].Y,
                           cities_[best].IsCity && bestD < 90 * 90);
            }

            // Self-healing pass: if any settlement still sits in its own small road
            // component, link it to the largest one. This makes "every settlement is
            // reachable over roads" a property of the generator, not of luck.
            for (int pass = 0; pass < 4; pass++)
            {
                int[] comp = LabelRoadComponents(this);
                var sizes = new SortedDictionary<int, int>();
                foreach (int id in comp)
                    if (id >= 0) sizes[id] = sizes.TryGetValue(id, out int v) ? v + 1 : 1;
                int best = -1, bestSize = 0;
                foreach (var kv in sizes)
                    if (kv.Value > bestSize) { bestSize = kv.Value; best = kv.Key; }
                if (best < 0) break;

                int anchor = -1;
                var mine = new int[cities_.Count];
                for (int i = 0; i < cities_.Count; i++)
                    mine[i] = SettlementRoadComponent(this, comp, cities_[i]);
                for (int i = 0; i < cities_.Count; i++)
                    if (mine[i] == best) { anchor = i; break; }
                if (anchor < 0) break;

                int fixedCount = 0;
                for (int i = 0; i < cities_.Count; i++)
                {
                    if (i == anchor || mine[i] == best) continue;
                    CarveRoute(cities_[i].X, cities_[i].Y, cities_[anchor].X, cities_[anchor].Y, false);
                    fixedCount++;
                }
                if (fixedCount == 0) break;
            }
        }

        // ===== DUNGEONS, CAMPS, TOWERS =====

        private void PlaceDungeons()
        {
            int numDungeons = rng_.Range(6, 10);
            int attempts = 0;
            while (dungeons_.Count < numDungeons && attempts < 400)
            {
                attempts++;
                int dx = rng_.Range(16, SIZE - 16);
                int dy = rng_.Range(16, SIZE - 16);
                var t = GetTerrain(dx, dy);
                if (t != OvermapTerrain.Forest && t != OvermapTerrain.DenseForest &&
                    t != OvermapTerrain.Swamp && t != OvermapTerrain.Field &&
                    t != OvermapTerrain.Tundra && t != OvermapTerrain.Desert)
                    continue;
                if (IsRoad(t) || reserved_[dy * SIZE + dx] != 0) continue;
                if (AreaHasRoad(dx, dy, 3)) continue;

                bool tooClose = false;
                foreach (var d in dungeons_)
                {
                    int ddx = dx - d.X, ddy = dy - d.Y;
                    if (ddx * ddx + ddy * ddy < 14 * 14) { tooClose = true; break; }
                }
                if (tooClose) continue;
                foreach (var c in cities_)
                {
                    int ddx = dx - c.X, ddy = dy - c.Y;
                    int need = c.Size / 2 + 10;
                    if (ddx * ddx + ddy * ddy < need * need) { tooClose = true; break; }
                }
                if (tooClose) continue;

                var dungeon = new Dungeon();
                dungeon.X = dx; dungeon.Y = dy;
                dungeon.Depth = rng_.Range(3, 10);
                dungeon.Type = rng_.OneIn(3) ? OvermapTerrain.Shrine : OvermapTerrain.Dungeon;
                dungeons_.Add(dungeon);

                // A 5x5 structure with a gate corridor leading south out of the ring.
                for (int oy = -2; oy <= 2; oy++)
                    for (int ox = -2; ox <= 2; ox++)
                    {
                        if (!InBounds(dx + ox, dy + oy)) continue;
                        if (ox == 0 && oy >= 0) SetTerrain(dx + ox, dy + oy, OvermapTerrain.TownGate);
                        else SetTerrain(dx + ox, dy + oy, dungeon.Type);
                    }
                ReserveRect(dx - 2, dy - 2, 5, 5, 2);

                ConnectFeature(dx, dy + 2, false); // start at the gate, not inside the ring

                var mobs = new[]
                {
                    (s: 'g', c: 0x44cc44u, n: "Goblin", hp: 8, dmg: 3, arm: 0, dodge: 1),
                    (s: 'r', c: 0xcc4444u, n: "Giant Rat", hp: 5, dmg: 2, arm: 0, dodge: 2),
                    (s: 's', c: 0x888888u, n: "Skeleton", hp: 12, dmg: 4, arm: 2, dodge: 0),
                    (s: 'z', c: 0x886644u, n: "Zombie", hp: 15, dmg: 3, arm: 1, dodge: 0),
                    (s: 'w', c: 0x444488u, n: "Wraith", hp: 10, dmg: 5, arm: 1, dodge: 2),
                };
                int num = rng_.Range(2, 5);
                for (int i = 0; i < num; i++)
                {
                    for (int a = 0; a < 12; a++)
                    {
                        int mx = dx + rng_.Range(-4, 4);
                        int my = dy + rng_.Range(-4, 4);
                        if (!InBounds(mx, my) || !IsWalkable(mx, my)) continue;
                        if (NpcExistsAt(mx, my, -1, 0)) continue;
                        var m = mobs[rng_.Range(0, 4)];
                        var e = new Entity();
                        e.X = mx; e.Y = my; e.Symbol = m.s; e.Color = m.c; e.Name = m.n;
                        e.Hostile = true; e.Hp = m.hp; e.MaxHp = m.hp;
                        e.DamageDice = 1; e.DamageSides = m.dmg;
                        e.Armor = m.arm; e.Dodge = m.dodge;
                        e.Title = "Monster";
                        entities_.Add(e);
                        break;
                    }
                }
                AddItem(dx, dy, dungeon.Type == OvermapTerrain.Shrine ? "magic_crystal" : "dungeon_loot");
                AddItem(dx + 1, dy, "gold_coin", rng_.Range(3, 10));
            }
        }

        private void PlaceCamps()
        {
            int num = rng_.Range(5, 9);
            for (int i = 0; i < num; i++)
            {
                for (int a = 0; a < 60; a++)
                {
                    int cx = rng_.Range(16, SIZE - 16);
                    int cy = rng_.Range(16, SIZE - 16);
                    var t = GetTerrain(cx, cy);
                    if (t != OvermapTerrain.Field && t != OvermapTerrain.Forest) continue;
                    if (IsRoad(t) || reserved_[cy * SIZE + cx] != 0) continue;
                    if (AreaHasRoad(cx, cy, 5)) continue;
                    bool nearStuff = false;
                    foreach (var c in cities_)
                    {
                        int dx = c.X - cx, dy = c.Y - cy;
                        if (dx * dx + dy * dy < 16 * 16) { nearStuff = true; break; }
                    }
                    if (nearStuff) continue;

                    SetTerrain(cx, cy, OvermapTerrain.Campfire);
                    for (int ti = 0; ti < rng_.Range(2, 4); ti++)
                    {
                        int tx = cx + rng_.Range(-3, 3);
                        int ty = cy + rng_.Range(-3, 3);
                        if (InBounds(tx, ty) && IsWalkable(tx, ty) && GetTerrain(tx, ty) != OvermapTerrain.Campfire)
                            SetTerrain(tx, ty, OvermapTerrain.Tent);
                    }
                    ReserveRect(cx - 5, cy - 5, 11, 11, 2);

                    for (int ni = 0; ni < rng_.Range(2, 4); ni++)
                    {
                        for (int a2 = 0; a2 < 8; a2++)
                        {
                            int nx = cx + rng_.Range(-2, 2);
                            int ny = cy + rng_.Range(-2, 2);
                            if (!InBounds(nx, ny) || !IsWalkable(nx, ny) || NpcExistsAt(nx, ny)) continue;
                            var npc = SpawnNPC(nx, ny, '@', 0xaa8866, RandomName() + " " + RandomLastName(),
                                               "Camper", "We're just passing through.");
                            npc.ActionDesc = "Sitting by the campfire";
                            npc.Behavior = EntityBehavior.Wander;
                            npc.HomeX = cx; npc.HomeY = cy;
                            npc.PatrolX = cx; npc.PatrolY = cy;
                            break;
                        }
                    }
                    if (rng_.OneIn(3))
                    {
                        for (int a2 = 0; a2 < 8; a2++)
                        {
                            int tx = cx + rng_.Range(-2, 2);
                            int ty = cy + rng_.Range(-2, 2);
                            if (!InBounds(tx, ty) || !IsWalkable(tx, ty) || NpcExistsAt(tx, ty)) continue;
                            var trader = SpawnNPC(tx, ty, '$', 0xffdd44, RandomName() + " " + RandomLastName(),
                                                  "Traveling Merchant", "Care to see my wares?");
                            trader.ActionDesc = "Arranging goods for sale";
                            trader.ShopItems = new List<string> { "bread", "herb", "torch", "rope", "health_potion", "stamina_potion" };
                            trader.ShopPrices = new List<int> { 3, 2, 4, 6, 18, 10 };
                            trader.Behavior = EntityBehavior.Idle;
                            trader.HomeX = tx; trader.HomeY = ty;
                            break;
                        }
                    }
                    AddItem(cx + 1, cy, "wood", 3);
                    ConnectFeature(cx, cy, false);
                    break;
                }
            }
        }

        private void PlaceBanditCamps()
        {
            int num = rng_.Range(4, 8);
            for (int i = 0; i < num; i++)
            {
                for (int a = 0; a < 60; a++)
                {
                    int cx = rng_.Range(20, SIZE - 20);
                    int cy = rng_.Range(20, SIZE - 20);
                    var t = GetTerrain(cx, cy);
                    if (t != OvermapTerrain.Forest && t != OvermapTerrain.DenseForest) continue;
                    if (IsRoad(t) || reserved_[cy * SIZE + cx] != 0) continue;
                    if (AreaHasRoad(cx, cy, 5)) continue;
                    bool nearStuff = false;
                    foreach (var c in cities_)
                    {
                        int dx = c.X - cx, dy = c.Y - cy;
                        if (dx * dx + dy * dy < 20 * 20) { nearStuff = true; break; }
                    }
                    if (nearStuff) continue;

                    SetTerrain(cx, cy, OvermapTerrain.BanditCamp);
                    for (int ti = 0; ti < rng_.Range(2, 4); ti++)
                    {
                        int tx = cx + rng_.Range(-3, 3);
                        int ty = cy + rng_.Range(-3, 3);
                        if (InBounds(tx, ty) && IsWalkable(tx, ty) && GetTerrain(tx, ty) != OvermapTerrain.BanditCamp)
                            SetTerrain(tx, ty, OvermapTerrain.Tent);
                    }
                    ReserveRect(cx - 5, cy - 5, 11, 11, 2);

                    for (int bi = 0; bi < rng_.Range(2, 4); bi++)
                    {
                        for (int a2 = 0; a2 < 8; a2++)
                        {
                            int bx = cx + rng_.Range(-2, 2);
                            int by = cy + rng_.Range(-2, 2);
                            if (!InBounds(bx, by) || !IsWalkable(bx, by) || NpcExistsAt(bx, by)) continue;
                            var e = new Entity();
                            e.X = bx; e.Y = by; e.Symbol = 'b'; e.Color = 0x884422;
                            e.Name = RandomName() + " " + RandomLastName();
                            e.Title = "Bandit";
                            e.ActionDesc = "Lurking around the camp";
                            e.Hostile = true; e.Hp = 12; e.MaxHp = 12;
                            e.DamageDice = 1; e.DamageSides = 6; e.Armor = 1; e.Dodge = 1;
                            e.Behavior = EntityBehavior.Wander;
                            e.HomeX = cx; e.HomeY = cy;
                            e.PatrolX = cx; e.PatrolY = cy;
                            entities_.Add(e);
                            break;
                        }
                    }
                    if (rng_.OneIn(3))
                    {
                        for (int a2 = 0; a2 < 8; a2++)
                        {
                            int lx = cx + rng_.Range(-1, 1);
                            int ly = cy + rng_.Range(-1, 1);
                            if (!InBounds(lx, ly) || !IsWalkable(lx, ly) || NpcExistsAt(lx, ly)) continue;
                            var e = new Entity();
                            e.X = lx; e.Y = ly; e.Symbol = 'B'; e.Color = 0xcc4422;
                            e.Name = RandomName() + " " + RandomLastName();
                            e.Title = "Bandit Leader";
                            e.Hostile = true; e.Hp = 25; e.MaxHp = 25;
                            e.DamageDice = 2; e.DamageSides = 4; e.Armor = 3; e.Dodge = 2;
                            entities_.Add(e);
                            break;
                        }
                    }
                    AddItem(cx + 1, cy, "gold_coin", rng_.Range(3, 8));
                    ConnectFeature(cx, cy, false);
                    break;
                }
            }
        }

        private void PlaceMageTowers()
        {
            int num = rng_.Range(3, 6);
            for (int i = 0; i < num; i++)
            {
                for (int a = 0; a < 80; a++)
                {
                    int x = rng_.Range(24, SIZE - 24);
                    int y = rng_.Range(24, SIZE - 24);
                    var t = GetTerrain(x, y);
                    if (t != OvermapTerrain.Field && t != OvermapTerrain.Forest) continue;
                    if (IsRoad(t) || reserved_[y * SIZE + x] != 0) continue;
                    bool nearStuff = false;
                    foreach (var c in cities_)
                    {
                        int dx = c.X - x, dy = c.Y - y;
                        if (dx * dx + dy * dy < 30 * 30) { nearStuff = true; break; }
                    }
                    if (nearStuff) continue;

                    // A real, enterable tower: the building is a Watchtower with two
                    // floors, marked by arcane terrain at its outer corners.
                    int bid = TryPlaceSettlementBuilding(x, y, 7, 6, BuildingType.Watchtower, true);
                    if (bid < 0) continue;
                    Building tower = GetBuilding(bid);
                    tower.Name = "Mage Tower";
                    tower.NumFloors = 2;
                    tower.Landmark = true;
                    GenerateBuildingInterior(tower);
                    ReserveRect(x - 5, y - 5, 11, 11, 2);
                    for (int oy = -5; oy <= 5; oy += 10)
                        for (int ox = -5; ox <= 5; ox += 10)
                        {
                            int tx = x + ox, ty = y + oy;
                            if (InBounds(tx, ty) && !IsRoad(GetTerrain(tx, ty)) &&
                                GetTerrain(tx, ty) != OvermapTerrain.BuildingFloor &&
                                GetTerrain(tx, ty) != OvermapTerrain.BuildingWall)
                                SetTerrain(tx, ty, OvermapTerrain.MageTower);
                        }
                    int gx = x, gy = y;
                    if (FindInteriorSpawn(tower, 0, ref gx, ref gy))
                    {
                        var e = SpawnNPC(gx, gy, 'M', 0x9966cc, RandomName() + " " + RandomLastName(),
                                         "Arcane Guardian", "None may pass without the tower's leave.");
                        e.Hostile = true; e.Hp = 20; e.MaxHp = 20;
                        e.DamageDice = 2; e.DamageSides = 6; e.Armor = 4; e.Dodge = 1;
                        e.BuildingId = bid; e.Floor = 0;
                        tower.EntityIndices.Add(entities_.Count - 1);
                    }
                    AddItem(x, y - 1, "magic_crystal", 1, bid, 0);
                    AddItem(x + 1, y - 1, "mana_potion", 1, bid, 0);
                    ConnectFeature(x, y + 4, false); // start outside the tower door
                    break;
                }
            }
        }

        // ===== CARAVANS, ITEMS, WILDLIFE =====

        private void PlaceCaravans()
        {
            int num = rng_.Range(3, 7);
            for (int i = 0; i < num; i++)
            {
                for (int a = 0; a < 40; a++)
                {
                    int cx = rng_.Range(30, SIZE - 30);
                    int cy = rng_.Range(30, SIZE - 30);
                    var t = GetTerrain(cx, cy);
                    if (!IsRoad(t)) continue;
                    if (GetRoadType(t) == RoadType.Trail) continue; // caravans use real roads
                    if (NpcExistsAt(cx, cy)) continue;
                    int cityIdx = FindNearestCity(cx, cy);
                    if (cityIdx < 0) continue;

                    var merch = SpawnNPC(cx, cy, '$', 0xffaa44, RandomName() + " " + RandomLastName(),
                                         "Caravan Merchant", "Fresh goods from afar! Have a look.");
                    merch.ActionDesc = "Traveling with trade goods";
                    merch.ShopItems = new List<string> { "bread", "herb", "health_potion", "mana_potion", "rope", "torch", "arrow", "bolt", "stamina_potion" };
                    merch.ShopPrices = new List<int> { 3, 2, 18, 18, 5, 3, 2, 3, 10 };
                    merch.Behavior = EntityBehavior.Idle;
                    merch.HomeX = cx; merch.HomeY = cy;

                    int mx = cx + 1, my = cy;
                    if (IsWalkable(mx, my) && !NpcExistsAt(mx, my))
                    {
                        var mule = new Entity();
                        mule.X = mx; mule.Y = my; mule.Symbol = 'm'; mule.Color = 0xaa8866;
                        mule.Name = "Pack Mule"; mule.Title = "Pack Animal";
                        mule.Hp = 20; mule.MaxHp = 20;
                        mule.Behavior = EntityBehavior.Idle;
                        entities_.Add(mule);
                    }
                    int gx = cx - 1, gy = cy;
                    if (IsWalkable(gx, gy) && !NpcExistsAt(gx, gy))
                    {
                        var guard = SpawnNPC(gx, gy, 'g', 0x888866, RandomName() + " " + RandomLastName(),
                                             "Caravan Guard", "We've got some valuable cargo.");
                        guard.ActionDesc = "Guarding the caravan";
                        guard.Hp = 15; guard.MaxHp = 15;
                        guard.DamageDice = 1; guard.DamageSides = 6; guard.Armor = 2;
                        guard.Behavior = EntityBehavior.Idle;
                        guard.HomeX = gx; guard.HomeY = gy;
                    }
                    AddItem(cx + 2, cy, "gold_coin", rng_.Range(10, 30));
                    AddItem(cx + 2, cy + 1, "bread", rng_.Range(3, 6));
                    AddItem(cx + 2, cy - 1, "herb", rng_.Range(2, 5));
                    break;
                }
            }
        }

        private void PopulateSpecialTiles()
        {
            // Field and forest gathering spots, never inside a settlement or a road.
            for (int i = 0; i < 80; i++)
            {
                int x = rng_.Range(5, SIZE - 5);
                int y = rng_.Range(5, SIZE - 5);
                var t = GetTerrain(x, y);
                if (reserved_[y * SIZE + x] != 0 || IsRoad(t)) continue;
                if (t == OvermapTerrain.Field)
                {
                    if (rng_.OneIn(2)) AddItem(x, y, "herb", 1);
                    else AddItem(x, y, "wood", 1);
                }
                else if (t == OvermapTerrain.Forest)
                {
                    if (rng_.OneIn(3)) AddItem(x, y, "wood", 1);
                }
            }

            // A few predators in the woods.
            for (int i = 0; i < 20; i++)
            {
                int x = rng_.Range(5, SIZE - 5);
                int y = rng_.Range(5, SIZE - 5);
                var t = GetTerrain(x, y);
                if (t != OvermapTerrain.Forest || reserved_[y * SIZE + x] != 0) continue;
                if (!IsWalkable(x, y) || NpcExistsAt(x, y)) continue;
                if (rng_.OneIn(2)) continue;
                var e = new Entity();
                e.X = x; e.Y = y;
                e.Symbol = 'b'; e.Color = 0x886644; e.Name = "Bear";
                e.Title = "Wild Beast";
                e.Hostile = true; e.Hp = 30; e.MaxHp = 30;
                e.DamageDice = 2; e.DamageSides = 6; e.Armor = 1; e.Dodge = 9;
                e.Behavior = EntityBehavior.Wander;
                e.HomeX = x; e.HomeY = y;
                e.PatrolX = x + rng_.Range(-6, 6);
                e.PatrolY = y + rng_.Range(-6, 6);
                entities_.Add(e);
            }
        }

        private void SpawnWildlife()
        {
            // Animals only go where they can actually stand: walkable, unreserved land.
            int numAnimals = 110 + rng_.Range(-15, 15);
            int spawned = 0;
            int maxAttempts = numAnimals * 4;

            for (int attempt = 0; attempt < maxAttempts && spawned < numAnimals; attempt++)
            {
                int x = rng_.Range(8, SIZE - 8);
                int y = rng_.Range(8, SIZE - 8);
                var terrain = GetTerrain(x, y);
                if (reserved_[y * SIZE + x] != 0 || !IsWalkable(x, y)) continue;
                if (NpcExistsAt(x, y)) continue;

                CreatureType[] candidates;
                if (terrain == OvermapTerrain.Forest)
                {
                    candidates = new[] { CreatureType.Deer, CreatureType.Boar, CreatureType.Wolf,
                                         CreatureType.Bear, CreatureType.Fox, CreatureType.Raven };
                }
                else if (terrain == OvermapTerrain.Field)
                {
                    candidates = new[] { CreatureType.Rabbit, CreatureType.Deer, CreatureType.Boar,
                                         CreatureType.Eagle };
                }
                else if (terrain == OvermapTerrain.Swamp)
                {
                    candidates = new[] { CreatureType.Rabbit, CreatureType.Fox, CreatureType.Raven };
                }
                else if (terrain == OvermapTerrain.Tundra || terrain == OvermapTerrain.Snow)
                {
                    candidates = new[] { CreatureType.Elk, CreatureType.Fox, CreatureType.Eagle };
                }
                else if (terrain == OvermapTerrain.Desert)
                {
                    candidates = new[] { CreatureType.Boar, CreatureType.Raven };
                }
                else
                {
                    continue;
                }
                if (candidates.Length == 0) continue;

                CreatureType type = candidates[rng_.Range(0, candidates.Length - 1)];
                var tmpl = CreatureDB.Get(type);

                var e = new Entity();
                e.X = x; e.Y = y;
                e.Symbol = tmpl.Symbol;
                e.Color = tmpl.Color;
                e.Name = tmpl.Name;
                e.Title = "";
                e.ActionDesc = "Wandering";
                e.Hostile = (tmpl.Diet == DietType.Carnivore);
                e.Hp = tmpl.Hp;
                e.MaxHp = tmpl.Hp;
                e.DamageDice = 1;
                e.DamageSides = tmpl.Damage;
                e.Armor = tmpl.Armor;
                e.Dodge = Math.Max(0, 12 - tmpl.Speed);
                e.Behavior = EntityBehavior.Wander;
                e.HomeX = x; e.HomeY = y;
                e.PatrolX = x + rng_.Range(-12, 12);
                e.PatrolY = y + rng_.Range(-12, 12);
                e.IsWildlife = true;
                e.Creature = type;
                entities_.Add(e);
                spawned++;
            }
        }

        // ===== NAME TABLES (port of src/map/overmap_data.h) =====

        private static readonly string[] CITY_NAMES =
        {
            "Aetherhaven", "Blackhollow", "Copperkeep", "Duskfall", "Elderwood",
            "Frostmire", "Goldspire", "Hawthorn", "Ironforge", "Jadegate",
            "Kingsholm", "Lunaris", "Mistvale", "Northwatch", "Oakenshire",
            "Portwind", "Quietmill", "Ravenrock", "Stonehold", "Thornwall",
            "Underhill", "Valdoria", "Whitestone", "Yarrowdale", "Zephyrport",
            "Ashwick", "Brighthelm", "Crystalvale", "Darkhollow", "Emberford"
        };

        private static readonly string[] VILLAGE_NAMES =
        {
            "Applewood", "Birchfield", "Cloverdale", "Deepwell", "Eastmere",
            "Fernwood", "Greenhollow", "Hillcrest", "Ivybridge", "Maplecroft",
            "Newdale", "Oldmill", "Pinethorn", "Riverside", "Southgate"
        };
    }
}
