#if UNITY_EDITOR
// ---------------------------------------------------------------------------
//  DemoRecorder — one-click 3-minute 1080p30 recording of the scripted demo.
//
//  Menu: Tools > Zombie Game > Record 3-Min Demo
//    1. Enables the DemoBot (session-only, no scene/prefab changes).
//    2. Forces the Game view to 1920x1080.
//    3. Opens MainLevel.unity (prompts to save unsaved changes).
//    4. Enters Play mode; a hidden host object starts Unity Recorder
//       (MP4 / H.264, Game view input, 30 fps constant, audio on)
//       the moment the bot reports Ready, records for exactly 180 s and
//       then stops and exits Play mode.
//    5. Output: <ProjectRoot>/Recordings/ZombieOutbreak_Demo.mp4
//
//  Command line (same flow, but unattended and exits the editor when done):
//    Unity.exe -projectPath <project> \
//      -executeMethod ZombieGame.EditorTools.DemoRecorder.RecordFromCommandLine \
//      -logFile record.log
//    Exit code 0 = the recording is on disk, 1 = nothing was recorded
//    (or the safety timeout fired), 2 = a script recompile was triggered.
//
//  Menu: Tools > Zombie Game > Stop Recording   — early stop (auto-exit).
//  Menu: Tools > Zombie Game > Demo Bot Enabled/Disabled — manual toggle.
//
//  Recorder API guarded with UNITY_RECORDER_PRESENT, which is auto-added /
//  removed below based on whether "com.unity.recorder" is really resolved
//  (packages-lock.json / Library/PackageCache, manifest.json as fallback).
// ---------------------------------------------------------------------------
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieGame;

namespace ZombieGame.EditorTools
{
    [InitializeOnLoad]
    public static class DemoRecorder
    {
        public const string ScenePath = "Assets/_Project/Scenes/MainLevel.unity";
        public const string RecordKey = "ZombieGame.DemoRecorder.Record";
        public const string StopKey = "ZombieGame.DemoRecorder.Stop";
        public const string EnabledKey = "ZombieGame.DemoBot.Enabled";
        public const string OutputPath = "Recordings/ZombieOutbreak_Demo.mp4";
        /// <summary>Set by the host as soon as Recorder is really running (read by the CLI exit logic).</summary>
        public const string RecStartedKey = "ZombieGame.DemoRecorder.RecStarted";

        const string PerfKey = "ZombieGame.DemoRecorder.PerfSaved";
        const string OldVSyncKey = "ZombieGame.DemoRecorder.OldVSync";
        const string OldTfrKey = "ZombieGame.DemoRecorder.OldTargetFrameRate";
        const string CliKey = "ZombieGame.DemoRecorder.CLI";
        const string CliStartKey = "ZombieGame.DemoRecorder.CLIStart";

        const double CliTimeoutSeconds = 360.0;      // "nothing recorded after 6 minutes" -> fail
        // Frame-paced capture renders offline: on a slow GPU 5400 frames can take 15+ minutes
        // of wall time, so the hang guard is deliberately generous.
        const double CliHardTimeoutSeconds = 2700.0; // started but never finished (45 min) -> fail

        static DemoRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += CliTick;     // safety net for -executeMethod runs
            // deferred: changing scripting defines during the reload itself would retrigger it
            EditorApplication.delayCall += () => SyncRecorderDefine();
        }

        // ----- menu items ---------------------------------------------------

        [MenuItem("Tools/Zombie Game/Record 3-Min Demo", false, 1)]
        public static void RecordDemo()
        {
            SyncRecorderDefine();
            bool cli = SessionState.GetBool(CliKey, false);

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // command-line runs must never block on a dialog nobody can click
                if (!cli && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                if (!File.Exists(ScenePath))
                {
                    if (cli)
                    {
                        Debug.LogError("[DemoRecorder] Scene not found: " + ScenePath);
                        EditorApplication.Exit(1);
                        return;
                    }
                    EditorUtility.DisplayDialog("Zombie Game",
                        "Scene not found: " + ScenePath, "OK");
                    return;
                }
                EditorSceneManager.OpenScene(ScenePath);
            }

            SetGameViewSize(1920, 1080);

            // Record at a locked 30 fps (belt and braces with Recorder's "Cap frame rate").
            // The previous values are remembered in SessionState and restored when play mode ends.
            if (!SessionState.GetBool(PerfKey, false))
            {
                SessionState.SetInt(OldVSyncKey, QualitySettings.vSyncCount);
                SessionState.SetInt(OldTfrKey, Application.targetFrameRate);
                SessionState.SetBool(PerfKey, true);
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 30;
            }

            SessionState.SetBool(EnabledKey, true);
            SessionState.SetBool(RecordKey, true);
            SessionState.SetBool(StopKey, false);
            SessionState.SetBool(RecStartedKey, false);

            EnsureOutputDirAndClean();

            Debug.Log("[DemoRecorder] Starting 3-minute demo recording -> " + OutputPath);
            if (EditorApplication.isPlaying)
                SpawnHost();          // already in Play mode — start the host right away
            else
                EditorApplication.EnterPlaymode();
        }

        // ----- command-line entry point -------------------------------------

        /// <summary>
        /// Unattended version of the menu item (see the header for the exact command line).
        /// The start is deferred with EditorApplication.delayCall so the editor is fully
        /// initialised first; when the finished MP4 is on disk the editor closes itself
        /// with exit code 0 (1 = nothing was recorded, 2 = a recompile was triggered).
        /// </summary>
        public static void RecordFromCommandLine()
        {
            EditorApplication.delayCall += () =>
            {
                if (SyncRecorderDefine())
                {
                    // adding/removing a define forces a domain reload that would abort the take
                    Debug.LogError("[DemoRecorder] UNITY_RECORDER_PRESENT changed - scripts are " +
                        "recompiling. Re-run the command once the compilation has finished.");
                    EditorApplication.Exit(2);
                    return;
                }

                SessionState.SetBool(CliKey, true);
                SessionState.SetInt(CliStartKey, (int)EditorApplication.timeSinceStartup);
                SessionState.SetBool(RecStartedKey, false);
                Debug.Log("[DemoRecorder] command-line recording requested.");
                RecordDemo();
            };
        }

        /// <summary>
        /// Batch helper that applies the define sync synchronously (EditorApplication.delayCall
        /// does not run under -batchmode -quit). Useful for CI before a command-line recording:
        ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt; \
        ///     -executeMethod ZombieGame.EditorTools.DemoRecorder.SyncDefineNow
        /// </summary>
        public static void SyncDefineNow()
        {
            bool changed = SyncRecorderDefine();
            var namedTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            Debug.Log("[DemoRecorder] SyncDefineNow: changed=" + changed +
                ", defines=[" + PlayerSettings.GetScriptingDefineSymbols(namedTarget) + "]");
        }

        // Runs in every domain (subscribed by the static constructor) and only
        // does something while a command-line run is in flight.
        static void CliTick()
        {
            if (!SessionState.GetBool(CliKey, false)) return;

            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetInt(CliStartKey, 0);
            bool started = SessionState.GetBool(RecStartedKey, false);

            if (!started && elapsed > CliTimeoutSeconds)
            {
                Debug.LogError("[DemoRecorder] CLI safety timeout: nothing was recorded after " +
                    elapsed.ToString("0") + " s.");
                SessionState.SetBool(CliKey, false);
                EditorApplication.Exit(1);
            }
            else if (started && elapsed > CliHardTimeoutSeconds)
            {
                Debug.LogError("[DemoRecorder] CLI safety timeout: play mode still running " +
                    elapsed.ToString("0") + " s after the start.");
                SessionState.SetBool(CliKey, false);
                EditorApplication.Exit(1);
            }
        }

        // Play mode ended during a command-line run: give Recorder a moment to finish
        // writing the file, verify it is really there, then leave the editor.
        static bool cliExitPending;
        static double cliExitStart;
        static long cliExitBytes = -1;
        static void FinishCommandLineRecording()
        {
            if (!SessionState.GetBool(CliKey, false)) return;
            SessionState.SetBool(CliKey, false);
            cliExitPending = true;
            cliExitStart = EditorApplication.timeSinceStartup;
            cliExitBytes = -1;
            EditorApplication.update += CliExitTick;
        }

        static void CliExitTick()
        {
            if (!cliExitPending) return;

            double t = EditorApplication.timeSinceStartup - cliExitStart;
            long bytes = OutputBytes();

            if (t < 2.0) return;                                             // let Recorder finalize
            if (t < 10.0 && bytes != cliExitBytes) { cliExitBytes = bytes; return; }  // still growing

            cliExitPending = false;
            EditorApplication.update -= CliExitTick;

            bool started = SessionState.GetBool(RecStartedKey, false);
            if (started && bytes > 100000)
            {
                Debug.Log("[DemoRecorder] CLI recording finished -> " +
                    Path.GetFullPath(OutputPath) + " (" + bytes + " bytes)");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[DemoRecorder] CLI recording FAILED (recording started: " +
                    started + ", file size: " + bytes + " bytes). Check the [DemoRecorder] / " +
                    "[DemoBot] / [DemoCamera] messages above.");
                EditorApplication.Exit(1);
            }
        }

        static long OutputBytes()
        {
            try
            {
                string full = Path.GetFullPath(OutputPath);
                return File.Exists(full) ? new FileInfo(full).Length : 0L;
            }
            catch (Exception) { return 0L; }
        }

        // Creates the hidden host that drives Recorder (used on Play mode entry
        // and directly when the menu is invoked while already playing).
        public static void SpawnHost()
        {
            if (UnityEngine.Object.FindAnyObjectByType<DemoRecorderHost>() != null)
                return;
            var host = new GameObject("~DemoRecorderHost");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideInHierarchy;
            host.AddComponent<DemoRecorderHost>();
        }

        [MenuItem("Tools/Zombie Game/Stop Recording", false, 2)]
        public static void StopRecording()
        {
            SessionState.SetBool(StopKey, true);
            Debug.Log("[DemoRecorder] Stop requested.");
        }

        [MenuItem("Tools/Zombie Game/Demo Bot Enabled", false, 20)]
        public static void EnableBot() { SessionState.SetBool(EnabledKey, true); }

        [MenuItem("Tools/Zombie Game/Demo Bot Enabled", true)]
        public static bool EnableBotValidate() { return !SessionState.GetBool(EnabledKey, false); }

        [MenuItem("Tools/Zombie Game/Demo Bot Disabled", false, 21)]
        public static void DisableBot() { SessionState.SetBool(EnabledKey, false); }

        [MenuItem("Tools/Zombie Game/Demo Bot Disabled", true)]
        public static bool DisableBotValidate() { return SessionState.GetBool(EnabledKey, false); }

        // ----- play mode wiring --------------------------------------------

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                if (SessionState.GetBool(RecordKey, false) && !SessionState.GetBool(StopKey, false))
                    SpawnHost();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                // play mode has ended: put the editor performance settings back the way we found them
                if (SessionState.GetBool(PerfKey, false))
                {
                    SessionState.SetBool(PerfKey, false);
                    QualitySettings.vSyncCount = SessionState.GetInt(OldVSyncKey, 1);
                    Application.targetFrameRate = SessionState.GetInt(OldTfrKey, -1);
                }
                SessionState.SetBool(RecordKey, false);

                // command-line runs: close the editor once the finished file is verified
                FinishCommandLineRecording();
            }
        }

        // ----- output folder ------------------------------------------------

        public static void EnsureOutputDirAndClean()
        {
            try
            {
                string dir = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (File.Exists(OutputPath))
                    File.Delete(OutputPath);
                if (File.Exists(OutputPath + ".mp4"))
                    File.Delete(OutputPath + ".mp4");   // leftover from builds that passed the extension
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DemoRecorder] Could not prepare output file: " + e.Message);
            }
        }

        // ----- Game view size (reflection — size ids are project specific) --

        public static void SetGameViewSize(int width, int height)
        {
            try
            {
                // GameViewSizes / GameViewSize / GameViewSizeGroup are internal to UnityEditor,
                // so everything below goes through reflection.
                Type sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
                if (sizesType == null) throw new Exception("GameViewSizes type not found");

                Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                PropertyInfo instanceProp = singletonType.GetProperty("instance",
                    BindingFlags.Public | BindingFlags.Static);
                object gameViewSizes = instanceProp.GetValue(null, null);

                // currentGroup follows the active build target (Standalone for this project).
                object group = sizesType.GetProperty("currentGroup",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(gameViewSizes, null);
                Type groupType = group.GetType();

                MethodInfo getTotalCount = groupType.GetMethod("GetTotalCount");
                MethodInfo getGameViewSize = groupType.GetMethod("GetGameViewSize");
                int count = (int)getTotalCount.Invoke(group, null);

                // Reuse an existing entry with the same resolution if present.
                for (int i = 0; i < count; i++)
                {
                    object size = getGameViewSize.Invoke(group, new object[] { i });
                    if ((int)size.GetType().GetProperty("width").GetValue(size, null) == width &&
                        (int)size.GetType().GetProperty("height").GetValue(size, null) == height)
                    {
                        SessionSetSelectedSize(i);
                        return;
                    }
                }

                // Otherwise add a custom entry (GameViewSizeType.FixedResolution == 1).
                Type sizeClassType = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
                Type sizeEnumType = Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
                object customSize = Activator.CreateInstance(sizeClassType,
                    new object[] { Enum.ToObject(sizeEnumType, 1), width, height, width + "x" + height });

                int newIndex = count;
                groupType.GetMethod("AddCustomSize").Invoke(group, new object[] { customSize });
                sizesType.GetMethod("SaveToHDD").Invoke(gameViewSizes, null);
                SessionSetSelectedSize(newIndex);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DemoRecorder] Could not set Game view size automatically (" +
                    e.Message + "). Please select a 1920x1080 resolution in the Game view dropdown.");
            }
        }

        static void SessionSetSelectedSize(int index)
        {
            try
            {
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                MethodInfo method = gameViewType.GetMethod("SizeSelectionCallback",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                bool any = false;
                foreach (EditorWindow w in Resources.FindObjectsOfTypeAll(gameViewType))
                {
                    method.Invoke(w, new object[] { index, null });
                    w.Repaint();
                    any = true;
                }

                // No Game view in the saved layout: open one, GameViewInput needs it to render.
                if (!any)
                {
                    EditorWindow w = EditorWindow.GetWindow(gameViewType);
                    if (w != null)
                    {
                        method.Invoke(w, new object[] { index, null });
                        w.Repaint();
                        Debug.Log("[DemoRecorder] Opened a Game view at " + index + ".");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DemoRecorder] SizeSelectionCallback failed: " + e.Message);
            }
        }

        // ----- scripting define sync ---------------------------------------

        /// <summary>Keeps UNITY_RECORDER_PRESENT in step with the resolved packages.
        /// Returns true when it changed the defines (which triggers a recompile).</summary>
        static bool SyncRecorderDefine()
        {
            try
            {
                bool present = PackageResolved();
                var namedTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                    BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
                string defines = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                bool hasDefine = defines.Contains("UNITY_RECORDER_PRESENT");
                if (present == hasDefine) return false;

                string updated = present
                    ? (string.IsNullOrEmpty(defines) ? "UNITY_RECORDER_PRESENT"
                        : defines + ";UNITY_RECORDER_PRESENT")
                    : defines.Replace("UNITY_RECORDER_PRESENT", "").Trim(';');
                PlayerSettings.SetScriptingDefineSymbols(namedTarget, updated);
                Debug.Log("[DemoRecorder] UNITY_RECORDER_PRESENT " +
                    (present ? "added" : "removed") + ".");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DemoRecorder] Define sync failed: " + e.Message);
                return false;
            }
        }

        // The define must only be set when com.unity.recorder is really on disk: check the
        // resolved package (packages-lock.json / Library/PackageCache) and use manifest.json
        // only as a fallback when the project has no lock file yet (very first resolution).
        static bool PackageResolved()
        {
            try
            {
                if (File.Exists("Packages/packages-lock.json") &&
                    File.ReadAllText("Packages/packages-lock.json")
                        .IndexOf("com.unity.recorder", StringComparison.Ordinal) >= 0)
                    return true;

                if (Directory.Exists("Library/PackageCache") &&
                    Directory.GetDirectories("Library/PackageCache", "com.unity.recorder*").Length > 0)
                    return true;

                // no lock file yet: trust the manifest, the package manager will resolve it now
                if (!File.Exists("Packages/packages-lock.json"))
                    return File.ReadAllText("Packages/manifest.json").Contains("com.unity.recorder");

                return false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DemoRecorder] Package check failed: " + e.Message);
                return false;
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Runtime host: waits for the bot, records 180 s, then exits Play mode.
    // -----------------------------------------------------------------------
    public class DemoRecorderHost : MonoBehaviour
    {
        bool started;
        float startGameTime;
        const float Duration = 180f;
        const float ReadyTimeout = 60f;

        bool yieldForced;                 // a stray key/mouse move must not end the take
        bool savedYieldToHuman;

#if UNITY_RECORDER_PRESENT
        UnityEditor.Recorder.RecorderController controller;
#endif

        void Start()
        {
            EnsureYieldForced();
#if UNITY_RECORDER_PRESENT
            try
            {
                PrepareRecording();
                Debug.Log("[DemoRecorderHost] Recorder prepared; waiting for DemoBot.Ready.");
            }
            catch (Exception e)
            {
                Debug.LogError("[DemoRecorderHost] Failed to prepare recorder: " + e);
                Finish();
            }
#else
            Debug.LogError("[DemoRecorderHost] UNITY_RECORDER_PRESENT is not defined — " +
                "the com.unity.recorder package is missing. Falling back: record the Game " +
                "view with Xbox Game Bar (Win+Alt+R) or OBS instead.");
            Finish();
#endif
        }

        void Update()
        {
            EnsureYieldForced();

            // Always honour a manual stop.
            if (SessionState.GetBool(DemoRecorder.StopKey, false))
            {
                if (started) StopRecording();
                Finish();
                return;
            }

            if (!started)
            {
                if (DemoBot.Ready)
                {
                    StartRecording();
                }
                else if (Time.unscaledTime > ReadyTimeout)
                {
                    Debug.LogWarning("[DemoRecorderHost] DemoBot never became ready in " +
                        ReadyTimeout + "s — stopping without a recording.");
                    Finish();
                }
                return;
            }

            // The Recorder stops itself after 180 s of RECORDED time (5400 frames at 30 fps) and
            // ExitPlayMode then leaves Play mode. Frame-paced capture sets Time.captureDeltaTime,
            // so the game advances 1/30 s per frame while the machine may render far slower than
            // real time. Wall-clock time must therefore NOT cut the take: the Recorder's own
            // interval is what guarantees an exact 180.0 s video.
            if (!IsRecordingNow())
            {
                Debug.Log("[DemoRecorderHost] Recorder finished its 180 s interval — wrapping up.");
                Finish();
                return;
            }

            if (Time.time - startGameTime > Duration + 5f)
            {
                Debug.LogWarning("[DemoRecorderHost] Take exceeded 180 s of game time — stopping.");
                StopRecording();
                Finish();
                return;
            }

            if (!DemoBot.Ready)
            {
                Debug.Log("[DemoRecorderHost] DemoBot ended early — stopping recording.");
                StopRecording();
                Finish();
            }
        }

        bool IsRecordingNow()
        {
#if UNITY_RECORDER_PRESENT
            try { return controller != null && controller.IsRecording(); }
            catch (Exception) { return false; }
#else
            return true;   // recorder compiled out: let the game-time failsafe end the take
#endif
        }

        // While the take runs the bot must ignore the human: force yieldToHuman off
        // (the original value is put back in RestoreYield when the take is over).
        void EnsureYieldForced()
        {
            DemoBot bot = DemoBot.Instance;
            if (bot == null) return;
            if (!yieldForced) { savedYieldToHuman = bot.yieldToHuman; yieldForced = true; }
            bot.yieldToHuman = false;
        }

        void RestoreYield()
        {
            if (!yieldForced) return;
            DemoBot bot = DemoBot.Instance;
            if (bot != null) bot.yieldToHuman = savedYieldToHuman;
            yieldForced = false;
        }

#if UNITY_RECORDER_PRESENT
        void PrepareRecording()
        {
            DemoRecorder.EnsureOutputDirAndClean();

            var output = ScriptableObject.CreateInstance<UnityEditor.Recorder.MovieRecorderSettings>();
            output.name = "Demo Movie Recorder";
            output.Enabled = true;

#pragma warning disable 618 // OutputFormat / VideoBitRateMode / EncodingQuality are obsolete but supported
            output.OutputFormat = UnityEditor.Recorder.MovieRecorderSettings.VideoRecorderOutputFormat.MP4; // H.264 in MP4
            output.VideoBitRateMode = VideoBitrateMode.High;
            output.EncodingQuality = UnityEditor.Recorder.MovieRecorderSettings.VideoEncodingQuality.High;
#pragma warning restore 618

            output.CaptureAudio = true;                          // keep game audio in the file
            output.AudioInputSettings.PreserveAudio = true;
            output.ImageInputSettings = new UnityEditor.Recorder.Input.GameViewInputSettings
            {
                OutputWidth = 1920,
                OutputHeight = 1080
            };
            // Recorder appends the container extension itself, so pass the path WITHOUT ".mp4"
            // or the deliverable would end up as ZombieOutbreak_Demo.mp4.mp4.
            string outNoExt = DemoRecorder.OutputPath;
            if (outNoExt.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                outNoExt = outNoExt.Substring(0, outNoExt.Length - 4);
            output.OutputFile = Path.GetFullPath(outNoExt).Replace('\\', '/');

            var controllerSettings = ScriptableObject.CreateInstance<UnityEditor.Recorder.RecorderControllerSettings>();
            controllerSettings.SetRecordModeToTimeInterval(0f, Duration);
            controllerSettings.FrameRate = 30f;
            controllerSettings.FrameRatePlayback = UnityEditor.Recorder.FrameRatePlayback.Constant;
            controllerSettings.CapFrameRate = true;
            controllerSettings.ExitPlayMode = true;

            controllerSettings.AddRecorderSettings(output);
            controller = new UnityEditor.Recorder.RecorderController(controllerSettings);
        }

        void StartRecording()
        {
            if (controller == null) return;
            controller.PrepareRecording();   // required by the Recorder API before StartRecording
            bool ok = controller.StartRecording();
            started = true;
            startGameTime = Time.time;
            if (ok)
            {
                SessionState.SetBool(DemoRecorder.RecStartedKey, true);
                Debug.Log("[DemoRecorderHost] Recording started (1920x1080 @ 30fps, 180 s).");
            }
            else
            {
                Debug.LogError("[DemoRecorderHost] Recorder refused to start - check Window > Recorder " +
                    "for the reported error. Fallback: capture the Game view with Xbox Game Bar " +
                    "(Win+Alt+R) or OBS.");
                Finish();
            }
        }

        void StopRecording()
        {
            try
            {
                if (controller != null && controller.IsRecording())
                {
                    controller.StopRecording();
                    Debug.Log("[DemoRecorderHost] Recording stopped -> " +
                        Path.GetFullPath(DemoRecorder.OutputPath + ".mp4"));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DemoRecorderHost] Stop failed: " + e.Message);
            }
        }
#else
        void StartRecording() { started = true; startGameTime = Time.time; }
        void StopRecording() { }
#endif

        void Finish()
        {
            enabled = false;
            RestoreYield();
            EditorApplicationDelayCall();
        }

        void OnDestroy()
        {
            RestoreYield();
#if UNITY_RECORDER_PRESENT
            try
            {
                if (controller != null && controller.IsRecording())
                    controller.StopRecording();
            }
            catch (Exception) { }
#endif
        }

        static void EditorApplicationDelayCall()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (UnityEditor.EditorApplication.isPlaying)
                    UnityEditor.EditorApplication.ExitPlaymode();
            };
        }
    }
}
#endif
