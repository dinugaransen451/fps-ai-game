using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ZombieGame
{
    /// <summary>Game state: kill quota (the "clear the building" objective), win/lose, pause, restart.</summary>
    public class GameManager : MonoBehaviour
    {
        public enum State { Playing, Paused, Won, Lost }

        public static GameManager Instance { get; private set; }

        public int killsRequired = 20;
        public State state = State.Playing;

        public int Kills { get; private set; }
        public int Headshots { get; private set; }
        public int KillsRunner { get; private set; }
        public int KillsTank { get; private set; }
        public int KillsScreamer { get; private set; }
        public float ElapsedTime { get; private set; }

        public PlayerController playerController;
        public PlayerStats playerStats;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void Start()
        {
            if (playerStats != null) playerStats.Died += OnPlayerDied;
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (state == State.Playing || state == State.Paused)
            {
                if (state == State.Playing) ElapsedTime += Time.deltaTime;
                if (kb.escapeKey.wasPressedThisFrame) SetPaused(state == State.Playing);
            }

            if (state == State.Won || state == State.Lost || state == State.Paused)
            {
                if (kb.rKey.wasPressedThisFrame) Restart();
            }
        }

        void SetPaused(bool paused)
        {
            state = paused ? State.Paused : State.Playing;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            if (playerController != null) playerController.SetCursorLocked(!paused);
        }

        public void RegisterKill(ZombieKind kind, bool headshot)
        {
            Kills++;
            if (headshot) Headshots++;
            if (kind == ZombieKind.Runner) KillsRunner++;
            else if (kind == ZombieKind.Tank) KillsTank++;
            else KillsScreamer++;

            if (state == State.Playing && Kills >= killsRequired) EndGame(true);
        }

        void OnPlayerDied()
        {
            if (state == State.Playing) EndGame(false);
        }

        void EndGame(bool won)
        {
            state = won ? State.Won : State.Lost;
            if (playerController != null)
            {
                playerController.SetCursorLocked(false);
                if (!won) playerController.Die();
            }
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
