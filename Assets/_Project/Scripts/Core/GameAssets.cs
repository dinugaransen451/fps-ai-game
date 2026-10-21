using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Asset references the runtime needs (prefabs made by the editor setup).
    /// Lives in Resources/ so a built game can load it. Every field is optional:
    /// without them the game falls back to simple procedural stand-ins.
    /// </summary>
    [CreateAssetMenu(menuName = "Zombie Game/Game Assets")]
    public class GameAssets : ScriptableObject
    {
        [Tooltip("Screamer prefab (Member 3: model + ScreamerBrain).")]
        public GameObject screamerPrefab;

        [Tooltip("Wrecked locker prop with HideSpot (Member 3).")]
        public GameObject lockerPrefab;

        [Tooltip("Zombie model + Animator, no AI. Used for the Tank and the Runner.")]
        public GameObject zombieVisualPrefab;

        public AudioClip screamClip;

        public static GameAssets Load()
        {
            return Resources.Load<GameAssets>("GameAssets");
        }
    }
}
