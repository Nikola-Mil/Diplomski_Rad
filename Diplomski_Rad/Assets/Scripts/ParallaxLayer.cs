// ParallaxLayer.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • Drift mode (useTilingScroll = false, the default): the classic
//     transform-lag formula (position = initial + cameraDelta * scrollSpeed).
//     No tiling to hide a finite sprite size, so bake a repeating pattern
//     directly into a generously-sized sprite (see BuildGameScene.cs's
//     CreateTriangleParallaxLayer) rather than relying on a tiny tile —
//     size it relative to scrollSpeed: the slower a layer scrolls, the more
//     it drifts relative to the camera over a normal play session, so it
//     needs proportionally more world-space coverage.
//   • Tiling mode (useTilingScroll = true — for a repeating background
//     pattern) exists for the theoretically-better case: the layer's
//     TRANSFORM stays camera-locked (always centred on the viewport at its
//     own Z) so it can never run out of coverage regardless of camera travel,
//     and the depth illusion comes entirely from scrolling the texture's UV
//     offset instead. In practice this has NOT been confirmed to work —
//     SpriteRenderer batching can silently ignore per-material texture
//     offset — so it's opt-in, not the default, until verified.
//   • Both modes scroll proportionally to CAMERA POSITION DELTA, not elapsed
//     time — a treadmill-style time-based scroll would keep moving even while
//     the camera is standing still, which isn't parallax, it's autoscroll.
//   • A per-instance material copy is created in Awake so the shared
//     Sprites-Default material is never mutated by one layer's UV offset.
//   • LateUpdate (not Update) so this reads the camera's position after
//     CameraController has already moved it for the frame.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • No Renderer at Awake: logs an error and disables the component so Update
//     is never called, avoiding repeated log spam.
//   • Material has no _MainTex (e.g. some URP/Lit materials): tiling mode is
//     forced off regardless of useTilingScroll, falling back to drift mode.
//   • material.mainTextureOffset throws at runtime: logs the error and
//     switches to drift mode for the rest of the session.
//   • referenceCamera stays null (Camera.main not found): skips the frame
//     silently until Camera.main becomes available.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    [Tooltip("Parallax factor per axis. 0 = appears infinitely far away (never seems to shift), " +
             "1 = right at the camera (locked, no depth illusion). Background layers: 0.05–0.5.")]
    public Vector2 scrollSpeed = new Vector2(0.1f, 0f);

    [Tooltip("True (for a repeating background pattern): stays camera-locked in position and " +
             "scrolls its texture via UV offset, so it can never run out of coverage no matter how " +
             "far the camera travels. NOTE: unreliable in practice — SpriteRenderer batching can " +
             "silently ignore per-material texture offset, so this mode is unverified; prefer false " +
             "unless you've confirmed it actually scrolls for your setup. False (default): drifts " +
             "using the classic transform-lag parallax instead — size the sprite generously (bake a " +
             "repeating pattern directly into its texture rather than relying on tiling) to avoid " +
             "edge gaps.")]
    public bool useTilingScroll = false;

    [Tooltip("Leave null to use Camera.main automatically.")]
    public Camera referenceCamera;

    // ── Private state ────────────────────────────────────────────────────────
    private Renderer    rend;
    private Material    instanceMaterial;   // owned copy, not shared
    private bool        useMaterialOffset;

    private Vector3     initialPosition;
    private Vector3     initialCameraPos;
    private bool        cameraInitialized;

    // World-space size of the sprite, used to convert a world-unit camera
    // delta into a UV-space texture offset delta — one full UV repeat (1.0)
    // corresponds to exactly one sprite-width/height of world movement.
    private Vector2     worldSize = Vector2.one;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        rend = GetComponent<Renderer>();
        if (rend == null)
        {
            Debug.LogError($"[ParallaxLayer] '{gameObject.name}' has no Renderer component. " +
                           "Attach a SpriteRenderer or MeshRenderer. " +
                           "The ParallaxLayer script is now disabled.");
            enabled = false;
            return;
        }

        initialPosition = transform.position;
        worldSize = new Vector2(
            Mathf.Max(0.0001f, rend.bounds.size.x),
            Mathf.Max(0.0001f, rend.bounds.size.y));

        // Attempt to grab an instance material for the UV-offset technique.
        bool materialSupportsOffset;
        try
        {
            instanceMaterial      = rend.material;  // .material auto-creates an instance
            materialSupportsOffset = instanceMaterial != null &&
                                      instanceMaterial.HasProperty("_MainTex");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ParallaxLayer] Could not access material on '{gameObject.name}': " +
                             $"{e.Message}  →  falling back to drift parallax.");
            materialSupportsOffset = false;
        }
        useMaterialOffset = useTilingScroll && materialSupportsOffset;

        // Resolve camera immediately if possible.
        if (referenceCamera == null)
            referenceCamera = Camera.main;

        if (referenceCamera != null)
        {
            initialCameraPos   = referenceCamera.transform.position;
            cameraInitialized  = true;
        }
    }

    private void LateUpdate()
    {
        // Lazily resolve camera in case it wasn't ready at Awake.
        if (referenceCamera == null)
        {
            referenceCamera = Camera.main;
            if (referenceCamera == null) return;
        }

        if (!cameraInitialized)
        {
            initialCameraPos  = referenceCamera.transform.position;
            cameraInitialized = true;
        }

        Vector3 cameraDelta = referenceCamera.transform.position - initialCameraPos;

        if (useMaterialOffset)
            ScrollMaterial(cameraDelta);
        else
            ScrollTransform(cameraDelta);
    }

    private void OnDestroy()
    {
        // Destroy the per-instance material to avoid memory leaks in the Editor.
        if (instanceMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(instanceMaterial);
            else
                DestroyImmediate(instanceMaterial);
        }
    }

    // ── Scroll strategies ────────────────────────────────────────────────────

    private void ScrollMaterial(Vector3 cameraDelta)
    {
        // Camera-locked position (see class comment for why) — this is what
        // guarantees the layer can never run out of coverage.
        transform.position = new Vector3(
            referenceCamera.transform.position.x,
            referenceCamera.transform.position.y,
            initialPosition.z);

        if (instanceMaterial == null)
        {
            Debug.LogError($"[ParallaxLayer] Instance material on '{gameObject.name}' became null " +
                           "at runtime – switching to drift parallax.");
            useMaterialOffset = false;
            return;
        }

        try
        {
            Vector2 uvDelta = new Vector2(
                cameraDelta.x / worldSize.x * scrollSpeed.x,
                cameraDelta.y / worldSize.y * scrollSpeed.y);
            instanceMaterial.mainTextureOffset = uvDelta;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ParallaxLayer] mainTextureOffset error on '{gameObject.name}': " +
                           $"{e.Message}  →  switching to drift parallax.");
            useMaterialOffset = false;
        }
    }

    private void ScrollTransform(Vector3 cameraDelta)
    {
        // scrollSpeed is defined as "how much this layer appears to move ON
        // SCREEN relative to camera movement" (1 = moves fully, like a normal
        // static world object right next to the player, e.g. gameplay
        // geometry sliding past as you walk; 0 = doesn't appear to move at
        // all, like something infinitely far away) — the intuitive
        // convention where CLOSER layers get a HIGHER value.
        //
        // The WORLD-SPACE tracking factor needed to actually produce that
        // on-screen behaviour is the complement of scrollSpeed, not
        // scrollSpeed itself: a layer that should barely move on screen (low
        // scrollSpeed, i.e. far away) has to track the camera's world
        // position CLOSELY (a tracking factor near 1), while a layer that
        // should appear to slide past at nearly full rate (high scrollSpeed,
        // i.e. close) must barely track the camera at all (a tracking factor
        // near 0), the same way ordinary un-parallaxed world geometry doesn't
        // track the camera and so appears to move past it at full speed.
        // Using scrollSpeed directly here (as an earlier version of this
        // method did) inverts the whole effect: "near" layers end up nearly
        // locked to the screen and "far" layers end up sliding past the most
        // — exactly backwards.
        Vector2 trackingFactor = Vector2.one - scrollSpeed;
        transform.position = new Vector3(
            initialPosition.x + cameraDelta.x * trackingFactor.x,
            initialPosition.y + cameraDelta.y * trackingFactor.y,
            initialPosition.z
        );
    }
}
