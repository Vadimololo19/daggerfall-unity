using System;
using System.Collections.Generic;

namespace FantasyRealms.World
{
    // =========================================================================
    // Enums. ORDER MUST MATCH src/map/tile.h EXACTLY: the acceptance tests reason
    // about walkability from the visuals produced by Tiles.GetOvermapVisual.
    // =========================================================================

    public enum OvermapTerrain : byte
    {
        Field, Forest, DenseForest, Swamp, Mountain, Water, Desert, Tundra, Snow,
        Town, TownCenter, Dungeon, Ruin, Shrine, Castle,
        RoadHorizontal, RoadVertical, BridgeHorizontal, BridgeVertical,
        TrailHorizontal, TrailVertical, StoneRoadHorizontal, StoneRoadVertical,
        Outpost, BanditCamp, MageTower,
        BuildingWall, BuildingFloor, BuildingDoor, CityWall, Tent, Campfire, TownGate, Pasture,
        COUNT
    }

    public enum Furniture : byte
    {
        None, Bed, Chair, Table, Chest, LockedChest, Bookshelf, Counter, Bench, Brazier, Crate, Door, Ladder, COUNT
    }

    public enum BuildingType : byte
    {
        None,
        House,        // Small residence
        Townhouse,    // 2-story residence (sometimes 1-floor)
        Shop,         // General store/shop
        Tavern,       // Tavern/inn
        Temple,       // Temple/shrine
        Smithy,       // Blacksmith
        Warehouse,    // Storage warehouse
        TownHall,     // City hall
        Watchtower,   // Guard watchtower
        Barn,         // Farm barn
        Well,         // Village well
        Stable,       // Horse stable
        Library,      // Public library
        Barracks,     // Town barracks
        COUNT
    }

    public enum RoadType : byte
    {
        None, Trail, Road, StoneRoad
    }

    public enum CreatureType : byte
    {
        // Herbivores
        Rabbit,
        Deer,
        Boar,
        Elk,
        // Carnivores
        Wolf,
        Bear,
        Fox,
        // Birds
        Raven,
        Eagle,
        COUNT
    }

    public enum DietType
    {
        Herbivore,
        Carnivore,
        Omnivore
    }

    // Port of Entity::Behavior.
    public enum EntityBehavior { Idle, Wander, Guard, Work, Sleep, Travel, Patrol }

    public struct TileVisual
    {
        public uint Color;
        public char Symbol;
        public bool Walkable;
        public bool Transparent;
        public string Name;

        public TileVisual(uint color, char symbol, bool walkable, bool transparent, string name)
        {
            Color = color; Symbol = symbol; Walkable = walkable; Transparent = transparent; Name = name;
        }
    }

    public struct Door
    {
        public int X, Y;
        public bool IsOpen;
        public int Floor;
        public int BuildingId;
    }

    public struct Ladder
    {
        public int X, Y;
        public int Floor;
        public int TargetFloor; // Which floor this ladder leads to
        public int BuildingId;
    }

    public struct Room
    {
        public int X1, Y1, X2, Y2; // Boundaries of the room
        public int Id;
        public string Name;
    }

    public sealed class BuildingFloor
    {
        public int Width;
        public int Height;
        public List<OvermapTerrain> Tiles = new List<OvermapTerrain>();   // width*height
        public List<Furniture> Decoration = new List<Furniture>();
        public List<Room> Rooms = new List<Room>();                       // rooms for visibility culling
        public List<Door> Doors = new List<Door>();                       // doors on this floor (with state)
        public List<Ladder> Ladders = new List<Ladder>();                 // ladders on this floor
    }

    public sealed class Building
    {
        public int Id;
        public int X, Y;              // center of footprint in overmap coordinates
        public int Width, Height;     // footprint on overmap
        public int EntranceOutsideX = -1, EntranceOutsideY = -1;
        public int EntranceInsideX = -1, EntranceInsideY = -1;
        public int NumFloors = 1;
        public BuildingType Type = BuildingType.House;
        public string Name = "";
        public List<int> EntityIndices = new List<int>(); // NPCs belonging to this building
        public List<BuildingFloor> Interior = new List<BuildingFloor>(); // per-floor layout
        public bool HasInterior = false;
        public bool Landmark = false; // mage towers stand outside settlements by design
    }

    public sealed class City
    {
        public int X, Y;
        public int Size;
        public string Name = "";
        public bool IsCity = false;
    }

    public sealed class Dungeon
    {
        public int X, Y;
        public int Depth;
        public OvermapTerrain Type;
    }

    public sealed class Entity
    {
        public int X, Y;
        public int Floor = 0;
        public int BuildingId = -1;
        public char Symbol;
        public uint Color;
        public string Name = "";
        public string Title = "";
        public string ActionDesc = "";
        public bool Hostile = false;
        public int Hp = 10, MaxHp = 10;
        public int DamageDice = 1, DamageSides = 4;
        public int Armor = 0;
        public int Dodge = 0;
        public string WeaponId = "";
        public List<string> ShopItems = new List<string>();
        public List<int> ShopPrices = new List<int>();
        public string Greeting = "Hello there.";

        // Personal inventory (NPCs carry items): (defId, quantity)
        public List<(string defId, int qty)> Inventory = new List<(string, int)>();

        // Behavior
        public EntityBehavior Behavior = EntityBehavior.Idle;
        public int PatrolX = -1, PatrolY = -1;
        public int HomeX = -1, HomeY = -1;
        public int WaitTimer = 0;

        // Wildlife tracking
        public CreatureType Creature = (CreatureType)0; // 0 = NPC
        public bool IsWildlife = false;
        public int LastHuntAttempt = 0;
    }

    public sealed class GroundItem
    {
        public int X, Y;
        public string DefId = "";
        public int Quantity = 1;
        public int BuildingId = -1;
        public int Floor = 0;
    }

    public struct CreatureTemplate
    {
        public string Name;
        public char Symbol;
        public uint Color;
        public DietType Diet;
        public int Hp;
        public int Damage;   // Damage dice max
        public int Armor;
        public int Speed;    // Movement speed (lower = faster)
        public float SightRange;
        public float FleeRange;
        public float HuntRange;
        public bool IsFlying;
    }

    // =========================================================================
    // Tile visuals (port of tile.h).
    // =========================================================================

    public static class Tiles
    {
        public static TileVisual GetFurnitureVisual(Furniture f)
        {
            switch (f)
            {
                case Furniture.Bed:         return new TileVisual(0xcc4444, '=', true, true, "Bed");
                case Furniture.Chair:       return new TileVisual(0x886644, 'h', true, true, "Chair");
                case Furniture.Table:       return new TileVisual(0x8B4513, 'w', true, true, "Table");
                case Furniture.Chest:       return new TileVisual(0x886644, '&', true, false, "Chest");
                case Furniture.LockedChest: return new TileVisual(0x886644, '&', true, false, "Locked Chest");
                case Furniture.Bookshelf:   return new TileVisual(0x884422, '#', false, false, "Bookshelf");
                case Furniture.Counter:     return new TileVisual(0x777755, '=', true, false, "Counter");
                case Furniture.Bench:       return new TileVisual(0x886644, 'H', true, true, "Bench");
                case Furniture.Brazier:     return new TileVisual(0xff6633, '0', true, true, "Brazier");
                case Furniture.Crate:       return new TileVisual(0x886644, '%', true, false, "Crate");
                case Furniture.Door:        return new TileVisual(0x8B4513, '+', true, false, "Door");
                case Furniture.Ladder:      return new TileVisual(0xaa8844, 'H', true, true, "Ladder");
                default:                    return new TileVisual(0x000000, ' ', true, true, "");
            }
        }

        public static uint BuildingWallColor(BuildingType bt)
        {
            switch (bt)
            {
                case BuildingType.House:      return 0x7a6a4a;
                case BuildingType.Townhouse:  return 0x8a7a5a;
                case BuildingType.Shop:       return 0x9a8a5a;
                case BuildingType.Tavern:     return 0x6a4a3a;
                case BuildingType.Temple:     return 0xaaaacc;
                case BuildingType.Smithy:     return 0x5a3a2a;
                case BuildingType.Warehouse:  return 0x6a5a4a;
                case BuildingType.TownHall:   return 0xbbaa88;
                case BuildingType.Watchtower: return 0x8a7a6a;
                case BuildingType.Barn:       return 0x7a6a3a;
                case BuildingType.Well:       return 0x555566;
                case BuildingType.Stable:     return 0x6a5a3a;
                case BuildingType.Library:    return 0x8a7a5a;
                case BuildingType.Barracks:   return 0x5a4a3a;
                default:                      return 0x5a4a3a;
            }
        }

        public static char BuildingWallChar(BuildingType bt)
        {
            switch (bt)
            {
                case BuildingType.TownHall:   return 'H';
                case BuildingType.Temple:     return 'C';
                case BuildingType.Watchtower: return 'T';
                case BuildingType.Tavern:     return '&';
                case BuildingType.Townhouse:  return '#';
                case BuildingType.Barn:       return 'B';
                default:                      return '#';
            }
        }

        public static TileVisual GetOvermapVisual(OvermapTerrain t)
        {
            switch (t)
            {
                case OvermapTerrain.Field:               return new TileVisual(0x4a8c3f, '.', true, true, "Field");
                case OvermapTerrain.Forest:              return new TileVisual(0x2d6b1e, 'T', true, false, "Forest");
                case OvermapTerrain.DenseForest:         return new TileVisual(0x1a4a10, 'T', false, false, "Dense forest");
                case OvermapTerrain.Swamp:               return new TileVisual(0x3a5a3a, '&', true, false, "Swamp");
                case OvermapTerrain.Mountain:            return new TileVisual(0x6a6a6a, '^', false, false, "Mountain");
                case OvermapTerrain.Water:               return new TileVisual(0x2a5a8c, '~', false, false, "Water");
                case OvermapTerrain.Desert:              return new TileVisual(0xd4b856, '.', true, true, "Desert");
                case OvermapTerrain.Tundra:              return new TileVisual(0x9aaa8a, '.', true, true, "Tundra");
                case OvermapTerrain.Snow:                return new TileVisual(0xeeeeee, '.', true, true, "Snow");
                case OvermapTerrain.Town:                return new TileVisual(0x8a6a4a, '#', true, true, "Town district");
                case OvermapTerrain.TownCenter:          return new TileVisual(0xaa8a5a, 'C', true, true, "Town square");
                case OvermapTerrain.Dungeon:             return new TileVisual(0xaa2222, 'D', false, false, "Dungeon");
                case OvermapTerrain.Ruin:                return new TileVisual(0x886644, '%', false, false, "Ruins");
                case OvermapTerrain.Shrine:              return new TileVisual(0xccaacc, 'S', false, false, "Shrine");
                case OvermapTerrain.Castle:              return new TileVisual(0x888888, 'H', false, false, "Castle");
                case OvermapTerrain.RoadHorizontal:      return new TileVisual(0x6a6a6a, '=', true, true, "Road");
                case OvermapTerrain.RoadVertical:        return new TileVisual(0x6a6a6a, '=', true, true, "Road");
                case OvermapTerrain.BridgeHorizontal:    return new TileVisual(0x8a7a5a, '=', true, true, "Bridge");
                case OvermapTerrain.BridgeVertical:      return new TileVisual(0x8a7a5a, '=', true, true, "Bridge");
                case OvermapTerrain.TrailHorizontal:     return new TileVisual(0x7a8a5a, ',', true, true, "Trail");
                case OvermapTerrain.TrailVertical:       return new TileVisual(0x7a8a5a, ',', true, true, "Trail");
                case OvermapTerrain.StoneRoadHorizontal: return new TileVisual(0x8a8a8a, '#', true, true, "Paved road");
                case OvermapTerrain.StoneRoadVertical:   return new TileVisual(0x8a8a8a, '#', true, true, "Paved road");
                case OvermapTerrain.Outpost:             return new TileVisual(0x888866, 'O', false, false, "Outpost");
                case OvermapTerrain.BanditCamp:          return new TileVisual(0x884422, 'B', false, false, "Bandit camp");
                case OvermapTerrain.MageTower:           return new TileVisual(0x9966cc, 'T', false, false, "Mage tower");
                case OvermapTerrain.BuildingWall:        return new TileVisual(0x5a4a3a, '#', false, false, "Building wall");
                case OvermapTerrain.BuildingFloor:       return new TileVisual(0x8a7a5a, '.', true, true, "Building floor");
                case OvermapTerrain.BuildingDoor:        return new TileVisual(0x8b4513, '+', true, true, "Building door");
                case OvermapTerrain.CityWall:            return new TileVisual(0x6a5a4a, '#', false, false, "City wall");
                case OvermapTerrain.Tent:                return new TileVisual(0xa08860, '^', true, false, "Tent");
                case OvermapTerrain.Campfire:            return new TileVisual(0xcc4400, '&', true, true, "Campfire");
                case OvermapTerrain.TownGate:            return new TileVisual(0x8a7a5a, '=', true, true, "Town gate");
                case OvermapTerrain.Pasture:             return new TileVisual(0x6aac5a, ',', true, true, "Pasture");
                default:                                 return new TileVisual(0xff00ff, '?', false, false, "Unknown");
            }
        }
    }

    // =========================================================================
    // Overmap: storage + queries (port of src/map/overmap.cpp).
    // Generation lives in Generation.cs; interiors in Interiors.cs.
    // =========================================================================

    public partial class Overmap
    {
        public const int SIZE = 512;

        private readonly OvermapTerrain[] data_ = new OvermapTerrain[SIZE * SIZE];
        private readonly byte[] reserved_ = new byte[SIZE * SIZE];
        private readonly Noise noise_;
        private readonly Rng rng_;

        private readonly List<City> cities_ = new List<City>();
        private readonly List<Dungeon> dungeons_ = new List<Dungeon>();
        private readonly List<Entity> entities_ = new List<Entity>();
        private readonly List<GroundItem> groundItems_ = new List<GroundItem>();
        private readonly List<Building> buildings_ = new List<Building>();
        private readonly Dictionary<int, int> tileBuildingMap_ = new Dictionary<int, int>();

        private int nextBuildingId_ = 1;

        private const int CITY_MIN_DIST = 28;
        private const int VILLAGE_MIN_DIST = 18;
        private const int DUNGEON_MIN_DIST = 14;

        private static int Key(int x, int y) => (y << 9) | x;
        private static int Idx(int x, int y) => y * SIZE + x;

        public Overmap(ulong seed)
        {
            noise_ = new Noise(seed);
            rng_ = new Rng(unchecked(seed + 1));
        }

        // ===== BOUNDS & TERRAIN =====

        public bool InBounds(int x, int y) => x >= 0 && x < SIZE && y >= 0 && y < SIZE;

        public OvermapTerrain GetTerrain(int x, int y)
        {
            if (!InBounds(x, y)) return OvermapTerrain.Water;
            return data_[Idx(x, y)];
        }

        public void SetTerrain(int x, int y, OvermapTerrain t)
        {
            if (InBounds(x, y)) data_[Idx(x, y)] = t;
        }

        public bool IsWater(int x, int y) => GetTerrain(x, y) == OvermapTerrain.Water;

        // ===== WALKABILITY =====

        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            var t = GetTerrain(x, y);
            return t != OvermapTerrain.Water &&
                   t != OvermapTerrain.Mountain &&
                   t != OvermapTerrain.BuildingWall &&
                   t != OvermapTerrain.CityWall &&
                   t != OvermapTerrain.Dungeon &&
                   t != OvermapTerrain.Shrine &&
                   t != OvermapTerrain.Castle &&
                   t != OvermapTerrain.MageTower &&
                   t != OvermapTerrain.Outpost &&
                   t != OvermapTerrain.BanditCamp;
        }

        public bool IsWalkableAt(int x, int y, int floor, int buildingId)
        {
            if (buildingId >= 0)
            {
                var t = GetTerrainAt(x, y, floor, buildingId);
                var vis = Tiles.GetOvermapVisual(t);
                if (!vis.Walkable) return false;
                // Check furniture blocking
                foreach (var b in buildings_)
                {
                    if (b.Id != buildingId) continue;
                    if (floor < 0 || floor >= b.Interior.Count) return false;
                    int lx = x - (b.X - b.Width / 2);
                    int ly = y - (b.Y - b.Height / 2);
                    if (lx < 0 || lx >= b.Interior[floor].Width ||
                        ly < 0 || ly >= b.Interior[floor].Height)
                        return false;
                    var f = b.Interior[floor].Decoration[ly * b.Interior[floor].Width + lx];
                    if (f != Furniture.None)
                    {
                        // A closed door blocks; an open door is passable.
                        if (f == Furniture.Door)
                        {
                            foreach (var door in b.Interior[floor].Doors)
                            {
                                if (door.X == lx && door.Y == ly && !door.IsOpen)
                                    return false;
                            }
                            return true;
                        }
                        var fv = Tiles.GetFurnitureVisual(f);
                        return fv.Walkable;
                    }
                    return true;
                }
                return false;
            }
            return IsWalkable(x, y);
        }

        public OvermapTerrain GetTerrainAt(int x, int y, int floor, int buildingId)
        {
            if (buildingId < 0) return GetTerrain(x, y);
            foreach (var b in buildings_)
            {
                if (b.Id != buildingId) continue;
                if (floor < 0 || floor >= b.Interior.Count) return OvermapTerrain.BuildingWall;
                int lx = x - (b.X - b.Width / 2);
                int ly = y - (b.Y - b.Height / 2);
                if (lx < 0 || lx >= b.Interior[floor].Width ||
                    ly < 0 || ly >= b.Interior[floor].Height)
                    return OvermapTerrain.BuildingWall;
                return b.Interior[floor].Tiles[ly * b.Interior[floor].Width + lx];
            }
            return OvermapTerrain.BuildingWall;
        }

        // ===== TRANSPARENCY =====

        public bool IsTransparent(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            switch (GetTerrain(x, y))
            {
                case OvermapTerrain.Mountain:
                case OvermapTerrain.DenseForest:
                case OvermapTerrain.BuildingWall:
                case OvermapTerrain.CityWall:
                case OvermapTerrain.Tent:
                case OvermapTerrain.Dungeon:
                case OvermapTerrain.Shrine:
                case OvermapTerrain.Castle:
                case OvermapTerrain.MageTower:
                case OvermapTerrain.Ruin:
                case OvermapTerrain.Outpost:
                case OvermapTerrain.BanditCamp:
                    return false;
                default:
                    return true;
            }
        }

        // ===== SIGHT / NOISE =====

        public int SightRangeAt(int x, int y)
        {
            switch (GetTerrain(x, y))
            {
                case OvermapTerrain.DenseForest: return 7;
                case OvermapTerrain.Forest: return 10;
                case OvermapTerrain.Swamp: return 11;
                case OvermapTerrain.Town:
                case OvermapTerrain.TownCenter:
                case OvermapTerrain.BuildingFloor:
                case OvermapTerrain.Tent: return 12;
                default: return 16;
            }
        }

        public int AreaNoiseAt(int x, int y)
        {
            switch (GetTerrain(x, y))
            {
                case OvermapTerrain.Town:
                case OvermapTerrain.TownCenter:
                case OvermapTerrain.Castle:
                    return 8;
                case OvermapTerrain.Outpost:
                case OvermapTerrain.BanditCamp:
                case OvermapTerrain.MageTower:
                case OvermapTerrain.Tent:
                case OvermapTerrain.BuildingFloor:
                    return 5;
                case OvermapTerrain.Forest:
                case OvermapTerrain.DenseForest:
                    return 3;
                case OvermapTerrain.Swamp:
                case OvermapTerrain.Field:
                case OvermapTerrain.Pasture:
                case OvermapTerrain.RoadHorizontal:
                case OvermapTerrain.RoadVertical:
                case OvermapTerrain.TrailHorizontal:
                case OvermapTerrain.TrailVertical:
                case OvermapTerrain.StoneRoadHorizontal:
                case OvermapTerrain.StoneRoadVertical:
                    return 2;
                case OvermapTerrain.Desert:
                case OvermapTerrain.Mountain:
                case OvermapTerrain.Water:
                case OvermapTerrain.Tundra:
                case OvermapTerrain.Snow:
                case OvermapTerrain.Dungeon:
                case OvermapTerrain.Ruin:
                    return 1;
                default:
                    return 4;
            }
        }

        // ===== CITY / LOCATION HELPERS =====

        public string GetCityNameAt(int x, int y)
        {
            foreach (var c in cities_)
                if (c.X == x && c.Y == y) return c.Name;
            return "";
        }

        public bool HasHostileAt(int x, int y)
        {
            foreach (var e in entities_)
                if (e.X == x && e.Y == y && e.Hostile && e.Hp > 0) return true;
            return false;
        }

        // ===== GROUND ITEMS =====

        public List<GroundItem> GetItemsAt(int x, int y, int buildingId = -1, int floor = 0)
        {
            var result = new List<GroundItem>();
            foreach (var gi in groundItems_)
                if (gi.X == x && gi.Y == y && gi.BuildingId == buildingId && gi.Floor == floor)
                    result.Add(gi);
            return result;
        }

        public void AddItem(int x, int y, string defId, int qty = 1, int buildingId = -1, int floor = 0)
        {
            foreach (var gi in groundItems_)
            {
                if (gi.X == x && gi.Y == y && gi.DefId == defId &&
                    gi.BuildingId == buildingId && gi.Floor == floor)
                {
                    gi.Quantity += qty;
                    return;
                }
            }
            groundItems_.Add(new GroundItem { X = x, Y = y, DefId = defId, Quantity = qty, BuildingId = buildingId, Floor = floor });
        }

        public void RemoveItem(int x, int y, string defId, int qty = 1, int buildingId = -1, int floor = 0)
        {
            for (int i = 0; i < groundItems_.Count; i++)
            {
                var gi = groundItems_[i];
                if (gi.X == x && gi.Y == y && gi.DefId == defId && gi.BuildingId == buildingId && gi.Floor == floor)
                {
                    gi.Quantity -= qty;
                    if (gi.Quantity <= 0)
                    {
                        groundItems_[i] = groundItems_[groundItems_.Count - 1];
                        groundItems_.RemoveAt(groundItems_.Count - 1);
                    }
                    return;
                }
            }
        }

        public bool HasItem(int x, int y, string defId)
        {
            foreach (var gi in groundItems_)
                if (gi.X == x && gi.Y == y && gi.DefId == defId && gi.Quantity > 0) return true;
            return false;
        }

        // ===== BUILDING SYSTEM =====

        public int NextBuildingId() => nextBuildingId_++;

        public Building GetBuilding(int id)
        {
            foreach (var b in buildings_)
                if (b.Id == id) return b;
            return new Building { Id = -1 };
        }

        public int GetBuildingIdAt(int x, int y)
        {
            if (tileBuildingMap_.TryGetValue(Key(x, y), out int id)) return id;
            return -1;
        }

        public Building GetBuildingAt(int x, int y)
        {
            if (tileBuildingMap_.TryGetValue(Key(x, y), out int id))
            {
                foreach (var b in buildings_)
                    if (b.Id == id) return b;
            }
            return null;
        }

        public IReadOnlyList<Building> Buildings => buildings_;
        public IReadOnlyList<City> Cities => cities_;
        public IReadOnlyList<Dungeon> Dungeons => dungeons_;
        public IReadOnlyList<Entity> Entities => entities_;
        public IReadOnlyList<GroundItem> GroundItems => groundItems_;

        /// <summary>Drop an item on the ground (loot, discarded goods). Not generation.</summary>
        public void AddGroundItem(int x, int y, string defId, int qty)
        {
            groundItems_.Add(new GroundItem { X = x, Y = y, DefId = defId, Quantity = qty });
        }

        public bool IsRoad(OvermapTerrain t) =>
            t == OvermapTerrain.RoadHorizontal ||
            t == OvermapTerrain.RoadVertical ||
            t == OvermapTerrain.StoneRoadHorizontal ||
            t == OvermapTerrain.StoneRoadVertical ||
            t == OvermapTerrain.TrailHorizontal ||
            t == OvermapTerrain.TrailVertical ||
            t == OvermapTerrain.BridgeHorizontal ||
            t == OvermapTerrain.BridgeVertical;

        public RoadType GetRoadType(OvermapTerrain t)
        {
            if (t == OvermapTerrain.StoneRoadHorizontal || t == OvermapTerrain.StoneRoadVertical)
                return RoadType.StoneRoad;
            if (t == OvermapTerrain.RoadHorizontal || t == OvermapTerrain.RoadVertical)
                return RoadType.Road;
            if (t == OvermapTerrain.TrailHorizontal || t == OvermapTerrain.TrailVertical)
                return RoadType.Trail;
            return RoadType.None;
        }

        // ===== RECTANGLE HELPERS =====

        private void PlaceRect(int x, int y, int w, int h, OvermapTerrain t)
        {
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    SetTerrain(x + dx, y + dy, t);
        }

        private void PlaceHollowRect(int x, int y, int w, int h, OvermapTerrain wall, OvermapTerrain fill)
        {
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    if (dx == 0 || dx == w - 1 || dy == 0 || dy == h - 1)
                        SetTerrain(x + dx, y + dy, wall);
                    else
                        SetTerrain(x + dx, y + dy, fill);
                }
        }

        private void PlaceBuilding(int cx, int cy, int w, int h, OvermapTerrain wall, OvermapTerrain floor)
        {
            int ox = cx - w / 2, oy = cy - h / 2;
            PlaceHollowRect(ox, oy, w, h, wall, floor);
            if (h >= 3) SetTerrain(ox + w / 2, oy + h - 1, OvermapTerrain.BuildingDoor);
        }

        private void PlaceBuilding(int cx, int cy, int w, int h, BuildingType bt)
        {
            int ox = cx - w / 2, oy = cy - h / 2;
            PlaceHollowRect(ox, oy, w, h, OvermapTerrain.BuildingWall, OvermapTerrain.BuildingFloor);
            if (h >= 3) SetTerrain(ox + w / 2, oy + h - 1, OvermapTerrain.BuildingDoor);
        }

        // ===== NEAREST SETTLEMENT =====

        private int FindNearestCity(int x, int y)
        {
            if (cities_.Count == 0) return -1;
            int best = 0, bestDist = 999999;
            for (int i = 0; i < cities_.Count; i++)
            {
                if (!cities_[i].IsCity) continue;
                int dx = cities_[i].X - x, dy = cities_[i].Y - y;
                int dist = dx * dx + dy * dy;
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        private int FindNearestSettlement(int x, int y)
        {
            if (cities_.Count == 0) return -1;
            int best = 0, bestDist = 999999;
            for (int i = 0; i < cities_.Count; i++)
            {
                int dx = cities_[i].X - x, dy = cities_[i].Y - y;
                int dist = dx * dx + dy * dy;
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        // ===== NPC HELPERS =====

        private static readonly string[] FIRST_NAMES =
        {
            "Aldric", "Bertha", "Cedric", "Drogo", "Elara", "Finn", "Greta", "Hrothgar",
            "Isolde", "Jorik", "Kara", "Leif", "Mira", "Norbert", "Odette", "Percival",
            "Quinn", "Ragnar", "Sigrid", "Torben", "Ursula", "Valerius", "Wilma", "Yorick",
            "Zelda", "Alaric", "Bran", "Catherine", "Duncan", "Edda", "Freyja", "Gunnar",
            "Helga", "Ivar", "Jens", "Knut", "Lena", "Magnus", "Nadia", "Olaf"
        };

        private static readonly string[] LAST_NAMES =
        {
            "Erikson", "Bjornsson", "Haldorsdottir", "Ironhand", "Stormborn", "Ashwood",
            "Blackwood", "Copperfield", "Dawnbringer", "Fairwind", "Goldheart", "Hawthorne",
            "Ironfoot", "Jadehelm", "Keystone", "Longstrider", "Moonwhisper", "Nightshade",
            "Oakenshield", "Proudfoot", "Quickblade", "Ravenclaw", "Silvermane", "Thornwood",
            "Underhill", "Voidwalker", "Whitehorn", "Yellowcrest", "Steelhilt", "Brightshield"
        };

        private string RandomName() => FIRST_NAMES[rng_.Range(0, 39)];
        private string RandomLastName() => LAST_NAMES[rng_.Range(0, 29)];

        public Entity SpawnNPC(int x, int y, char sym, uint color, string name, string title, string greeting)
        {
            var e = new Entity();
            e.X = x; e.Y = y; e.Symbol = sym; e.Color = color;
            e.Name = name; e.Title = title; e.Greeting = greeting;
            e.HomeX = x; e.HomeY = y;
            e.Hp = 10; e.MaxHp = 10;
            entities_.Add(e);
            PopulateNPCInventory(e);
            return e;
        }

        private void PopulateNPCInventory(Entity e)
        {
            var inv = e.Inventory;
            void Add(string id, int qty = 1) => inv.Add((id, qty));
            void AddSome(string[] items, int count)
            {
                for (int i = 0; i < count && items.Length > 0; i++)
                    inv.Add((items[rng_.Range(0, items.Length - 1)], 1));
            }

            string t = e.Title;
            var smithItems = new[] { "short_sword", "spear", "mace", "shield", "iron_helmet", "leather_armor", "hammer" };
            var tailorItems = new[] { "cloak", "hood", "leather_cap", "leather_gloves", "cloth", "silk" };
            var alchemistItems = new[] { "health_potion", "antidote", "herb", "poison_vial", "mana_potion", "stamina_potion", "glass" };
            var generalItems = new[] { "bread", "cheese", "dried_meat", "rope", "torch", "water_flask", "lantern", "candle" };
            var weaponItems = new[] { "dagger", "short_sword", "long_sword", "spear", "bow", "arrow", "crossbow", "bolt" };
            var armorItems = new[] { "leather_armor", "leather_helmet", "leather_boots", "leather_gloves", "shield", "chainmail", "iron_helmet", "iron_boots" };
            var bakerItems = new[] { "bread", "cheese", "berry_pie", "salt" };
            var jewelerItems = new[] { "silver_ring", "gold_ring", "copper_amulet", "silver_amulet", "gold_amulet", "jewelry_box" };
            var tavernItems = new[] { "ale", "wine", "mead", "bread", "cheese", "dried_meat", "cooked_fish" };
            var farmerItems = new[] { "apple", "cheese", "wood", "cloth", "leather", "fur", "feather" };
            var commonItems = new[] { "bread", "cheese", "dried_meat", "water_flask", "herb" };

            if (t.Contains("Smith")) { AddSome(smithItems, rng_.Range(3, 6)); Add("gold_coin", rng_.Range(5, 20)); }
            else if (t.Contains("Tailor")) { AddSome(tailorItems, rng_.Range(3, 5)); Add("gold_coin", rng_.Range(3, 15)); }
            else if (t.Contains("Alchemist")) { AddSome(alchemistItems, rng_.Range(3, 6)); Add("gold_coin", rng_.Range(8, 25)); }
            else if (t.Contains("General Store")) { AddSome(generalItems, rng_.Range(4, 8)); Add("gold_coin", rng_.Range(10, 30)); }
            else if (t.Contains("Weaponry") || t.Contains("Weapon")) { AddSome(weaponItems, rng_.Range(3, 6)); Add("gold_coin", rng_.Range(5, 20)); }
            else if (t.Contains("Armoury")) { AddSome(armorItems, rng_.Range(3, 5)); Add("gold_coin", rng_.Range(5, 20)); }
            else if (t.Contains("Baker")) { AddSome(bakerItems, rng_.Range(4, 7)); Add("gold_coin", rng_.Range(2, 10)); }
            else if (t.Contains("Jeweler")) { AddSome(jewelerItems, rng_.Range(2, 4)); Add("gold_coin", rng_.Range(15, 40)); }
            else if (t.Contains("Innkeeper")) { AddSome(tavernItems, rng_.Range(4, 8)); Add("gold_coin", rng_.Range(5, 15)); }
            else if (t.Contains("Mayor")) { Add("gold_coin", rng_.Range(20, 50)); Add("silver_ring", 1); Add("bread", 2); Add("wine", 1); }
            else if (t.Contains("Elder")) { Add("gold_coin", rng_.Range(10, 30)); Add("herb", rng_.Range(2, 5)); Add("book_history", 1); }
            else if (t.Contains("Guard") || t.Contains("Militia"))
            {
                if (rng_.OneIn(2)) Add("dagger", 1);
                if (rng_.OneIn(3)) Add("spear", 1);
                if (rng_.OneIn(2)) Add("bread", 1);
                Add("gold_coin", rng_.Range(1, 5));
            }
            else if (t.Contains("Farmer") || t.Contains("Herder")) { AddSome(farmerItems, rng_.Range(2, 5)); }
            else if (t.Contains("Merchant") || t.Contains("Trader")) { AddSome(generalItems, rng_.Range(3, 6)); Add("gold_coin", rng_.Range(10, 40)); }
            else if (t.Contains("Camper")) { AddSome(commonItems, rng_.Range(1, 3)); Add("torch", rng_.Range(1, 2)); }
            else if (t.Contains("Villager") || t.Contains("Resident") || t.Contains("Citizen") ||
                     t.Contains("Artisan") || t.Contains("Scholar") || t.Contains("Apprentice"))
            {
                AddSome(commonItems, rng_.Range(1, 3));
                if (rng_.OneIn(3)) Add("gold_coin", rng_.Range(1, 5));
            }
            else if (t.Contains("Woodcutter") || t.Contains("Wood")) { Add("wood", rng_.Range(2, 5)); Add("bread", 1); }
            else if (t.Contains("Fisher")) { Add("cooked_fish", rng_.Range(1, 3)); Add("water_flask", 1); }
            else if (t.Contains("Herbalist")) { Add("herb", rng_.Range(3, 6)); Add("antidote", rng_.Range(1, 2)); }
            else
            {
                if (rng_.OneIn(2)) AddSome(commonItems, 1);
                if (rng_.OneIn(4)) Add("gold_coin", rng_.Range(1, 5));
            }
        }

        public bool NpcExistsAt(int x, int y, int buildingId = -1, int floor = -1)
        {
            foreach (var e in entities_)
                if (e.X == x && e.Y == y && e.Hp > 0 &&
                    (buildingId < 0 || (e.BuildingId == buildingId &&
                                        (floor < 0 || e.Floor == floor)))) return true;
            return false;
        }
    }
}
