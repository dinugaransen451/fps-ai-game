using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>
    /// Extension point for anything that wants to drive the player instead of the DemoBot
    /// (a replay system, a cut-scene controller, ...). Assign it to DemoBot.driver before
    /// the storyboard starts and Tick() is forwarded to it every frame.
    /// </summary>
    public interface IPlayerDriver
    {
        void Tick(PlayerController pc);
    }

    /// <summary>
    /// DEMO BOT: an autonomous "player" used to record the 3-minute showcase.
    /// It is spawned automatically (see AutoSpawn) when the editor menu
    /// "Tools > Zombie Game > Demo Bot Enabled" was used before pressing Play.
    ///
    /// It walks a scripted tour of the level, fights like a cautious human (aim smoothing,
    /// burst fire, reloads, bottles and a grenade), follows a 180-second storyboard that
    /// introduces every AI agent (Director / Tank / Screamer / Runner) and keeps the run
    /// deterministic and un-killable: fixed random seed, scripted health + ammo drain,
    /// an emergency revive, and a kill quota so high the game can never end early.
    ///
    /// Everything it does is timestamped in the console and mirrored in the F1 debug panel.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class DemoBot : MonoBehaviour, IPlayerDriver
    {
        // ------------------------------------------------------------------ statics
        /// <summary>Set from a build script (or left alone in the editor, where SessionState is used).</summary>
        public static bool Enabled;
        /// <summary>True from the moment the bot has the player until the storyboard ends (the recorder waits on it).</summary>
        public static bool Ready { get; private set; }
        public static DemoBot Instance { get; private set; }
        /// <summary>Lifts the ambient light a little so the 1080p recording is not pitch black.</summary>
        public static bool brightenForRecording = true;
        /// <summary>When true (default), DemoBot calls GameHUD.ShowCaption for storyboard captions.
        /// Set to false to produce a clean recording (no on-screen text). Console logs remain.</summary>
        public static bool showCaptions = true;
        public const float DemoDuration = 180f;
        const string EnabledKey = "ZombieGame.DemoBot.Enabled";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnLoad() { Ready = false; Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
#if UNITY_EDITOR
            bool on = UnityEditor.SessionState.GetBool(EnabledKey, false);
#else
            bool on = Enabled;
#endif
            if (!on) return;
            if (FindAnyObjectByType<DemoBot>() != null) return;
            GameObject go = new GameObject("DemoBot");
            go.AddComponent<DemoBot>();
            Debug.Log("[DemoBot] spawned (demo enabled)");
        }

        // ------------------------------------------------------------------ storyboard data
        /// <summary>One event of the 180 s storyboard: fires at 'time' seconds after the bot took over.</summary>
        public class DemoEvent
        {
            public float time;
            public string action;      // machine action id (see Fire)
            public string caption;     // lower-third text, null = keep the previous caption

            public DemoEvent(float t, string a, string c) { time = t; action = a; caption = c; }
        }

        /// <summary>The 180-second script. Public so a lecturer can tweak timings before a recording.</summary>
        public readonly List<DemoEvent> storyboard = new List<DemoEvent>();

        /// <summary>Alternative driver (see IPlayerDriver). Null = the bot itself drives the player.</summary>
        public IPlayerDriver driver;
        /// <summary>Hand control back to a human that presses a key or moves the mouse (F9 returns it).</summary>
        public bool yieldToHuman = true;

        enum Phase { Countdown, Tour, PushBarricade, Retreat, Standoff, Outro }

        // ------------------------------------------------------------------ wiring
        PlayerController pc;
        PlayerStats stats;
        PlayerWeapon weapon;
        PlayerInteraction interaction;
        GameHUD hud;
        Director director;
        GameManager gm;
        DemoCamera demoCam;
        int originalKillsRequired = 20;

        Phase phase = Phase.Countdown;
        float startTime;
        float phaseStart;
        bool manualTakeover;
        bool ending;

        // navigation
        readonly List<Vector3> path = new List<Vector3>();
        Vector3 navTarget;
        int pathIndex;
        float nextPathTime;
        Vector3 lastStuckPos;
        float nextStuckCheck = 4f;
        int skipToRouteIndex = -1;

        // combat
        float nextCombat;
        ZombieHealth aimTarget;         // visible zombie we are pointing the gun at (null = none)
        ZombieHealth screamer;          // the demo Screamer - it is never shot, only aimed at
        Vector3 aimPoint;
        float nextBottle;
        bool grenadeUsed;
        float prevTankDist = 999f;
        float tankClosingSince = -1f;
        bool tankHunting;

        // doors
        Door[] doors;
        float nextDoorCheck;

        // scripted spawns
        struct PendingSpawn { public ZombieKind kind; public string filter; public float nextTry; public float giveUp; }
        PendingSpawn pending;
        bool hasPending;

        // barricade segment
        Barricade barricade;
        float phaseT;
        float nextNoise;

        // ------------------------------------------------------------------ level tour
        // Waypoints the bot cycles through. The NavMesh supplies the exact heights (ramp, both floors);
        // doors are opened when the bot gets close, barricades are pushed, zombies interrupt the walk.
        static readonly Vector3[] Route = new Vector3[]
        {
            new Vector3(6f, 0f, 9.6f),      // room A, approaching door A (6,12)
            new Vector3(6f, 0f, 14f),       // corridor (west)
            new Vector3(3f, 0f, 14f),       // corridor west end - turn around
            new Vector3(14f, 0f, 14f),      // corridor
            new Vector3(18f, 0f, 8f),       // office through door B (18,12)
            new Vector3(18f, 0f, 14.5f),    // back to the corridor
            new Vector3(30f, 0f, 14f),      // east corridor
            new Vector3(35f, 0f, 17.4f),    // stair hall (opening at x=35 in the corridor wall)
            new Vector3(35f, 0f, 20f),      // on the ramp (rises z=18 -> z=26)
            new Vector3(35f, 4f, 26.3f),    // ramp top, floor 2
            new Vector3(32f, 4f, 27.2f),    // floor 2, clear of the stairwell hole
            new Vector3(35f, 4f, 17.2f),    // floor 2 corridor through the stair-hall opening
            new Vector3(22f, 4f, 14f),      // floor 2 corridor (door E2 at 22,16)
            new Vector3(8f, 4f, 14f),       // floor 2 corridor west
            new Vector3(3f, 4f, 14f),       // turn
            new Vector3(30f, 4f, 14f),      // back east
            new Vector3(31f, 4f, 17.2f),    // floor 2 room F (west of the railing)
            new Vector3(31f, 4f, 26.8f),    // floor 2 north, beside the stairwell hole
            new Vector3(34.8f, 4f, 26.4f),  // ramp top, ready to go down
            new Vector3(35f, 0f, 18.4f),    // bottom of the ramp, floor 1
            new Vector3(33f, 0f, 14f),      // corridor - loop restarts
        };
        int routeIndex;

        // ------------------------------------------------------------------ lifecycle
        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (stats != null) stats.Died -= OnDied;
            Ready = false;
        }

        void Start()
        {
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            // wait for the game (GameBootstrap makes the player, then NavGrid a couple of frames later)
            float wait0 = Time.unscaledTime;
            while ((PlayerController.Instance == null || GameManager.Instance == null || NavGrid.Instance == null)
                   && Time.unscaledTime - wait0 < 25f)
                yield return null;

            pc = PlayerController.Instance;
            stats = PlayerStats.Instance;
            gm = GameManager.Instance;
            hud = GameHUD.Instance;
            director = FindAnyObjectByType<Director>();
            if (pc == null || stats == null || gm == null || director == null)
            {
                Debug.LogError("[DemoBot] could not find the game objects - demo aborted.");
                yield break;
            }
            weapon = pc.GetComponent<PlayerWeapon>();
            interaction = pc.GetComponent<PlayerInteraction>();
            doors = FindObjectsByType<Door>();

            Random.InitState(1234);                    // deterministic demo, every run
            Time.timeScale = 1f;
            BuildStoryboard();

            // hand the player over to the bot
            pc.externalControl = true;
            if (weapon != null) weapon.externalInput = true;
            if (hud != null) { hud.ShowDebug = true; hud.Caption = ""; }
            pc.SetCursorLocked(true);

            // the camera director takes over rendering
            demoCam = gameObject.AddComponent<DemoCamera>();

            // a touch more ambient light so the recording reads well (Task 5)
            if (brightenForRecording)
                RenderSettings.ambientLight = new Color(0.09f, 0.095f, 0.105f);

            // keep the run going: the Director must not flood the storyboard, and the
            // kill quota is raised so high the game can never show a win screen
            director.demoPacingOverride = 14f;
            originalKillsRequired = gm.killsRequired;
            gm.killsRequired = 999;
            stats.Died += OnDied;

            Random.InitState(1234);                    // deterministic demo, every run
            Time.timeScale = 1f;

            // hand the player over to the bot
            pc.externalControl = true;
            if (weapon != null) weapon.externalInput = true;
            if (hud != null) { hud.ShowDebug = true; hud.showZombieLabels = true; hud.Caption = ""; }
            pc.SetCursorLocked(true);

            // the camera director takes over rendering
            demoCam = gameObject.AddComponent<DemoCamera>();

            // a touch more ambient light so the recording reads well (Task 5)
            if (brightenForRecording)
                RenderSettings.ambientLight = new Color(0.09f, 0.095f, 0.105f);
            phaseStart = startTime;
            lastStuckPos = pc.transform.position;
            Ready = true;
            Log("demo bot ready - 180 s storyboard starting");

            int next = 0;
            while (Time.time - startTime < DemoDuration && !ending)
            {
                float t = Time.time - startTime;
                while (next < storyboard.Count && t >= storyboard[next].time)
                {
                    float dur = (next + 1 < storyboard.Count ? storyboard[next + 1].time : DemoDuration) - storyboard[next].time;
                    Fire(storyboard[next], dur);
                    next++;
                }

                try
                {
                    if (driver != null) driver.Tick(pc);
                    else Tick(pc);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);            // never let one bad frame kill the recording
                }
                yield return null;
            }

            Finish();
        }

        void Finish()
        {
            if (ending) return;
            ending = true;
            pc.externalControl = false;
            if (weapon != null) weapon.externalInput = false;
            if (stats != null) stats.Died -= OnDied;
            if (hud != null) hud.Caption = "";
            if (director != null) director.demoPacingOverride = 0f;
            if (gm != null) gm.killsRequired = originalKillsRequired;   // normal value again for free play
            if (brightenForRecording)
                RenderSettings.ambientLight = new Color(0.05f, 0.055f, 0.065f);
            Ready = false;
            Log("demo finished after " + (Time.time - startTime).ToString("0.0") + " s - control returned to the human");
        }

        void BuildStoryboard()
        {
            storyboard.Clear();
            storyboard.Add(new DemoEvent(0f, "countdown", "Recording starts in 5..."));
            storyboard.Add(new DemoEvent(5f, "intro", "ZombieOutbreak - 4 agents, one autonomous player   |   Agent 1: the DIRECTOR"));
            storyboard.Add(new DemoEvent(15f, "runner", "Agent 4: the RUNNER - hand-written A* over its own nav graph, re-routes around blocked doors"));
            storyboard.Add(new DemoEvent(25f, "runner2", null));
            storyboard.Add(new DemoEvent(55f, "tank", "Agent 2: the TANK - behaviour tree, hears gunshots, smashes barricades and doors"));
            storyboard.Add(new DemoEvent(63f, "barricade", "Physics + Agent 2: barricades are rigidbodies - pushed into a doorway, the Tank breaks through"));
            storyboard.Add(new DemoEvent(95f, "screamer", "Agent 3: the SCREAMER - hides, then screams to call the horde (AlertSystem broadcast)"));
            storyboard.Add(new DemoEvent(135f, "director", "Agent 1: the DIRECTOR - pacing, placement and type choice (live decisions in the F1 panel)"));
            storyboard.Add(new DemoEvent(150f, "outro", "Demo complete - 180 seconds, 4 AI agents, one autonomous player"));
            storyboard.Add(new DemoEvent(176f, "finale", "ZombieOutbreak   |   thanks for watching"));
        }

        // ------------------------------------------------------------------ storyboard dispatch
        void Fire(DemoEvent e, float duration)
        {
            Log("storyboard " + e.action + " (t=" + e.time.ToString("0") + "s)");
            if (!string.IsNullOrEmpty(e.caption) && hud != null && showCaptions)
                hud.ShowCaption(e.caption, duration);

            switch (e.action)
            {
                case "countdown":
                    // countdown happens entirely inside the recording (text hidden when showCaptions=false)
                    phase = Phase.Countdown;
                    if (demoCam != null) demoCam.ForceShot(DemoCamera.ShotKind.Overview, 5.5f);
                    Log("storyboard countdown (t=0s)");
                    break;

                case "intro":
                    SetPhase(Phase.Tour);
                    if (demoCam != null) demoCam.ForceShot(DemoCamera.ShotKind.Chase, 6f);
                    break;

                case "runner":
                    QueueSpawn(ZombieKind.Runner, "Cafe");
                    director.demoPacingOverride = 18f;      // let the Runners be the show
                    break;

                case "runner2":
                    QueueSpawn(ZombieKind.Runner, "Cafe");
                    break;

                case "tank":
                    QueueSpawn(ZombieKind.Tank, "Stairs");
                    director.demoPacingOverride = 16f;
                    break;

                case "barricade":
                    BeginBarricadePush();
                    break;

                case "screamer":
                    QueueSpawn(ZombieKind.Screamer, "Lab");
                    break;

                case "director":
                    SetPhase(Phase.Tour);
                    director.demoPacingOverride = 6f;       // rapid decisions for the showcase
                    break;

                case "outro":
                    SetPhase(Phase.Outro);
                    director.demoPacingOverride = 14f;
                    SetDestination(new Vector3(6f, 0f, 8f));
                    break;

                case "finale":
                    break;                                  // caption only
            }
        }

        void SetPhase(Phase p)
        {
            phase = p;
            phaseStart = Time.time;
            phaseT = 0f;
            nextPathTime = 0f;                              // re-path immediately
        }

        void QueueSpawn(ZombieKind kind, string filter)
        {
            pending.kind = kind;
            pending.filter = filter;
            pending.nextTry = Time.time;
            pending.giveUp = Time.time + 10f;               // retry every 2 s, give up after 10 s
            hasPending = true;
        }

        void BeginBarricadePush()
        {
            SetPhase(Phase.PushBarricade);
            barricade = null;
            Barricade[] all = FindObjectsByType<Barricade>();
            float best = 999f;
            Vector3 spot = new Vector3(27.4f, 0f, 22f);     // the lab doorway barricade on floor 1
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                float d = Vector3.Distance(all[i].transform.position, spot);
                if (d < best) { best = d; barricade = all[i]; }
            }
            nextNoise = Time.time + 1.2f;
        }

        // ------------------------------------------------------------------ per-frame driver
        /// <summary>IPlayerDriver: drives the player one frame (also called by DemoCamera-independent tests).</summary>
        public void Tick(PlayerController player)
        {
            if (player == null || stats == null) return;
            float now = Time.time;
            phaseT = now - phaseStart;

            // the recording keeps rolling - a stray ESC must not freeze it
            if (gm.state == GameManager.State.Paused)
            {
                gm.state = GameManager.State.Playing;
                Time.timeScale = 1f;
                AudioListener.pause = false;
                pc.SetCursorLocked(true);
                Log("auto-resumed (paused by accident during the recording)");
            }

            CheckHumanInput();

            if (now >= nextCombat) { nextCombat = now + 0.2f; CombatScan(now); }
            if (now >= nextDoorCheck) { nextDoorCheck = now + 0.25f; TryOpenDoors(); }
            if (hasPending) TryPendingSpawn(now);
            ScriptedState(now);

            Vector3 dir = Vector3.zero;
            float speed = 0f;

            if (manualTakeover)
            {
                pc.ExternalMove(Vector3.zero, 0f);
                return;
            }

            switch (phase)
            {
                case Phase.Countdown:
                    // stand still for the establishing shot and count down the first 5 seconds
                    if (hud != null)
                    {
                        int n = Mathf.Max(1, Mathf.CeilToInt(5f - (now - startTime)));
                        hud.Caption = "Recording starts in " + n + "...";
                        hud.CaptionUntil = Time.unscaledTime + 0.5f;
                    }
                    break;

                case Phase.PushBarricade:
                    TickPush(now, out dir, out speed);
                    break;

                case Phase.Retreat:
                    TickMove(now, 3.1f, out dir, out speed);
                    if (phaseT > 25f) SetPhase(Phase.Tour);  // safety valve
                    break;

                case Phase.Standoff:
                    TickStandoff(now, out dir, out speed);
                    break;

                case Phase.Outro:
                    TickMove(now, 2.6f, out dir, out speed);
                    break;

                default:
                    TickMove(now, 3.05f, out dir, out speed);   // Tour: walk the route
                    break;
            }

            // a hostile zombie closer than 14 m interrupts whatever the bot is doing
            if (phase != Phase.Countdown && phase != Phase.Standoff && phase != Phase.PushBarricade && aimTarget != null)
            {
                float d = Vector3.Distance(pc.transform.position, aimTarget.transform.position);
                if (d < 14f)
                {
                    Vector3 away = pc.transform.position - aimTarget.transform.position;
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.back;
                    if (d < 7f) { dir = away; speed = 1.9f; }                       // back off while firing
                    else { dir = Vector3.Cross(away, Vector3.up) * Mathf.Sign(Mathf.Sin(now * 0.6f)); speed = 1.2f; }
                }
            }

            player.ExternalMove(dir, speed);
            UpdateLook(now, dir);
        }

        // ------------------------------------------------------------------ movement
        void TickMove(float now, float walkSpeed, out Vector3 dir, out float speed)
        {
            dir = Vector3.zero;
            speed = 0f;
            if (routeIndex >= Route.Length) routeIndex = 0;
            if (skipToRouteIndex >= 0) { routeIndex = skipToRouteIndex; skipToRouteIndex = -1; nextPathTime = 0f; }

            navTarget = Route[routeIndex];
            if (now >= nextPathTime)
            {
                SetDestination(navTarget);
                nextPathTime = now + 1.2f;
            }

            bool arrived;
            dir = Steer(out arrived, 0.9f);
            speed = dir.sqrMagnitude > 0.001f ? walkSpeed : 0f;
            if (arrived) { routeIndex = (routeIndex + 1) % Route.Length; nextPathTime = 0f; }

            CheckStuck(now);
        }

        void TickPush(float now, out Vector3 dir, out float speed)
        {
            dir = Vector3.zero;
            speed = 0f;

            if (barricade == null) { SetPhase(Phase.Retreat); SetDestination(new Vector3(25.5f, 0f, 14f)); return; }

            // keep walking into the barricade from the west so it slides east through the doorway
            Vector3 target = barricade.transform.position + Vector3.left * 1.35f;
            if (now >= nextPathTime) { SetDestination(target); nextPathTime = now + 0.6f; }

            // lure the Tank: fake gunshot noise on the far side of the barricade
            if (now >= nextNoise)
            {
                nextNoise = now + 1.6f;
                NoiseSystem.Emit(barricade.transform.position + Vector3.right * 2.6f + Vector3.up, 34f, NoiseKind.Gunshot);
            }

            bool arrived;
            dir = Steer(out arrived, 0.4f);
            speed = dir.sqrMagnitude > 0.001f ? 1.55f : 0f;

            if (phaseT > 10f)
            {
                SetPhase(Phase.Retreat);
                SetDestination(new Vector3(25.5f, 0f, 14f));
                Log("barricade pushed - falling back to the corridor so the Tank can finish it");
            }
            CheckStuck(now);
        }

        void TickStandoff(float now, out Vector3 dir, out float speed)
        {
            dir = Vector3.zero;
            speed = 0f;

            if (screamer == null || !screamer.IsAlive)
            {
                if (phaseT > 3f) { SetPhase(Phase.Tour); Log("screamer dealt with - back on the tour"); }
                return;
            }

            Vector3 zpos = screamer.transform.position;
            Vector3 away = pc.transform.position - zpos;
            away.y = 0f;
            float d = away.magnitude;
            away = d > 0.01f ? away / d : Vector3.back;

            // if another zombie gets on top of us, give ground while keeping the Screamer in view
            ZombieHealth close = NearestNonScreamerWithin(4.5f);
            if (close != null)
            {
                Vector3 back = pc.transform.position - close.transform.position;
                back.y = 0f;
                dir = back.sqrMagnitude > 0.001f ? back.normalized : -away;
                speed = 2.2f;
                return;
            }

            // hold about 11 m away, circling slowly
            Vector3 want = zpos + away * 11f + Vector3.Cross(away, Vector3.up) * Mathf.Sin(now * 0.35f) * 3f;
            if (now >= nextPathTime) { SetDestination(want); nextPathTime = now + 0.9f; }

            bool arrived;
            dir = Steer(out arrived, 1.1f);
            speed = dir.sqrMagnitude > 0.001f ? 2.4f : 0f;
            CheckStuck(now);
        }

        bool SetDestination(Vector3 target)
        {
            if (pc == null) return false;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(target, out hit, 3f, NavMesh.AllAreas)) return false;

            NavMeshPath p = new NavMeshPath();
            NavMesh.CalculatePath(pc.transform.position, hit.position, NavMesh.AllAreas, p);
            if (p.corners == null || p.corners.Length == 0) return false;

            path.Clear();
            for (int i = 0; i < p.corners.Length; i++) path.Add(p.corners[i]);
            pathIndex = 0;
            return true;
        }

        Vector3 Steer(out bool arrived, float arriveRadius)
        {
            arrived = false;
            if (path.Count == 0 || pc == null) return Vector3.zero;

            Vector3 pos = pc.transform.position;
            while (pathIndex < path.Count - 1 &&
                   Vector3.Distance(new Vector3(pos.x, path[pathIndex].y, pos.z), path[pathIndex]) < 0.6f)
                pathIndex++;

            Vector3 corner = path[pathIndex];
            Vector3 delta = corner - pos;
            delta.y = 0f;
            if (pathIndex == path.Count - 1 && delta.magnitude < arriveRadius) { arrived = true; return Vector3.zero; }
            if (delta.sqrMagnitude < 0.0004f) return Vector3.zero;
            return delta.normalized;
        }

        void CheckStuck(float now)
        {
            if (now < nextStuckCheck) return;
            nextStuckCheck = now + 4f;
            if (pc == null) return;

            Vector3 pos = pc.transform.position;
            if ((pos - lastStuckPos).magnitude < 0.3f && phase != Phase.PushBarricade)
            {
                nextPathTime = 0f;                              // try the path again first
                if (skipToRouteIndex < 0 && (now - phaseStart) > 8f && phase == Phase.Tour)
                {
                    skipToRouteIndex = (routeIndex + 1) % Route.Length;
                    Log("stuck - skipping to the next waypoint");
                }
            }
            lastStuckPos = pos;
        }

        // ------------------------------------------------------------------ doors
        void TryOpenDoors()
        {
            if (doors == null || interaction == null || pc == null) return;
            Vector3 pos = pc.transform.position;
            for (int i = 0; i < doors.Length; i++)
            {
                Door d = doors[i];
                if (d == null) continue;
                if (d.State != Door.DoorState.Closed && d.State != Door.DoorState.Closing) continue;
                Vector3 dp = d.transform.position;
                if (Mathf.Abs(dp.y - pos.y) > 2f) continue;
                if ((dp - pos).sqrMagnitude < 1.9f * 1.9f)
                {
                    interaction.UseDoorExternal(d);
                    nextPathTime = 0f;                          // re-path now that the way is open
                    Log("opened door " + d.name);
                }
            }
        }

        // ------------------------------------------------------------------ combat
        void CombatScan(float now)
        {
            aimTarget = null;
            Vector3 eye = pc.transform.position + Vector3.up * 1.62f;
            float best = 22f;

            for (int i = 0; i < ZombieHealth.Alive.Count; i++)
            {
                ZombieHealth z = ZombieHealth.Alive[i];
                if (z == null || !z.IsAlive) continue;
                Vector3 chest = z.transform.position + Vector3.up * (z.kind == ZombieKind.Runner ? 1.25f : 1.5f);
                float d = Vector3.Distance(eye, chest);
                if (d >= best) continue;
                if (!LineClear(eye, chest, z.transform)) continue;
                best = d;
                aimTarget = z;
            }

            // during the Screamer standoff, stare at the Screamer (never fire at it) unless
            // something else is right on top of us
            if (phase == Phase.Standoff && screamer != null && screamer.IsAlive)
            {
                if (aimTarget == null || Vector3.Distance(pc.transform.position, aimTarget.transform.position) > 6f)
                    aimTarget = screamer;
            }

            if (aimTarget != null)
            {
                bool head = aimTarget.kind == ZombieKind.Runner;      // Runners: head shots, they are quick
                aimPoint = aimTarget.transform.position + Vector3.up * (head ? 1.62f : 1.35f);
                aimPoint += new Vector3(Mathf.Sin(now * 5.1f), Mathf.Sin(now * 4.3f), 0f) * 0.02f;

                // only shoot the things that are not part of the show
                if (aimTarget.kind != ZombieKind.Screamer && weapon != null)
                    weapon.FireExternal();
            }

            // reload when the magazine is empty and nothing is breathing down our neck
            if (stats.ammo <= 0 && stats.reserveAmmo > 0 && weapon != null)
            {
                float nearest = NearestZombieDistance();
                if (nearest > 4f) weapon.ReloadExternal();
            }

            TrackTank(now);

            // a distraction bottle for a distant, uninterested Tank
            ZombieHealth tank = NearestOfKind(ZombieKind.Tank);
            if (tank != null && !tankHunting && interaction != null && stats.bottles > 0 && now >= nextBottle)
            {
                float d = Vector3.Distance(pc.transform.position, tank.transform.position);
                if (d > 12f)
                {
                    interaction.ThrowExternal(Throwable.ThrowType.Bottle);
                    nextBottle = now + 14f;
                    Log("threw a bottle to lure the Tank");
                }
            }

            // one grenade when a Tank gets into our face
            if (tank != null && !grenadeUsed && interaction != null && stats.grenades > 0)
            {
                float d = Vector3.Distance(pc.transform.position, tank.transform.position);
                if (d <= 9f)
                {
                    interaction.ThrowExternal(Throwable.ThrowType.Grenade);
                    grenadeUsed = true;
                    Log("grenade out - a Tank that close is worth the ammo");
                }
            }
        }

        void TrackTank(float now)
        {
            ZombieHealth tank = NearestOfKind(ZombieKind.Tank);
            if (tank == null) { prevTankDist = 999f; tankClosingSince = -1f; tankHunting = false; return; }

            float d = Vector3.Distance(pc.transform.position, tank.transform.position);
            bool closing = d < prevTankDist - 0.04f;
            prevTankDist = d;

            if (closing)
            {
                if (tankClosingSince < 0f) tankClosingSince = now;
                tankHunting = now - tankClosingSince > 2f;
            }
            else { tankClosingSince = -1f; tankHunting = false; }

            TankBrain tb = tank.GetComponent<TankBrain>();
            if (tb != null && tb.DebugInfo.Contains("Chase")) tankHunting = true;   // belt and braces
        }

        float NearestZombieDistance()
        {
            float best = 999f;
            Vector3 pos = pc.transform.position;
            for (int i = 0; i < ZombieHealth.Alive.Count; i++)
            {
                ZombieHealth z = ZombieHealth.Alive[i];
                if (z == null || !z.IsAlive) continue;
                float d = Vector3.Distance(pos, z.transform.position);
                if (d < best) best = d;
            }
            return best;
        }

        ZombieHealth NearestOfKind(ZombieKind kind)
        {
            ZombieHealth best = null;
            float bestD = 999f;
            Vector3 pos = pc.transform.position;
            for (int i = 0; i < ZombieHealth.Alive.Count; i++)
            {
                ZombieHealth z = ZombieHealth.Alive[i];
                if (z == null || !z.IsAlive || z.kind != kind) continue;
                float d = Vector3.Distance(pos, z.transform.position);
                if (d < bestD) { bestD = d; best = z; }
            }
            return best;
        }

        /// <summary>The closest alive zombie that is not the demo Screamer and within 'radius' (null if none).</summary>
        ZombieHealth NearestNonScreamerWithin(float radius)
        {
            ZombieHealth best = null;
            float bestD = radius;
            Vector3 pos = pc.transform.position;
            for (int i = 0; i < ZombieHealth.Alive.Count; i++)
            {
                ZombieHealth z = ZombieHealth.Alive[i];
                if (z == null || !z.IsAlive) continue;
                if (screamer != null && z == screamer) continue;
                float d = Vector3.Distance(pos, z.transform.position);
                if (d < bestD) { bestD = d; best = z; }
            }
            return best;
        }

        static bool LineClear(Vector3 from, Vector3 to, Transform target)
        {
            Vector3 dir = to - from;
            float d = dir.magnitude;
            if (d < 0.1f) return true;
            RaycastHit hit;
            if (!Physics.Linecast(from, to, out hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            if (hit.transform == target || hit.transform.IsChildOf(target)) return true;
            return false;
        }

        // ------------------------------------------------------------------ look
        void UpdateLook(float now, Vector3 moveDir)
        {
            float wantedYaw;
            float wantedEulerX;

            if (aimTarget != null)
            {
                Vector3 eye = pc.transform.position + Vector3.up * 1.62f;
                Vector3 to = aimPoint - eye;
                float horiz = new Vector2(to.x, to.z).magnitude;
                wantedYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                wantedEulerX = -Mathf.Atan2(to.y, Mathf.Max(0.01f, horiz)) * Mathf.Rad2Deg;   // negative = look up
            }
            else if (moveDir.sqrMagnitude > 0.02f)
            {
                // walking: look where we are going with a lazy human drift
                wantedYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg + Mathf.Sin(now * 0.5f) * 5f;
                wantedEulerX = -2f + Mathf.Sin(now * 0.7f) * 2.5f;
            }
            else
            {
                // standing: idle scanning
                wantedYaw = pc.transform.eulerAngles.y + Mathf.Sin(now * 0.35f) * 14f;
                wantedEulerX = Mathf.Sin(now * 0.6f) * 4f;
            }

            float curEulerX = pc.cam != null ? pc.cam.transform.localRotation.eulerAngles.x : 0f;
            if (curEulerX > 180f) curEulerX -= 360f;

            float errYaw = Mathf.DeltaAngle(pc.transform.eulerAngles.y, wantedYaw);
            float errPitch = Mathf.DeltaAngle(curEulerX, wantedEulerX);

            float gain = aimTarget != null ? 3.4f : 2.2f;
            float maxRate = pc.externalTurnRate;
            pc.ExternalLook(Mathf.Clamp(errYaw * gain, -maxRate, maxRate),
                            Mathf.Clamp(-errPitch * gain, -maxRate, maxRate));
        }

        // ------------------------------------------------------------------ scripted state
        void ScriptedState(float now)
        {
            float t = now - startTime;

            // ---- scripted health: 100 -> ~45, a scripted recovery at t=120, then down towards 22.
            // Real damage still hurts (dramatic!), but the demo can never die: floor at 12 hp.
            float scripted;
            if (t < 118f) scripted = Mathf.Lerp(100f, 45f, t / 118f);
            else if (t < 124f)
            {
                scripted = 60f;
                if (stats.health < 55f)
                {
                    stats.health = 60f;
                    if (hud != null) hud.Toast("Recovered behind cover", 2.5f);
                    Log("scripted recovery: health back to 60");
                }
            }
            else scripted = Mathf.Lerp(60f, 22f, (t - 124f) / (DemoDuration - 124f));

            if (stats.health > scripted) stats.health = Mathf.Max(scripted, stats.health - 8f * Time.deltaTime);
            if (stats.health < 12f) stats.health = 12f;

            // ---- scripted reserve ammo: 36 -> ~8 across the demo (topped up so it never runs dry)
            float floorReserve = Mathf.Lerp(36f, 8f, Mathf.Clamp01(t / DemoDuration));
            if (stats.reserveAmmo < floorReserve) stats.reserveAmmo = Mathf.CeilToInt(floorReserve);
        }

        void OnDied()
        {
            Log("EMERGENCY REVIVE - the scripted demo must never end on a game-over screen");
            pc.ReviveExternal();
            stats.health = 60f;
            gm.state = GameManager.State.Playing;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            pc.SetCursorLocked(true);
            if (hud != null) hud.Toast("Auto-revive (demo safety)", 2.5f);
        }

        // ------------------------------------------------------------------ human takeover
        void CheckHumanInput()
        {
            if (!yieldToHuman || manualTakeover)
            {
                if (manualTakeover)
                {
                    Keyboard kb0 = Keyboard.current;
                    if (kb0 != null && kb0.f9Key.wasPressedThisFrame)
                    {
                        manualTakeover = false;
                        pc.externalControl = true;
                        if (weapon != null) weapon.externalInput = true;
                        pc.SetCursorLocked(true);
                        nextPathTime = 0f;
                        if (hud != null && showCaptions) hud.ShowCaption("Demo bot back in control", 2.5f);
                        Log("control returned to the demo bot (F9)");
                    }
                }
                return;
            }

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;

            bool pressed = false;
            if (kb != null)
            {
                // every key except the debug hotkeys counts as "a human wants to play"
                foreach (KeyControl k in kb.allKeys)
                {
                    if (k == null) continue;
                    Key code = k.keyCode;
                    if (code == Key.None || code == Key.Escape || code == Key.F1 || code == Key.F9) continue;
                    if (k.wasPressedThisFrame) { pressed = true; break; }
                }
            }
            if (!pressed && mouse != null && mouse.delta.ReadValue().sqrMagnitude > 25f) pressed = true;   // > 5 px

            if (pressed)
            {
                manualTakeover = true;
                pc.externalControl = false;
                if (weapon != null) weapon.externalInput = false;
                if (hud != null && showCaptions) hud.ShowCaption("Manual control - press F9 to give it back to the demo", 5f);
                Log("human took over (storyboard keeps running)");
            }
        }

        // ------------------------------------------------------------------ helpers
        void TryPendingSpawn(float now)
        {
            if (now < pending.nextTry) return;
            GameObject z = director.ForceSpawn(pending.kind, pending.filter);
            if (z != null)
            {
                hasPending = false;
                OnScriptedSpawn(z, pending.kind);
            }
            else
            {
                pending.nextTry = now + 2f;
                if (now >= pending.giveUp) { hasPending = false; Log("gave up spawning the " + pending.kind); }
            }
        }

        void OnScriptedSpawn(GameObject z, ZombieKind kind)
        {
            Log("scripted spawn: " + kind + " at " + z.transform.position.ToString("0.0"));
            ZombieHealth hp = z.GetComponent<ZombieHealth>();
            if (kind == ZombieKind.Screamer)
            {
                screamer = hp;
                if (phase != Phase.Standoff) SetPhase(Phase.Standoff);
            }
            if (demoCam != null) demoCam.OnZombieSpawned(hp);
        }
        void Log(string msg)
        {
            float t = Time.time - startTime;
            Debug.Log("[DemoBot " + t.ToString("0.0") + "s] " + msg);
        }
    }
}
