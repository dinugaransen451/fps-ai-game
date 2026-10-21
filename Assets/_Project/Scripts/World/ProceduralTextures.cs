using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieGame
{
    public enum SurfaceKind { Concrete, Plaster, Tiles, CeilingTile, Wood, Metal, Hazard, Carpet, Blood, BulletHole }

    /// <summary>Tileable procedural textures (no external art needed): the World Builder's texturing pass.</summary>
    public static class ProceduralTextures
    {
        const int Size = 256;
        static readonly Dictionary<SurfaceKind, Texture2D> cache = new Dictionary<SurfaceKind, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); }

        public static Texture2D Get(SurfaceKind kind)
        {
            Texture2D t;
            if (cache.TryGetValue(kind, out t) && t != null) return t;
            t = Generate(kind);
            t.name = "T_" + kind;
            cache[kind] = t;
            return t;
        }

        // ---------------------------------------------------------------- noise
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 69069;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        static float ValueNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float fx = x - xi;
            float fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int x0 = Mod(xi, period), x1 = Mod(xi + 1, period);
            int y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
            float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>Tileable fractal noise, u and v in 0..1, returns 0..1.</summary>
        static float Fbm(float u, float v, int baseFreq, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * freq, v * freq, freq, seed + o * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2;
            }
            return sum / norm;
        }

        static Color32 C(float r, float g, float b, float a = 1f)
        {
            return new Color32(
                (byte)(Mathf.Clamp01(r) * 255f), (byte)(Mathf.Clamp01(g) * 255f),
                (byte)(Mathf.Clamp01(b) * 255f), (byte)(Mathf.Clamp01(a) * 255f));
        }

        // ---------------------------------------------------------------- generators
        static Texture2D Generate(SurfaceKind kind)
        {
            Color32[] px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size;
                    float v = (y + 0.5f) / Size;
                    px[y * Size + x] = Pixel(kind, x, y, u, v);
                }
            }

            bool decal = kind == SurfaceKind.Blood || kind == SurfaceKind.BulletHole;
            Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            tex.wrapMode = decal ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        static Color32 Pixel(SurfaceKind kind, int x, int y, float u, float v)
        {
            switch (kind)
            {
                case SurfaceKind.Concrete:
                {
                    float n = Fbm(u, v, 4, 4, 3);
                    float streak = Fbm(u, v, 6, 3, 9) * Fbm(u * 1f, v, 2, 2, 5);
                    float g = 0.34f + n * 0.30f - streak * 0.10f;
                    // panel joints (2x2 panels per tile)
                    int px = x % 128, py = y % 128;
                    if (px < 2 || py < 2) g *= 0.55f;
                    return C(g * 1.0f, g * 1.02f, g * 0.97f);
                }
                case SurfaceKind.Plaster:
                {
                    float n = Fbm(u, v, 3, 4, 21);
                    float stain = Fbm(u, v, 5, 3, 33);
                    float g = 0.50f + n * 0.20f;
                    float s = Mathf.SmoothStep(0.55f, 0.85f, stain) * 0.28f;
                    return C(g * 0.96f - s, g * 1.00f - s, g * 0.90f - s * 1.1f);
                }
                case SurfaceKind.Tiles:
                {
                    int tx = x / 64, ty = y / 64;
                    int lx = x % 64, ly = y % 64;
                    float baseG = ((tx + ty) % 2 == 0) ? 0.40f : 0.32f;
                    float rnd = Hash(tx, ty, 77) * 0.06f;
                    float dirt = Fbm(u, v, 8, 4, 14);
                    float g = baseG + rnd + (dirt - 0.5f) * 0.18f;
                    if (lx < 2 || ly < 2) g = 0.10f + dirt * 0.05f;
                    return C(g * 0.95f, g, g * 0.96f);
                }
                case SurfaceKind.CeilingTile:
                {
                    int lx = x % 64, ly = y % 64;
                    float n = Fbm(u, v, 6, 3, 41);
                    float g = 0.62f + n * 0.12f;
                    if (lx < 2 || ly < 2) g = 0.25f;
                    else if (((lx / 8) + (ly / 8)) % 2 == 0 && lx % 8 == 4 && ly % 8 == 4) g *= 0.6f;
                    return C(g, g, g * 0.94f);
                }
                case SurfaceKind.Wood:
                {
                    int plank = y / 32;
                    float grain = Fbm(u * 1f, v, 2, 3, 51 + plank) * 0.5f + ValueNoise(u * 24f, plank * 3.1f, 24, 7) * 0.5f;
                    float g = 0.28f + grain * 0.22f + Hash(plank, 3, 5) * 0.06f;
                    if (y % 32 < 2) g *= 0.4f;
                    return C(g * 1.15f, g * 0.80f, g * 0.50f);
                }
                case SurfaceKind.Metal:
                {
                    float brush = ValueNoise(u * 4f, v * 96f, 96, 61) * 0.5f + Fbm(u, v, 4, 3, 63) * 0.5f;
                    float rust = Mathf.SmoothStep(0.62f, 0.9f, Fbm(u, v, 3, 4, 81));
                    float g = 0.30f + brush * 0.18f;
                    return C(g * (1f + rust * 0.9f), g * (1f - rust * 0.25f), g * (1f - rust * 0.7f));
                }
                case SurfaceKind.Hazard:
                {
                    float d = ((x + y) % 64) / 64f;
                    bool yellow = d < 0.5f;
                    float n = Fbm(u, v, 6, 3, 91) * 0.25f;
                    return yellow ? C(0.75f - n, 0.60f - n, 0.05f) : C(0.07f, 0.07f, 0.07f);
                }
                case SurfaceKind.Carpet:
                {
                    float n = ValueNoise(u * 64f, v * 64f, 64, 101) * 0.5f + Fbm(u, v, 8, 3, 103) * 0.5f;
                    float g = 0.16f + n * 0.14f;
                    return C(g * 1.25f, g * 0.55f, g * 0.55f);
                }
                case SurfaceKind.Blood:
                {
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float wob = Fbm(u, v, 4, 4, 121);
                    float blob = 1f - r * 1.15f + (wob - 0.5f) * 0.9f;
                    // satellite droplets
                    float drops = Fbm(u, v, 12, 2, 131);
                    if (blob < 0f && drops > 0.72f && r < 0.95f) blob = 0.1f;
                    float a = blob > 0.05f ? 1f : 0f;
                    float shade = 0.25f + wob * 0.25f;
                    return C(shade * 1.0f, 0.015f, 0.015f, a);
                }
                default: // BulletHole
                {
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float wob = Fbm(u, v, 6, 2, 141);
                    float a = (r < 0.28f + (wob - 0.5f) * 0.12f) ? 1f : 0f;
                    return C(0.03f, 0.03f, 0.03f, a);
                }
            }
        }
    }

    /// <summary>Creates URP materials at runtime from the Resources/BaseLit material (so the shader is included in builds).</summary>
    public static class GameMaterials
    {
        static Material baseLit;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); baseLit = null; }

        static Material Template()
        {
            if (baseLit != null) return baseLit;
            baseLit = Resources.Load<Material>("BaseLit");
            if (baseLit == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s == null) s = Shader.Find("Standard");
                if (s == null) s = Shader.Find("Sprites/Default");
                baseLit = new Material(s);
                baseLit.name = "BaseLit_Runtime";
            }
            return baseLit;
        }

        static Material NewMat(string name, Color color, float smooth, float metal)
        {
            Material m = new Material(Template());
            m.name = name;
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }

        /// <summary>Textured surface; tiling is in repeats across the face.</summary>
        public static Material Surface(SurfaceKind kind, float tileU, float tileV)
        {
            tileU = Mathf.Max(0.5f, Mathf.Round(tileU * 2f) / 2f);
            tileV = Mathf.Max(0.5f, Mathf.Round(tileV * 2f) / 2f);
            string key = "S_" + kind + "_" + tileU + "_" + tileV;
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;

            float smooth = 0.12f, metal = 0f;
            if (kind == SurfaceKind.Tiles) smooth = 0.35f;
            if (kind == SurfaceKind.Metal) { smooth = 0.45f; metal = 0.55f; }
            m = NewMat("M_" + kind + "_" + tileU + "x" + tileV, Color.white, smooth, metal);
            Texture2D tex = ProceduralTextures.Get(kind);
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetTextureScale("_BaseMap", new Vector2(tileU, tileV));
            m.SetTextureScale("_MainTex", new Vector2(tileU, tileV));
            cache[key] = m;
            return m;
        }

        public static Material Solid(Color color, float smooth = 0.2f, float metal = 0f)
        {
            string key = "C_" + ColorUtility.ToHtmlStringRGB(color) + "_" + smooth + "_" + metal;
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = NewMat("M_Solid_" + ColorUtility.ToHtmlStringRGB(color), color, smooth, metal);
            cache[key] = m;
            return m;
        }

        public static Material Emissive(Color color, float intensity = 2f)
        {
            string key = "E_" + ColorUtility.ToHtmlStringRGB(color) + "_" + intensity;
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = NewMat("M_Emissive_" + ColorUtility.ToHtmlStringRGB(color), color * 0.25f, 0.2f, 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            cache[key] = m;
            return m;
        }

        /// <summary>Alpha-clipped decal material (blood, bullet holes).</summary>
        public static Material Decal(SurfaceKind kind)
        {
            string key = "D_" + kind;
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = NewMat("M_Decal_" + kind, Color.white, 0.5f, 0f);
            Texture2D tex = ProceduralTextures.Get(kind);
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            cache[key] = m;
            return m;
        }
    }
}
