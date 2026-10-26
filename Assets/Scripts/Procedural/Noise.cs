using System;

namespace FantasyRealms.World
{
    // =========================================================================
    // Port of src/utils/noise.cpp. Exact ulong arithmetic, same constants and
    // 32-bit float math, so a given seed produces the same world as the C++ build.
    // =========================================================================

    public sealed class Rng
    {
        private ulong state;

        public Rng(ulong seed) { state = seed; }

        public ulong Next()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }

        public int Range(int min, int max)
        {
            if (min >= max) return min;
            return min + (int)(Next() % (ulong)(max - min + 1));
        }

        public float RangeF(float min, float max)
        {
            float t = (float)(Next() >> 11) * (1.0f / 0x200000);
            return min + t * (max - min);
        }

        public bool OneIn(int n)
        {
            return n > 0 && (Next() % (ulong)n) == 0;
        }
    }

    public sealed class Noise
    {
        private readonly ulong seed_;

        public Noise(ulong seed) { seed_ = seed; }

        private static ulong HashInt(int x, int y, ulong seed)
        {
            // static_cast<uint64_t>(int) sign-extends; C# (ulong)(long) matches.
            ulong h = seed;
            h ^= unchecked((ulong)(long)x) * 0x9E3779B97F4A7C15UL;
            h ^= unchecked((ulong)(long)y) * 0xBF58476D1CE4E5B9UL;
            h ^= h >> 30;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 27;
            h *= 0x94D049BB133111EBUL;
            h ^= h >> 31;
            return h;
        }

        private float SmoothNoise(int ix, int iy)
        {
            ulong h = HashInt(ix, iy, seed_);
            return (float)h / ulong.MaxValue;
        }

        private float Interpolate(float a, float b, float t)
        {
            float t2 = t * t * (3.0f - 2.0f * t);
            return a + (b - a) * t2;
        }

        public float Noise2D(float x, float y)
        {
            int ix = (int)MathF.Floor(x);
            int iy = (int)MathF.Floor(y);
            float fx = x - ix;
            float fy = y - iy;

            float v00 = SmoothNoise(ix, iy);
            float v10 = SmoothNoise(ix + 1, iy);
            float v01 = SmoothNoise(ix, iy + 1);
            float v11 = SmoothNoise(ix + 1, iy + 1);

            float ix0 = Interpolate(v00, v10, fx);
            float ix1 = Interpolate(v01, v11, fx);
            return Interpolate(ix0, ix1, fy);
        }

        public float FractalNoise2D(float x, float y, int octaves = 4, float persistence = 0.5f, float scale = 64.0f)
        {
            float total = 0;
            float amplitude = 1;
            float maxValue = 0;
            for (int i = 0; i < octaves; i++)
            {
                float sx = x / scale * (float)(1 << i);
                float sy = y / scale * (float)(1 << i);
                total += Noise2D(sx, sy) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
            }
            return total / maxValue;
        }
    }

    // =========================================================================
    // Port of src/world/creature_db.cpp.
    // =========================================================================

    public static class CreatureDB
    {
        private static readonly CreatureTemplate[] CREATURES =
        {
            // Herbivores
            new CreatureTemplate { Name = "Rabbit", Symbol = 'r', Color = 0xcc8844, Diet = DietType.Herbivore, Hp = 3,  Damage = 1, Armor = 0, Speed = 1, SightRange = 8.0f,  FleeRange = 5.0f,  HuntRange = 0.0f,  IsFlying = false },
            new CreatureTemplate { Name = "Deer",   Symbol = 'd', Color = 0x8844cc, Diet = DietType.Herbivore, Hp = 8,  Damage = 2, Armor = 0, Speed = 2, SightRange = 12.0f, FleeRange = 8.0f,  HuntRange = 0.0f,  IsFlying = false },
            new CreatureTemplate { Name = "Boar",   Symbol = 'b', Color = 0x664422, Diet = DietType.Herbivore, Hp = 15, Damage = 3, Armor = 1, Speed = 2, SightRange = 10.0f, FleeRange = 6.0f,  HuntRange = 0.0f,  IsFlying = false },
            new CreatureTemplate { Name = "Elk",    Symbol = 'E', Color = 0x884422, Diet = DietType.Herbivore, Hp = 20, Damage = 2, Armor = 2, Speed = 3, SightRange = 14.0f, FleeRange = 10.0f, HuntRange = 0.0f,  IsFlying = false },
            // Carnivores
            new CreatureTemplate { Name = "Wolf",   Symbol = 'w', Color = 0x444488, Diet = DietType.Carnivore, Hp = 12, Damage = 4, Armor = 1, Speed = 1, SightRange = 15.0f, FleeRange = 3.0f,  HuntRange = 12.0f, IsFlying = false },
            new CreatureTemplate { Name = "Bear",   Symbol = 'B', Color = 0x663333, Diet = DietType.Carnivore, Hp = 25, Damage = 5, Armor = 2, Speed = 3, SightRange = 12.0f, FleeRange = 2.0f,  HuntRange = 10.0f, IsFlying = false },
            new CreatureTemplate { Name = "Fox",    Symbol = 'f', Color = 0xff9944, Diet = DietType.Carnivore, Hp = 6,  Damage = 3, Armor = 0, Speed = 1, SightRange = 13.0f, FleeRange = 4.0f,  HuntRange = 11.0f, IsFlying = false },
            // Birds
            new CreatureTemplate { Name = "Raven",  Symbol = 'R', Color = 0x333333, Diet = DietType.Omnivore, Hp = 4,  Damage = 1, Armor = 0, Speed = 1, SightRange = 16.0f, FleeRange = 6.0f,  HuntRange = 8.0f,  IsFlying = true },
            new CreatureTemplate { Name = "Eagle",  Symbol = 'e', Color = 0xffaa44, Diet = DietType.Carnivore, Hp = 10, Damage = 3, Armor = 0, Speed = 2, SightRange = 20.0f, FleeRange = 4.0f,  HuntRange = 18.0f, IsFlying = true },
        };

        public static CreatureTemplate Get(CreatureType type) => CREATURES[(int)type];
    }
}
