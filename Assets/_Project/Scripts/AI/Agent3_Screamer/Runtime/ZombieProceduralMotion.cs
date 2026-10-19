using UnityEngine;

namespace ScreamerAI
{
    /// <summary>
    /// The zombie model ships with ONE animation clip (the scream). This component fills the gap by
    /// layering procedural motion on top of the Animator's idle "hunch" pose in LateUpdate:
    /// shambling walk, faster lurching run, crouch (hiding), breathing and random head/arm twitches.
    ///
    /// While the scream clip plays (ClipPlaying = true) the procedural layer fades out and the
    /// Animator drives the skeleton. All rotations are applied around the MODEL'S right / forward / up
    /// axes, so it doesn't matter how the Mixamo bones are oriented internally.
    ///
    /// If you later get proper walk/run clips (e.g. from Mixamo), just drop this component or set the
    /// amplitudes to 0 - the Agent Controller teammate can replace it with a real Animator blend tree.
    /// </summary>
    [DisallowMultipleComponent]
    public class ZombieProceduralMotion : MonoBehaviour
    {
        public Animator animator;

        [Header("Live inputs (written by ScreamerBrain)")]
        public float Speed;
        [Range(0f, 1f)] public float Crouch;
        [Range(0f, 1f)] public float RunBlend;
        public bool ClipPlaying;

        [Header("Gait tuning (degrees)")]
        public float cycleLength = 1.35f;
        public float walkSwing = 22f;
        public float runSwing = 44f;
        public float kneeBend = 32f;
        public float armSwing = 16f;
        public float armReach = 26f;
        public float baseHunch = 8f;
        public float runLean = 14f;
        public float crouchThighFlex = 55f;
        public float crouchLean = 22f;
        [Range(0f, 2f)] public float twitch = 1f;
        public float blendTime = 0.12f;

        enum Bone
        {
            Hips, Spine, Chest, UpperChest, Neck, Head,
            LeftUpLeg, LeftLeg, LeftFoot, RightUpLeg, RightLeg, RightFoot,
            LeftArm, RightArm,
            Count
        }

        static readonly HumanBodyBones[] HumanIds =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm
        };

        static readonly string[] FallbackNames =
        {
            "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head",
            "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot",
            "LeftArm", "RightArm"
        };

        Transform[] bones;
        Quaternion[] baseRot;
        Vector3 hipsBasePos;
        float legLength = 0.9f;
        bool ready;

        float phase;
        float weight = 1f;
        float weightVel;
        float locoSmoothed;
        float runSmoothed;
        float crouchSmoothed;
        float seed;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            seed = Random.value * 100f;
        }

        void Start()
        {
            if (animator == null) return;

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);   // evaluate the idle hunch pose once so we can cache it

            CacheBones();
        }

        void CacheBones()
        {
            int n = (int)Bone.Count;
            bones = new Transform[n];
            baseRot = new Quaternion[n];

            bool human = animator.avatar != null && animator.avatar.isValid && animator.isHuman;
            for (int i = 0; i < n; i++)
            {
                Transform t = null;
                if (human) t = animator.GetBoneTransform(HumanIds[i]);
                if (t == null) t = FindDeep(animator.transform, FallbackNames[i]);
                bones[i] = t;
                if (t != null) baseRot[i] = t.localRotation;
            }

            Transform hips = bones[(int)Bone.Hips];
            ready = hips != null;
            if (!ready)
            {
                Debug.LogWarning("[ZombieProceduralMotion] Could not find the Hips bone - procedural motion disabled.", this);
                return;
            }
            hipsBasePos = hips.localPosition;

            Transform up = bones[(int)Bone.LeftUpLeg];
            Transform low = bones[(int)Bone.LeftLeg];
            Transform foot = bones[(int)Bone.LeftFoot];
            if (up != null && low != null && foot != null)
                legLength = Vector3.Distance(up.position, low.position) + Vector3.Distance(low.position, foot.position);
        }

        void LateUpdate()
        {
            if (!ready) return;

            float dt = Time.deltaTime;

            float targetWeight = ClipPlaying ? 0f : 1f;
            weight = Mathf.SmoothDamp(weight, targetWeight, ref weightVel, blendTime);
            if (weight < 0.002f) { weight = 0f; return; }   // Animator fully in charge (scream clip)

            float loco = Mathf.Clamp01(Speed / 0.25f);
            locoSmoothed = Mathf.MoveTowards(locoSmoothed, loco, dt * 4f);
            runSmoothed = Mathf.MoveTowards(runSmoothed, RunBlend, dt * 3f);
            crouchSmoothed = Mathf.MoveTowards(crouchSmoothed, Crouch, dt * 2.5f);

            phase += dt * Mathf.PI * 2f * Mathf.Clamp(Speed, 0f, 8f) / Mathf.Max(0.3f, cycleLength);
            float s = Mathf.Sin(phase);
            float c = Mathf.Cos(phase);
            float t = Time.time + seed;

            Transform root = animator.transform;
            Vector3 R = root.right;
            Vector3 F = root.forward;
            Vector3 U = root.up;

            float swing = Mathf.Lerp(walkSwing, runSwing, runSmoothed) * locoSmoothed;
            float crouch = crouchSmoothed;
            float lean = baseHunch * (1f - crouch) + runLean * runSmoothed * locoSmoothed + crouchLean * crouch
                       + Mathf.Sin(Time.time * 1.9f + seed) * 1.2f;                       // breathing

            float nX = (Mathf.PerlinNoise(t * 0.7f, 1.3f) - 0.5f) * 2f * twitch;
            float nY = (Mathf.PerlinNoise(t * 0.9f, 7.1f) - 0.5f) * 2f * twitch;
            float nZ = (Mathf.PerlinNoise(t * 1.1f, 3.7f) - 0.5f) * 2f * twitch;

            // ---- hips (position + rotation)
            Transform hips = bones[(int)Bone.Hips];
            Vector3 animPos = hips.localPosition;
            hips.localPosition = hipsBasePos;
            float bob = -Mathf.Abs(s) * 0.035f * locoSmoothed;
            float drop = legLength * (1f - Mathf.Cos(crouchThighFlex * Mathf.Deg2Rad)) * crouch;
            hips.position += U * (bob - drop);
            hips.localPosition = Vector3.Lerp(animPos, hips.localPosition, weight);
            Apply(Bone.Hips, Q(s * 4f * locoSmoothed, F) * Q(-s * 5f * locoSmoothed, U));

            // ---- spine, neck, head
            float spineTwist = -s * 7f * locoSmoothed;
            float spineRoll = nZ * 2f + s * 3f * locoSmoothed;
            Apply(Bone.Spine, Q(lean * 0.35f, R) * Q(spineTwist * 0.3f, U) * Q(spineRoll * 0.3f, F));
            Apply(Bone.Chest, Q(lean * 0.35f, R) * Q(spineTwist * 0.4f, U) * Q(spineRoll * 0.4f, F));
            Apply(Bone.UpperChest, Q(lean * 0.30f, R) * Q(spineTwist * 0.3f, U) * Q(spineRoll * 0.3f, F));
            Apply(Bone.Neck, Q(nX * 3f, R) * Q(nZ * 4f, F));
            Apply(Bone.Head, Q(-lean * 0.45f + nX * 7f, R) * Q(nY * 14f, U) * Q(nZ * 11f, F));

            // ---- legs
            float flex = crouchThighFlex * crouch;
            float kneeAmp = kneeBend * locoSmoothed * Mathf.Lerp(0.6f, 1f, runSmoothed);

            float thighL = -s * swing - flex;
            float kneeL = Mathf.Max(0f, c) * kneeAmp + 2f * flex;
            float thighR = s * swing - flex;
            float kneeR = Mathf.Max(0f, -c) * kneeAmp + 2f * flex;

            Apply(Bone.LeftUpLeg, Q(thighL, R));
            Apply(Bone.LeftLeg, Q(kneeL, R));
            Apply(Bone.LeftFoot, Q(-(thighL + kneeL), R));      // keep the foot flat
            Apply(Bone.RightUpLeg, Q(thighR, R));
            Apply(Bone.RightLeg, Q(kneeR, R));
            Apply(Bone.RightFoot, Q(-(thighR + kneeR), R));

            // ---- arms: swing opposite to the legs, held out in front like a classic zombie
            float armAmp = armSwing * Mathf.Lerp(1f, 1.8f, runSmoothed) * locoSmoothed;
            float reach = armReach * locoSmoothed + 12f * crouch;
            Apply(Bone.LeftArm, Q(s * armAmp - reach - nX * 3f, R));
            Apply(Bone.RightArm, Q(-s * armAmp - reach + nX * 3f, R));
        }

        /// <summary>Re-pose one bone: base (idle) pose + a world-space offset, blended with whatever the Animator produced.</summary>
        void Apply(Bone b, Quaternion worldOffset)
        {
            Transform tr = bones[(int)b];
            if (tr == null) return;

            Quaternion animLocal = tr.localRotation;
            tr.localRotation = baseRot[(int)b];
            tr.rotation = worldOffset * tr.rotation;
            Quaternion target = tr.localRotation;
            tr.localRotation = Quaternion.Slerp(animLocal, target, weight);
        }

        static Quaternion Q(float degrees, Vector3 axis)
        {
            return Quaternion.AngleAxis(degrees, axis);
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                Transform r = FindDeep(child, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
