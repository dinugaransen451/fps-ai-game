using UnityEngine;
using UnityEngine.InputSystem;

namespace ScreamerAI.TestHarness
{
    /// <summary>
    /// On-screen Utility AI inspector. Shows every action's score, the winning action, and (per action)
    /// each consideration - ideal for demonstrating the decision logic in the viva.
    /// Press H to hide / show.
    /// </summary>
    public class ScreamerDebugOverlay : MonoBehaviour
    {
        public bool visible = true;
        public bool showWorldLabels = true;

        ScreamerBrain[] brains = new ScreamerBrain[0];
        ZombieAlertReceiver[] receivers = new ZombieAlertReceiver[0];
        float nextRefresh;
        GUIStyle label;
        GUIStyle small;
        GUIStyle title;
        Camera cam;

        static readonly Color[] ActionColors =
        {
            new Color(0.55f, 0.75f, 0.55f),   // wander
            new Color(1.00f, 0.35f, 0.25f),   // scream
            new Color(1.00f, 0.80f, 0.25f),   // flee
            new Color(0.40f, 0.70f, 1.00f)    // hide
        };

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) visible = !visible;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 1f;
                brains = FindObjectsByType<ScreamerBrain>();
                receivers = FindObjectsByType<ZombieAlertReceiver>();
            }
        }

        void EnsureStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
            small.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
            title.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            if (!visible) return;
            EnsureStyles();
            if (cam == null) cam = Camera.main;

            float x = 12f;
            float y = 12f;
            float w = 330f;

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 6f, y - 6f, w + 12f, 150f + brains.Length * 190f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(x, y, w, 22f), "SCREAMER - Utility AI", title);
            y += 24f;
            GUI.Label(new Rect(x, y, w, 40f), "WASD move | Shift sprint | F flashlight | H hide HUD | Esc free cursor", small);
            y += 32f;

            for (int b = 0; b < brains.Length; b++)
            {
                ScreamerBrain brain = brains[b];
                if (brain == null || brain.Scores == null) continue;

                Color ac = ActionColors[(int)brain.CurrentAction];
                string hex = ColorUtility.ToHtmlStringRGB(ac);
                GUI.Label(new Rect(x, y, w, 20f), "Action: <b><color=#" + hex + ">" + brain.CurrentAction.ToString().ToUpper() + "</color></b>"
                    + (brain.IsScreaming ? "  (screaming)" : "") + (brain.IsHidden ? "  (hidden)" : ""), label);
                y += 20f;
                GUI.Label(new Rect(x, y, w, 20f),
                    "sees: " + (brain.SeesPlayer ? "YES" : "no") + "   threat: " + brain.Threat.ToString("F2")
                    + "   dist: " + (brain.PerceivedDistance > 900f ? "-" : brain.PerceivedDistance.ToString("F1") + "m"), small);
                y += 17f;
                GUI.Label(new Rect(x, y, w, 20f),
                    "allies in earshot: " + brain.AlliesInEarshot + "   scream cooldown: " + brain.ScreamCooldownRemaining.ToString("F1") + "s"
                    + (brain.LastScreamHeardBy > 0 ? "   last heard by " + brain.LastScreamHeardBy : ""), small);
                y += 22f;

                for (int i = 0; i < brain.Scores.Length; i++)
                {
                    ActionScore sc = brain.Scores[i];
                    bool current = sc.Action == brain.CurrentAction;
                    GUI.Label(new Rect(x, y, 70f, 18f), sc.Action.ToString(), current ? label : small);

                    Rect bar = new Rect(x + 72f, y + 3f, 180f, 12f);
                    GUI.color = new Color(1f, 1f, 1f, 0.15f);
                    GUI.DrawTexture(bar, Texture2D.whiteTexture);
                    GUI.color = ActionColors[i];
                    GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(sc.Score), bar.height), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(x + 258f, y, 70f, 18f), sc.Score.ToString("F2"), small);
                    y += 19f;

                    if (current && sc.Considerations.Count > 0)
                    {
                        string line = "";
                        for (int k = 0; k < sc.Considerations.Count; k++)
                            line += sc.Considerations[k].Label + " " + sc.Considerations[k].Output.ToString("F2") + (k < sc.Considerations.Count - 1 ? "  x  " : "");
                        GUI.Label(new Rect(x + 12f, y - 2f, w - 12f, 34f), "<i>" + line + "</i>", small);
                        y += 26f;
                    }
                }
                y += 10f;
            }

            if (showWorldLabels && cam != null) DrawWorldLabels();
        }

        void DrawWorldLabels()
        {
            for (int i = 0; i < brains.Length; i++)
            {
                if (brains[i] == null) continue;
                string t = "SCREAMER\n" + brains[i].CurrentAction.ToString().ToUpper();
                DrawLabel(brains[i].transform.position + Vector3.up * 2.4f, t, ActionColors[(int)brains[i].CurrentAction]);
            }
            for (int i = 0; i < receivers.Length; i++)
            {
                if (receivers[i] == null) continue;
                Color col = receivers[i].IsAlerted ? new Color(1f, 0.4f, 0.3f) : new Color(0.7f, 0.9f, 0.7f);
                DrawLabel(receivers[i].transform.position + Vector3.up * 2.3f, "zombie\n" + receivers[i].CurrentState, col);
            }
        }

        void DrawLabel(Vector3 world, string text, Color color)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f || sp.z > 45f) return;
            Rect r = new Rect(sp.x - 50f, Screen.height - sp.y - 20f, 100f, 40f);
            GUIStyle st = new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            st.normal.textColor = color;
            GUI.Label(r, text, st);
        }
    }
}
