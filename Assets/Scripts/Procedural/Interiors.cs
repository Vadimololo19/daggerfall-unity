using System;
using System.Collections.Generic;

namespace FantasyRealms.World
{
    // =========================================================================
    // Building interiors (port of Overmap::generateBuildingInterior and
    // Overmap::findInteriorSpawn from src/map/overmap.cpp).
    // =========================================================================

    public partial class Overmap
    {
        public void GenerateBuildingInterior(Building b)
        {
            if (b.Type == BuildingType.None) return;
            int w = b.Width;
            int h = b.Height;
            int floors = Math.Max(1, b.NumFloors);
            if (w < 5 || h < 5) return;
            b.Interior.Clear();

            for (int f = 0; f < floors; ++f)
            {
                b.Interior.Add(BuildInteriorFloor(b, f, floors, w, h));
            }
            b.HasInterior = true;
        }

        private BuildingFloor BuildInteriorFloor(Building b, int f, int floors, int w, int h)
        {
            var bf = new BuildingFloor();
            bf.Width = w;
            bf.Height = h;
            for (int i = 0; i < w * h; i++)
            {
                bf.Tiles.Add(OvermapTerrain.BuildingWall);
                bf.Decoration.Add(Furniture.None);
            }

            bool InB(int x, int y) => x >= 0 && x < w && y >= 0 && y < h;
            void SetTile(int x, int y, OvermapTerrain tile) { if (InB(x, y)) bf.Tiles[y * w + x] = tile; }
            void SetDeco(int x, int y, Furniture furniture) { if (InB(x, y)) bf.Decoration[y * w + x] = furniture; }
            void AddRoom(int x1, int y1, int x2, int y2, int id, string name) =>
                bf.Rooms.Add(new Room { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Id = id, Name = name });
            void ClearDecorationAt(int x, int y) { if (InB(x, y)) bf.Decoration[y * w + x] = Furniture.None; }
            void AddDoor(int x, int y)
            {
                if (!InB(x, y)) return;
                SetTile(x, y, OvermapTerrain.BuildingFloor);
                ClearDecorationAt(x, y);
                SetDeco(x, y, Furniture.Door);
                bf.Doors.Add(new Door { X = x, Y = y, IsOpen = false, Floor = f, BuildingId = b.Id });
            }
            void AddLadder(int x, int y, int targetFloor)
            {
                if (!InB(x, y)) return;
                SetTile(x, y, OvermapTerrain.BuildingFloor);
                ClearDecorationAt(x, y);
                SetDeco(x, y, Furniture.Ladder);
                bf.Ladders.Add(new Ladder { X = x, Y = y, Floor = f, TargetFloor = targetFloor, BuildingId = b.Id });
            }

            // Every floor starts as one connected room. Interior generation must
            // never rely on a partially carved room.
            for (int y = 1; y < h - 1; ++y)
                for (int x = 1; x < w - 1; ++x)
                    SetTile(x, y, OvermapTerrain.BuildingFloor);

            int split = Math.Clamp(w / 2, 2, w - 3);
            int passageY = Math.Clamp(h / 2, 2, h - 3);
            // The ladder sits on the same local cell on every floor, so a room door
            // must never land on it: the ladder would erase the only door on a floor.
            int ladderX = w - 3;
            int ladderY = h - 3;
            int DoorRow(int x)
            {
                int y = passageY;
                if (x == ladderX && y == ladderY)
                    y = (ladderY > 2) ? ladderY - 1 : Math.Min(h - 2, ladderY + 1);
                return y;
            }
            void TwoRooms(string left, string right)
            {
                for (int y = 1; y < h - 1; ++y) SetTile(split, y, OvermapTerrain.BuildingWall);
                AddDoor(split, DoorRow(split));
                AddRoom(1, 1, split - 1, h - 2, 0, left);
                AddRoom(split + 1, 1, w - 2, h - 2, 1, right);
            }
            void OneRoom(string name) => AddRoom(1, 1, w - 2, h - 2, 0, name);
            bool PlaceIfEmpty(int x, int y, Furniture furniture)
            {
                if (!InB(x, y) || (x == w / 2 && y == h - 2)) return false;
                int index = y * w + x;
                if (bf.Tiles[index] != OvermapTerrain.BuildingFloor ||
                    bf.Decoration[index] != Furniture.None) return false;
                SetDeco(x, y, furniture);
                return true;
            }

            switch (b.Type)
            {
                case BuildingType.House:
                    TwoRooms("Living Room", "Bedroom");
                    PlaceIfEmpty(2, 2, Furniture.Table);
                    PlaceIfEmpty(1, 3, Furniture.Chair);
                    PlaceIfEmpty(3, 3, Furniture.Chair);
                    PlaceIfEmpty(w - 3, 2, Furniture.Bed);
                    PlaceIfEmpty(w - 2, 3, Furniture.Chest);
                    break;
                case BuildingType.Townhouse:
                    if (f == 0)
                    {
                        TwoRooms("Parlor", "Kitchen");
                        PlaceIfEmpty(2, 2, Furniture.Table);
                        PlaceIfEmpty(3, 3, Furniture.Chair);
                        PlaceIfEmpty(w - 3, 2, Furniture.Counter);
                    }
                    else
                    {
                        TwoRooms("Bedroom", "Study");
                        PlaceIfEmpty(2, 2, Furniture.Bed);
                        PlaceIfEmpty(3, 2, Furniture.Chest);
                        PlaceIfEmpty(w - 3, 2, Furniture.Bookshelf);
                    }
                    break;
                case BuildingType.Shop:
                    TwoRooms("Trading Hall", "Warehouse");
                    PlaceIfEmpty(2, 2, Furniture.Counter);
                    PlaceIfEmpty(3, 2, Furniture.Counter);
                    PlaceIfEmpty(2, 4, Furniture.Chest);
                    PlaceIfEmpty(w - 3, 2, Furniture.Crate);
                    PlaceIfEmpty(w - 2, 4, Furniture.Crate);
                    break;
                case BuildingType.Tavern:
                    if (f == 0)
                    {
                        TwoRooms("Common Hall", "Kitchen");
                        PlaceIfEmpty(2, 2, Furniture.Counter);
                        PlaceIfEmpty(3, 2, Furniture.Counter);
                        PlaceIfEmpty(2, 4, Furniture.Table);
                        PlaceIfEmpty(3, 4, Furniture.Bench);
                        PlaceIfEmpty(5, 4, Furniture.Table);
                        PlaceIfEmpty(6, 4, Furniture.Bench);
                    }
                    else
                    {
                        int roomSplit1 = w / 3;
                        int roomSplit2 = 2 * w / 3;
                        for (int y = 1; y < h - 1; ++y)
                        {
                            SetTile(roomSplit1, y, OvermapTerrain.BuildingWall);
                            SetTile(roomSplit2, y, OvermapTerrain.BuildingWall);
                        }
                        AddRoom(1, 1, roomSplit1 - 1, h - 2, 0, "Guest Room A");
                        AddRoom(roomSplit1 + 1, 1, roomSplit2 - 1, h - 2, 1, "Guest Room B");
                        AddRoom(roomSplit2 + 1, 1, w - 2, h - 2, 2, "Guest Room C");
                        AddDoor(roomSplit1, DoorRow(roomSplit1));
                        AddDoor(roomSplit2, DoorRow(roomSplit2));
                        PlaceIfEmpty(2, 2, Furniture.Bed);
                        PlaceIfEmpty(w / 3 + 3, 2, Furniture.Bed);
                        PlaceIfEmpty(2 * w / 3 + 3, 2, Furniture.Bed);
                    }
                    break;
                case BuildingType.Temple:
                    TwoRooms(f == 0 ? "Sanctuary" : "Archive", f == 0 ? "Side Chapel" : "Quarters");
                    PlaceIfEmpty(2, 2, Furniture.Brazier);
                    PlaceIfEmpty(3, 3, Furniture.Bench);
                    PlaceIfEmpty(2, 4, Furniture.Bench);
                    PlaceIfEmpty(w - 3, 2, f == 0 ? Furniture.Brazier : Furniture.Bookshelf);
                    break;
                case BuildingType.Smithy:
                    TwoRooms("Forge", "Storage");
                    PlaceIfEmpty(2, 2, Furniture.Brazier);
                    PlaceIfEmpty(3, 2, Furniture.Counter);
                    PlaceIfEmpty(2, 4, Furniture.Counter);
                    PlaceIfEmpty(w - 3, 2, Furniture.Crate);
                    PlaceIfEmpty(w - 2, 4, Furniture.Chest);
                    break;
                case BuildingType.Library:
                    TwoRooms("Reading Hall", "Archive");
                    PlaceIfEmpty(2, 2, Furniture.Bookshelf);
                    PlaceIfEmpty(2, 4, Furniture.Bookshelf);
                    PlaceIfEmpty(3, 3, Furniture.Table);
                    PlaceIfEmpty(3, 4, Furniture.Chair);
                    PlaceIfEmpty(w - 3, 2, Furniture.Bookshelf);
                    PlaceIfEmpty(w - 3, 4, Furniture.Bookshelf);
                    break;
                case BuildingType.Warehouse:
                    TwoRooms("Cargo Hall", "Cold Storage");
                    for (int y = 2; y < h - 2; y += 2)
                    {
                        PlaceIfEmpty(2, y, Furniture.Crate);
                        PlaceIfEmpty(w - 3, y, Furniture.Crate);
                    }
                    PlaceIfEmpty(w - 2, 2, Furniture.LockedChest);
                    break;
                case BuildingType.Barn:
                    TwoRooms("Stalls", "Hay Loft");
                    PlaceIfEmpty(2, 2, Furniture.Crate);
                    PlaceIfEmpty(3, 4, Furniture.Crate);
                    PlaceIfEmpty(w - 3, 2, Furniture.Crate);
                    PlaceIfEmpty(w - 3, 4, Furniture.Crate);
                    break;
                case BuildingType.Barracks:
                    TwoRooms("Sleeping Quarters", "Armory");
                    for (int y = 2; y < h - 2; y += 2)
                    {
                        PlaceIfEmpty(2, y, Furniture.Bed);
                        PlaceIfEmpty(3, y, Furniture.Chest);
                    }
                    PlaceIfEmpty(w - 3, 2, Furniture.Counter);
                    break;
                case BuildingType.TownHall:
                    TwoRooms(f == 0 ? "Public Hall" : "Council Chamber", "Records Office");
                    PlaceIfEmpty(2, 2, Furniture.Counter);
                    PlaceIfEmpty(3, 3, Furniture.Table);
                    PlaceIfEmpty(2, 4, Furniture.Bench);
                    PlaceIfEmpty(w - 3, 2, Furniture.Bookshelf);
                    PlaceIfEmpty(w - 3, 4, Furniture.LockedChest);
                    break;
                case BuildingType.Watchtower:
                    // Upper floors get a partition with a door so that every floor
                    // of a multi-floor building has at least one interior door.
                    if (f == 0) OneRoom("Guard Room");
                    else TwoRooms("Lookout", "Quarters");
                    PlaceIfEmpty(2, 2, Furniture.Counter);
                    PlaceIfEmpty(3, 2, Furniture.Chest);
                    PlaceIfEmpty(w - 3, 2, Furniture.Bench);
                    PlaceIfEmpty(w - 2, 4, Furniture.Bed);
                    break;
                default:
                    OneRoom("Interior");
                    break;
            }

            // The entrance is a real two-tile connection: outside floor -> door -> room.
            if (f == 0)
            {
                SetTile(w / 2, h - 1, OvermapTerrain.BuildingFloor);
                AddDoor(w / 2, h - 2);
            }
            if (f < floors - 1) AddLadder(ladderX, ladderY, f + 1);
            if (f > 0) AddLadder(ladderX, ladderY, f - 1);

            // Keep the entrance and stair landing clear. Furniture is deliberately
            // type-specific and is placed only on existing floor tiles.
            int furnitureCount = 0;
            foreach (var furniture in bf.Decoration)
                if (furniture != Furniture.None && furniture != Furniture.Door && furniture != Furniture.Ladder)
                    ++furnitureCount;
            Furniture[] fallback = { Furniture.Table, Furniture.Chair, Furniture.Chest,
                                     Furniture.Bench, Furniture.Crate, Furniture.Bed };
            for (int y = 1; y < h - 1 && furnitureCount < 5; ++y)
            {
                for (int x = 1; x < w - 1 && furnitureCount < 5; ++x)
                {
                    if (PlaceIfEmpty(x, y, fallback[(x + y + f) % 6])) ++furnitureCount;
                }
            }
            return bf;
        }

        public bool FindInteriorSpawn(Building b, int floor, ref int worldX, ref int worldY)
        {
            if (floor < 0 || floor >= b.Interior.Count) return false;
            var bf = b.Interior[floor];
            for (int y = 1; y < bf.Height - 1; ++y)
            {
                for (int x = 1; x < bf.Width - 1; ++x)
                {
                    int i = y * bf.Width + x;
                    if (bf.Tiles[i] != OvermapTerrain.BuildingFloor || bf.Decoration[i] != Furniture.None)
                        continue;
                    int wx = b.X - b.Width / 2 + x;
                    int wy = b.Y - b.Height / 2 + y;
                    bool occupied = false;
                    foreach (var entity in entities_)
                    {
                        if (entity.Hp > 0 && entity.BuildingId == b.Id &&
                            entity.Floor == floor && entity.X == wx && entity.Y == wy)
                        {
                            occupied = true;
                            break;
                        }
                    }
                    if (occupied) continue;
                    worldX = wx;
                    worldY = wy;
                    return true;
                }
            }
            return false;
        }
    }
}
