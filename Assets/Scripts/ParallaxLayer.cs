// ParallaxLayer.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices:
//   • Primary strategy: material UV offset (mainTextureOffset).  This produces
//     a seamless infinite-scroll effect when the sprite's texture is set to
//     "Repeat" wrap mode.  A per-instance material copy is created in Awake so
//     the shared Sprites-Default material is never mutated.
//   • Fallback strategy: transform-position parallax.  When the renderer has no
//     _MainTex property (e.g., Sprite Atlas, URP Lit material), the layer's world
//     position is shifted by (cameraDelta × scrollSpeed), which is the classic
//     multi-plane parallax formula.
//   • scrollSpeed doubles as the parallax factor for the transform path:
//       - scrollSpeed = 0.1  →  layer moves at 10 % of camera speed (very far)
//       - scrollSpeed = 0.6  →  layer moves at 60 % of camera speed (near)
//   • The per-instance material is destroyed in OnDestroy to avoid Editor leaks.
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • No Renderer at Awake: logs an error and disables the component so Update
//     is never called, avoiding repeated log spam.
//   • material.mainTextureOffset throws at runtime: logs the error, clears the
//     instance-material reference, and automatically switches to transform path.
//   • referenceCamera stays null (Camera.main not found): skips the frame silently
//     until Camera.main becomes available.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    [Tooltip("Scroll / parallax factor per axis.  0 = stationary, 1 = locked to camera.")]
    public Vector2 scrollSpeed = new Vector2(0.1f, 0f);

    [Tooltip("Leave null to use Camera.main automatically.")]
    public Camera referenceCamera;

    // ── Private state ────────────────────────────────────────────────────────
    private Renderer    rend;
    private Material    instanceMaterial;   // owned copy, not shared
    private bool        useMaterialOffset;

    private Vector3     initialPosition;
    private Vector3     initialCameraPos;
    private bool        cameraInitialized;

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

        // Attempt to grab an instance material for the offset technique.
        try
        {
            instanceMaterial   = rend.material;  // .material auto-creates an instance
            useMaterialOffset  = instanceMaterial != null &&
                                  instanceMaterial.HasProperty("_MainTex");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ParallaxLayer] Could not access material on '{gameObject.name}': " +
                             $"{e.Message}  →  falling back to transform parallax.");
            useMaterialOffset = false;
        }

        // Resolve camera immediately if possible.
        if (referenceCamera == null)
            referenceCamera = Camera.main;

        if (referenceCamera != null)
        {
            initialCameraPos   = referenceCamera.transform.position;
            cameraInitialized  = true;
        }
    }

    private void Update()
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

        if (useMaterialOffset)
            ScrollMaterial();
        else
            ScrollTransform();
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

    private void ScrollMaterial()
    {
        if (instanceMaterial == null)
        {
            Debug.LogError($"[ParallaxLayer] Instance material on '{gameObject.name}' became null " +
                           "at runtime – switching to transform parallax.");
            useMaterialOffset = false;
            return;
        }

        try
        {
            Vector2 offset = instanceMaterial.mainTextureOffset;
            offset += scrollSpeed * Time.deltaTime;
            instanceMaterial.mainTextureOffset = offset;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ParallaxLayer] mainTextureOffset error on '{gameObject.name}': " +
                           $"{e.Message}  →  switching to transform parallax.");
            useMaterialOffset = false;
        }
    }

    private void ScrollTransform()
    {
        Vector3 cameraDelta = referenceCamera.transform.position - initialCameraPos;

        // The layer moves at scrollSpeed fraction of the camera delta.
        // Layers with low scrollSpeed feel farther away; high = closer.
        transform.position = new Vector3(
            initialPosition.x + cameraDelta.x * scrollSpeed.x,
            initialPosition.y + cameraDelta.y * scrollSpeed.y,
            initialPosition.z
        );
    }
}
