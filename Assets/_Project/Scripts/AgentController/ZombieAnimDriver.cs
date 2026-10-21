using UnityEngine;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>
    /// One place that turns "speed / attacking / crouching" into animation, whatever the body is:
    /// the Blender zombie (ZombieProceduralMotion over the Animator) or the BlockRig fallback.
    /// </summary>
    public class ZombieAnimDriver : MonoBehaviour
    {
        ZombieProceduralMotion motion;
        BlockRig block;

        void Awake() { Find(); }

        void Find()
        {
            motion = GetComponent<ZombieProceduralMotion>();
            if (motion == null) motion = GetComponentInChildren<ZombieProceduralMotion>();
            block = GetComponentInChildren<BlockRig>();
        }

        public void Set(float speed, float runBlend, float attack, float crouch = 0f)
        {
            if (motion == null && block == null) Find();
            if (motion != null)
            {
                motion.Speed = speed;
                motion.RunBlend = runBlend;
                motion.Attack = attack;
                motion.Crouch = crouch;
            }
            if (block != null)
            {
                block.Speed = speed;
                block.RunBlend = runBlend;
                block.Attack = attack;
                block.Crouch = crouch;
            }
        }
    }
}
