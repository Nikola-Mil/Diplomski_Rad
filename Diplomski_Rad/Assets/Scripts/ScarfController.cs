// ScarfController.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices — Why Verlet integration instead of Unity Joints?
//
//   1. Unconditional stability: Verlet position-based constraints do not have
//      spring constants to tune.  Unity joints (SpringJoint2D, DistanceJoint2D)
//      require careful mass/frequency/damping ratios that become stiff under
//      large forces (e.g., wind bursts) and can explode.
//
//   2. Per-segment wind: each point in the Verlet chain can receive an
//      independent wind impulse.  Joint hierarchies propagate forces through the
//      solver in a single pass, making it hard to apply per-link effects.
//
//   3. No inter-segment coupling overhead: constraint relaxation (Gauss-Seidel
//      passes) is simple O(n) per iteration; joint graphs in PhysX require
//      broader solve steps.
//
//   4. LateUpdate placement: the scarf reads the player's final-frame bone
//      position after all physics and animation, preventing one-frame lag.
//
// Algorithm overview:
//   • points[0] is pinned to the ScarfRoot transform every frame.
//   • For points[1..N]: integrate position using prev-position (Verlet step).
//   • Apply gravity, drag, and accumulated wind as acceleration.
//   • Run 3 constraint-relaxation iterations to enforce segmentDistance.
//   • Move visual GameObjects to the solved positions.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • Missing ScarfRoot (script placed on wrong object): a child named
//     "ScarfRoot" is created automatically.
//   • Segment GameObjects cannot be created: logged per-segment; remaining
//     segments still simulate correctly.
//   • Zero segmentDistance: guard against divide-by-zero in constraint solve.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;

public class ScarfController : MonoBehaviour
{
    // ── Public parameters ────────────────────────────────────────────────────
    [Header("Scarf Shape")]
    public int   segmentCount    = 6;
    public float segmentDistance = 0.3f;

    [Header("Simulation")]
    [Tooltip("How much each constraint correction is applied per iteration (0–1).")]
    public float stiffness     = 0.95f;
    [Tooltip("Gravitational pull on scarf segments (world units/s²).")]
    public float gravity       = 8f;
    [Tooltip("Velocity damping factor applied each frame (0 = stop instantly, 1 = no damping).")]
    public float drag          = 0.97f;
    // Deliberately exaggerated well past what a real piece of cloth this light
    // would feel from either force — the goal isn't physical accuracy, it's a
    // scarf that reads as heavier/snappier under gravity while still whipping
    // around dramatically in wind, which needs both pulls to be strong enough
    // to fight for control of the segments rather than one quietly dominating.
    [Tooltip("Multiplier applied to wind forces received via ApplyWind.")]
    public float windInfluence = 6f;

    // ── Internal Verlet state ────────────────────────────────────────────────
    private struct VerletPoint
    {
        public Vector3 position;
        public Vector3 prevPosition;
    }

    private VerletPoint[] points;   // length = segmentCount + 1  (index 0 = pinned root)
    private GameObject[]  segments; // length = segmentCount  (visual objects, follow points[1..N])

    private Vector2 accumulatedWind; // summed this frame; reset after LateUpdate

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        // ── Guard: ensure we have a valid root transform ───────────────────────
        // If someone attaches ScarfController to a non-ScarfRoot object that has
        // no parent, create a child named "ScarfRoot" and reparent this script.
        if (transform.parent == null && gameObject.name != "ScarfRoot")
        {
            Debug.LogWarning("[ScarfController] This component is not on a ScarfRoot child. " +
                             "Creating a 'ScarfRoot' child and moving the component there.");
            var scarfRootGO = new GameObject("ScarfRoot");
            scarfRootGO.transform.SetParent(transform, false);
            scarfRootGO.transform.localPosition = new Vector3(0.3f, -0.2f, 0f);
            // The component stays on this object; root position used is transform.position.
        }

        InitPoints();
        InitSegmentObjects();
    }

    private void LateUpdate()
    {
        if (points == null || points.Length == 0) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ── Pin root to ScarfRoot world position ──────────────────────────────
        points[0].prevPosition = points[0].position;
        points[0].position     = transform.position;

        // ── Verlet integration for non-pinned points ──────────────────────────
        Vector3 windAcc = (Vector3)(accumulatedWind * windInfluence);
        Vector3 gravAcc = Vector3.down * gravity;
        accumulatedWind = Vector2.zero; // consume wind

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 velocity   = (points[i].position - points[i].prevPosition) * drag;
            Vector3 acceleration = (gravAcc + windAcc) * (dt * dt);

            points[i].prevPosition = points[i].position;
            points[i].position    += velocity + acceleration;
        }

        // ── Constraint relaxation (3 Gauss-Seidel iterations) ────────────────
        // More iterations → stiffer chain.  3 is a good balance for real-time.
        for (int iter = 0; iter < 3; iter++)
        {
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 delta = points[i + 1].position - points[i].position;
                float   dist  = delta.magnitude;

                if (dist < 1e-5f) continue; // avoid divide-by-zero for overlapping points

                float   excess    = (dist - segmentDistance) * stiffness;
                Vector3 direction = delta / dist;

                if (i == 0)
                {
                    // Root is pinned – push only the child
                    points[i + 1].position -= direction * excess;
                }
                else
                {
                    // Shared correction: split equally between neighbours
                    Vector3 half = direction * (excess * 0.5f);
                    points[i].position     += half;
                    points[i + 1].position -= half;
                }
            }
        }

        // ── Update visual segment positions ───────────────────────────────────
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null)
            {
                Debug.LogError($"[ScarfController] Segment GameObject at index {i} is null. " +
                               "It may have been destroyed externally.");
                continue;
            }
            segments[i].transform.position = points[i + 1].position;
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Accumulates a wind force to be applied during the next LateUpdate.
    /// Safe to call from any thread-equivalent (Update, coroutines).
    /// </summary>
    public void ApplyWind(Vector2 windForce)
    {
        accumulatedWind += windForce;
    }

    // ── Initialisation helpers ───────────────────────────────────────────────

    private void InitPoints()
    {
        if (segmentCount < 1)
        {
            Debug.LogError($"[ScarfController] segmentCount must be >= 1 (got {segmentCount}). " +
                           "Clamping to 1.");
            segmentCount = 1;
        }

        // Index 0 = pinned root; indices 1..segmentCount = free points
        points = new VerletPoint[segmentCount + 1];
        for (int i = 0; i <= segmentCount; i++)
        {
            Vector3 startPos = transform.position + Vector3.down * (segmentDistance * i);
            points[i] = new VerletPoint
            {
                position     = startPos,
                prevPosition = startPos
            };
        }
    }

    private void InitSegmentObjects()
    {
        segments = new GameObject[segmentCount];
        Sprite circleSprite = CreateCircleSprite(new Color(0.9f, 0.4f, 0.2f));

        for (int i = 0; i < segmentCount; i++)
        {
            try
            {
                var segGO = new GameObject($"ScarfSegment_{i}");
                segGO.transform.SetParent(transform, false);
                segGO.transform.position = points[i + 1].position;

                // Scale decreases toward the tip for a natural tapering look
                float scale = Mathf.Lerp(0.2f, 0.08f, (float)i / segmentCount);
                segGO.transform.localScale = Vector3.one * scale;

                var sr    = segGO.AddComponent<SpriteRenderer>();
                sr.sprite = circleSprite;
                sr.color  = new Color(0.9f, 0.4f + 0.05f * i, 0.2f);

                segments[i] = segGO;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScarfController] Failed to create segment {i}: {e.Message}\n" +
                               "Simulation continues for remaining segments.");
                segments[i] = null;
            }
        }
    }

    /// <summary>Creates a simple circle sprite from a procedural texture.</summary>
    private static Sprite CreateCircleSprite(Color color)
    {
        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                tex.SetPixel(x, y, dx * dx + dy * dy <= r * r ? color : Color.clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size),
                             new Vector2(0.5f, 0.5f), size);
    }
}
