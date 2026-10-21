using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>IMGUI HUD: health, stamina, ammo, objective, prompts, crosshair, hit markers, damage vignette,
    /// pause / win / lose screens and an F1 AI debug view (Director decisions + live zombie states).</summary>
    public class GameHUD : MonoBehaviour
    {
        public static GameHUD Instance { get; private set; }

        public PlayerStats stats;
        public PlayerController controller;
        public PlayerWeapon weapon;
        public PlayerInteraction interaction;
        public Director director;
        public Camera cam;

        struct Toast_ { public string text; public float until; }

        readonly List<Toast_> toasts = new List<Toast_>();
        float hitMarkerUntil;
        bool hitHead;
        float damageFlash;
        float startTime;
        Texture2D vignette;
        GUIStyle label, big, small, center, captionStyle;

        /// <summary>The F1 AI debug view (Director decisions + live zombie states). The demo bot turns this on.</summary>
        public bool ShowDebug { get; set; }

        /// <summary>If true, draw zombie floating labels (state/speed/HP) above zombies.
        /// Demo runs with this false to avoid UI clutter; normal play defaults to true.</summary>
        public bool showZombieLabels = true;

        /// <summary>Lower-third caption text; empty = hidden. Cleared automatically once CaptionUntil passes.</summary>
        public string Caption = "";
        /// <summary>Unscaled time until which Caption stays on screen.</summary>
        public float CaptionUntil;

        void Awake()
        {
            Instance = this;
            startTime = Time.time;
        }

        void OnEnable() { AlertSystem.AlertBroadcast += OnAlertBroadcast; }
        void OnDisable() { AlertSystem.AlertBroadcast -= OnAlertBroadcast; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (stats != null) stats.Damaged += OnDamaged;
            BuildVignette();
        }

        void OnAlertBroadcast(AlertEvent e)
        {
            Toast("A Screamer is calling the horde!", 3f);
        }

        void OnDamaged(float amount, Vector3 from)
        {
            damageFlash = Mathf.Clamp01(damageFlash + 0.35f + amount / 60f);
        }

        public void Toast(string text, float seconds)
        {
            Toast_ t = new Toast_();
            t.text = text;
            t.until = Time.unscaledTime + seconds;
            toasts.Add(t);
            if (toasts.Count > 4) toasts.RemoveAt(0);
        }

        public void ShowHitMarker(bool head)
        {
            hitMarkerUntil = Time.unscaledTime + 0.18f;
            hitHead = head;
        }

        /// <summary>Show a lower-third caption for 'seconds' (used by the demo storyboard).</summary>
        public void ShowCaption(string text, float seconds)
        {
            Caption = text ?? "";
            CaptionUntil = Time.unscaledTime + seconds;
        }

        void BuildVignette()
        {
            int n = 128;
            vignette = new Texture2D(n, n, TextureFormat.RGBA32, false);
            vignette.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.SmoothStep(0.35f, 1f, d);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            vignette.SetPixels32(px);
            vignette.Apply();
        }

        void Update()
        {
            damageFlash = Mathf.MoveTowards(damageFlash, 0f, Time.unscaledDeltaTime * 1.4f);
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) ShowDebug = !ShowDebug;
            if (!string.IsNullOrEmpty(Caption) && Time.unscaledTime > CaptionUntil) Caption = "";
            toasts.RemoveAll(t => Time.unscaledTime > t.until);
        }

        void MakeStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label);
            label.fontSize = 22;
            label.fontStyle = FontStyle.Bold;
            label.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            big = new GUIStyle(label);
            big.fontSize = 54;
            small = new GUIStyle(label);
            small.fontSize = 16;
            small.fontStyle = FontStyle.Normal;
            center = new GUIStyle(label);
            center.alignment = TextAnchor.MiddleCenter;
        }

        void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            Color old = style.normal.textColor;
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        void Bar(float x, float y, float w, float h, float fill, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, w + 6f, h + 6f), Texture2D.whiteTexture);
            GUI.color = new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 0.9f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(fill), h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void OnGUI()
        {
            if (stats == null) return;
            MakeStyles();

            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float W = Screen.width / s, H = Screen.height / s;

            GameManager gm = GameManager.Instance;

            // damage + low health vignette
            float low = stats.HealthFraction < 0.35f ? (0.35f - stats.HealthFraction) / 0.35f : 0f;
            float pulse = low > 0f ? (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f)) * low * 0.7f : 0f;
            float a = Mathf.Clamp01(damageFlash * 0.8f + pulse);
            if (vignette != null && a > 0.01f)
            {
                GUI.color = new Color(0.8f, 0f, 0f, a);
                GUI.DrawTexture(new Rect(0, 0, W, H), vignette);
                GUI.color = Color.white;
            }

            // crosshair + hit marker
            float cx = W * 0.5f, cy = H * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 14f, cy - 1f, 8f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 6f, cy - 1f, 8f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 14f, 2f, 8f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy + 6f, 2f, 8f), Texture2D.whiteTexture);
            if (Time.unscaledTime < hitMarkerUntil)
            {
                GUI.color = hitHead ? new Color(1f, 0.25f, 0.2f) : Color.white;
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
                GUI.DrawTexture(new Rect(cx - 12f, cy - 1.5f, 24f, 3f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 1.5f, cy - 12f, 3f, 24f), Texture2D.whiteTexture);
                GUI.matrix = m;
            }
            GUI.color = Color.white;

            // health + stamina
            Color hc = Color.Lerp(new Color(0.9f, 0.1f, 0.1f), new Color(0.25f, 0.85f, 0.3f), stats.HealthFraction);
            Shadowed(new Rect(40f, H - 118f, 300f, 30f), "HEALTH  " + Mathf.CeilToInt(stats.health), label, Color.white);
            Bar(40f, H - 80f, 380f, 26f, stats.HealthFraction, hc);
            if (controller != null) Bar(40f, H - 42f, 380f, 8f, controller.Stamina / controller.maxStamina, new Color(0.9f, 0.8f, 0.3f));

            // ammo
            string ammoText = stats.ammo + " / " + stats.reserveAmmo;
            Color ac = stats.ammo == 0 ? new Color(1f, 0.3f, 0.2f) : Color.white;
            Rect ar = new Rect(W - 440f, H - 130f, 400f, 70f);
            big.alignment = TextAnchor.MiddleRight;
            Shadowed(ar, ammoText, big, ac);
            label.alignment = TextAnchor.MiddleRight;
            Shadowed(new Rect(W - 440f, H - 62f, 400f, 28f), "[G] Grenades " + stats.grenades + "    [Q] Bottles " + stats.bottles, label, new Color(0.8f, 0.85f, 0.8f));
            if (weapon != null && weapon.Reloading)
            {
                Shadowed(new Rect(W * 0.5f - 150f, H * 0.5f + 60f, 300f, 30f), "RELOADING", center, new Color(1f, 0.85f, 0.3f));
                Bar(W * 0.5f - 100f, H * 0.5f + 96f, 200f, 6f, weapon.ReloadProgress, new Color(1f, 0.85f, 0.3f));
            }
            label.alignment = TextAnchor.UpperLeft;
            big.alignment = TextAnchor.UpperLeft;

            // objective
            if (gm != null)
            {
                int left = Mathf.Max(0, gm.killsRequired - gm.Kills);
                Shadowed(new Rect(W * 0.5f - 300f, 24f, 600f, 34f), "CLEAR THE BUILDING", center, new Color(0.95f, 0.85f, 0.5f));
                // during the 3-minute demo the quota is set impossibly high on purpose (no win screen), so
                // show a plain counter instead of "3 / 999"
                string killed = gm.killsRequired >= 900 ? ("Infected killed  " + gm.Kills)
                                                        : ("Infected killed  " + gm.Kills + " / " + gm.killsRequired);
                Shadowed(new Rect(W * 0.5f - 300f, 56f, 600f, 34f), killed, center, Color.white);
            }

            // interaction prompt
            if (interaction != null && !string.IsNullOrEmpty(interaction.Prompt))
                Shadowed(new Rect(W * 0.5f - 300f, H * 0.5f + 140f, 600f, 34f), interaction.Prompt, center, new Color(1f, 1f, 0.7f));

            // toasts
            for (int i = 0; i < toasts.Count; i++)
                Shadowed(new Rect(W * 0.5f - 400f, 110f + i * 30f, 800f, 30f), toasts[i].text, center, new Color(1f, 0.9f, 0.6f));

            // controls (first seconds)
            if (Time.time - startTime < 14f && gm != null && gm.state == GameManager.State.Playing)
                DrawControls(W, H);

            // demo caption (lower third) - drawn above the HUD, below the debug view
            if (!string.IsNullOrEmpty(Caption) && Time.unscaledTime < CaptionUntil)
            {
                if (DemoBot.showCaptions) DrawCaption(W, H);
            }

            if (ShowDebug) DrawDebug(W, H);

            if (gm != null)
            {
                if (gm.state == GameManager.State.Paused) DrawOverlay(W, H, "PAUSED", "Press ESC to resume   |   R to restart", new Color(0.9f, 0.9f, 0.9f), true);
                else if (gm.state == GameManager.State.Lost) DrawOverlay(W, H, "YOU DIED", "Killed: " + gm.Kills + " / " + gm.killsRequired + "     Press R to restart", new Color(0.85f, 0.1f, 0.1f), false);
                else if (gm.state == GameManager.State.Won) DrawOverlay(W, H, "BUILDING CLEARED", "Time " + FormatTime(gm.ElapsedTime) + "   Headshots " + gm.Headshots + "     Press R to play again", new Color(0.4f, 0.9f, 0.45f), false);
            }
            GUI.matrix = Matrix4x4.identity;
        }

        static string FormatTime(float t)
        {
            int m = Mathf.FloorToInt(t / 60f);
            int sec = Mathf.FloorToInt(t % 60f);
            return m + ":" + sec.ToString("00");
        }

        /// <summary>Lower-third caption bar for the demo storyboard: 1320x78 semi-transparent bar with
        /// 40px bold shadowed text (drawn in the 1080-height coordinate space the HUD already uses).</summary>
        void DrawCaption(float W, float H)
        {
            if (captionStyle == null)
            {
                captionStyle = new GUIStyle(label);
                captionStyle.fontSize = 40;                 // >= 36px so it stays readable in the recording
                captionStyle.alignment = TextAnchor.MiddleCenter;
            }

            float bw = 1320f, bh = 78f;
            Rect bar = new Rect(W * 0.5f - bw * 0.5f, H - 336f, bw, bh);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);      // semi-transparent backing
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0.75f, 0.25f, 0.9f);
            GUI.DrawTexture(new Rect(bar.x, bar.yMax - 4f, bw, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Shadowed(bar, Caption, captionStyle, Color.white);
        }

        void DrawControls(float W, float H)
        {
            string text = "WASD move   SHIFT sprint   SPACE jump   MOUSE look   LMB shoot   R reload\n" +
                          "E open / close doors   F flashlight   Q throw bottle (distraction)   G throw grenade\n" +
                          "Walk into barricades to push them   ESC pause   F1 AI decision log";
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(W * 0.5f - 560f, H - 250f, 1120f, 110f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            small.alignment = TextAnchor.MiddleCenter;
            Shadowed(new Rect(W * 0.5f - 560f, H - 250f, 1120f, 110f), text, small, new Color(0.9f, 0.9f, 0.9f));
            small.alignment = TextAnchor.UpperLeft;
        }

        void DrawOverlay(float W, float H, string title, string sub, Color color, bool lightDim)
        {
            GUI.color = new Color(0f, 0f, 0f, lightDim ? 0.55f : 0.7f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
            big.alignment = TextAnchor.MiddleCenter;
            Shadowed(new Rect(0, H * 0.35f, W, 100f), title, big, color);
            big.alignment = TextAnchor.UpperLeft;
            Shadowed(new Rect(0, H * 0.35f + 100f, W, 40f), sub, center, Color.white);
            if (lightDim) DrawControls(W, H + 140f);
        }

        void DrawDebug(float W, float H)
        {
            // Director panel
            float x = 30f, y = 120f;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x - 10f, y - 10f, 760f, 300f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            small.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(x, y, 740f, 22f), "AI DECISION LOG", small);
            small.fontStyle = FontStyle.Normal;
            y += 24f;
            if (director != null)
            {
                GUI.Label(new Rect(x, y, 740f, 22f),
                    "Director  intensity " + director.Intensity.ToString("0.00") + "  spawned " + director.SpawnedTotal + "/" + director.totalToSpawn +
                    "  alive " + ZombieHealth.Alive.Count + "  camping " + director.StayTime.ToString("0") + "s", small);
                y += 22f;
                for (int i = 0; i < director.Log.Count; i++)
                {
                    GUI.Label(new Rect(x, y, 740f, 20f), director.Log[i], small);
                    y += 20f;
                }
            }

            // live labels above zombies
            Camera c = cam != null ? cam : Camera.main;
            if (c == null) return;
            if (!showZombieLabels) return;
            for (int i = 0; i < ZombieHealth.Alive.Count; i++)
            {
                ZombieHealth z = ZombieHealth.Alive[i];
                if (z == null) continue;
                Vector3 sp = c.WorldToScreenPoint(z.transform.position + Vector3.up * 2.3f);
                if (sp.z <= 0f) continue;
                float s = Screen.height / 1080f;
                string info = z.kind.ToString();
                RunnerBrain rb = z.GetComponent<RunnerBrain>();
                TankBrain tb = z.GetComponent<TankBrain>();
                ScreamerBrain sb = z.GetComponent<ScreamerBrain>();
                if (rb != null) info = rb.DebugInfo;
                else if (tb != null) info = tb.DebugInfo;
                else if (sb != null) info = "Screamer  " + sb.CurrentAction + (sb.IsHidden ? " (hidden)" : "");
                info += "  HP " + Mathf.CeilToInt(z.health);
                float px = sp.x / s, py = (Screen.height - sp.y) / s;
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(px - 190f, py - 12f, 380f, 22f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                small.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(px - 190f, py - 12f, 380f, 22f), info, small);
                small.alignment = TextAnchor.UpperLeft;
            }
        }
    }
}
