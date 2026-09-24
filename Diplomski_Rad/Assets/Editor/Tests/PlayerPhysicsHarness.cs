// PlayerPhysicsHarness.cs
// Drives the REAL PlayerController and the real Box2D solver headlessly inside
// an EditMode test. Physics is switched to script simulation and stepped by
// hand, calling the controller's own private FixedUpdate / Jump / PerformDash
// via reflection in the same order the engine uses at runtime
// (FixedUpdate → physics step → WaitForFixedUpdate coroutines). Nothing about
// the controller is re-implemented here, so whatever these tests measure is
// what the game actually does.
//
// The player itself comes from BuildGameScene.CreatePlayer, i.e. the exact
// body (mass, gravityScale, collider, material) the scene builder creates.

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class PlayerPhysicsHarness : IDisposable
{
    // 1x1 BoxCollider2D + 0.05 edgeRadius on every side (see BuildGameScene.CreatePlayer).
    public const float HALF_WIDTH = 0.55f;
    public const float HEIGHT     = 1.1f;

    private const BindingFlags PRIV = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo mAwake       = typeof(PlayerController).GetMethod("Awake", PRIV);
    private static readonly MethodInfo mFixedUpdate = typeof(PlayerController).GetMethod("FixedUpdate", PRIV);
    private static readonly MethodInfo mJump        = typeof(PlayerController).GetMethod("Jump", PRIV);
    private static readonly MethodInfo mPerformDash = typeof(PlayerController).GetMethod("PerformDash", PRIV);
    private static readonly MethodInfo mOnDisable   = typeof(PlayerController).GetMethod("OnDisable", PRIV);
    private static readonly MethodInfo mOnDestroy   = typeof(PlayerController).GetMethod("OnDestroy", PRIV);
    private static readonly FieldInfo  fInput       = typeof(PlayerController).GetField("horizontalInput", PRIV);

    public readonly PlayerController Pc;
    public readonly Rigidbody2D      Rb;
    public readonly BoxCollider2D    Col;
    public readonly int              GroundLayer;

    private readonly TempScene         scene;
    private readonly SimulationMode2D  previousMode;
    private readonly PhysicsMaterial2D zeroFriction;

    public PlayerPhysicsHarness()
    {
        Assert.IsNotNull(mFixedUpdate, "PlayerController.FixedUpdate not found — was it renamed?");
        Assert.IsNotNull(mJump,        "PlayerController.Jump not found — was it renamed?");
        Assert.IsNotNull(fInput,       "PlayerController.horizontalInput not found — was it renamed?");

        scene        = new TempScene();
        previousMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;

        GroundLayer = LayerMask.NameToLayer("Ground");
        Assert.AreNotEqual(-1, GroundLayer, "'Ground' layer missing — run Tools/Build Platformer Scene once.");
        zeroFriction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Tiles/ZeroFriction.physicsMaterial2D");

        var go = BuildGameScene.CreatePlayer(new Vector3(0f, 100f, 0f), GroundLayer);
        Pc  = go.GetComponent<PlayerController>();
        Rb  = go.GetComponent<Rigidbody2D>();
        Col = go.GetComponent<BoxCollider2D>();
        // Awake doesn't run for non-[ExecuteAlways] scripts in edit mode.
        mAwake.Invoke(Pc, null);
    }

    public void Dispose()
    {
        // Same reason as Awake: dispose the controller's InputActions by hand.
        if (Pc != null)
        {
            mOnDisable?.Invoke(Pc, null);
            mOnDestroy?.Invoke(Pc, null);
        }
        Physics2D.simulationMode = previousMode;
        scene.Dispose();
    }

    /// <summary>Static solid block on the Ground layer, like one run of tilemap tiles.</summary>
    public GameObject AddBlock(float xMin, float xMax, float yMin, float yMax)
    {
        var go = new GameObject("TestBlock") { layer = GroundLayer };
        go.transform.position = new Vector3((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, 0f);
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        var bc = go.AddComponent<BoxCollider2D>();
        bc.size           = new Vector2(xMax - xMin, yMax - yMin);
        bc.sharedMaterial = zeroFriction;
        Physics2D.SyncTransforms();
        return go;
    }

    /// <summary>Bottom of the collider (edge radius included) in world units.</summary>
    public float Feet => Rb.position.y - HEIGHT * 0.5f;

    /// <summary>Teleports the player so its feet are at feetY.</summary>
    public void Place(float centreX, float feetY, Vector2 velocity)
    {
        var p = new Vector2(centreX, feetY + HEIGHT * 0.5f);
        Pc.transform.position = p;
        Rb.position           = p;
        Rb.linearVelocity     = velocity;
        Physics2D.SyncTransforms();
    }

    /// <summary>One 50 Hz physics tick with the given held horizontal input (-1..1).</summary>
    public void Step(float input)
    {
        fInput.SetValue(Pc, input);
        mFixedUpdate.Invoke(Pc, null);
        Physics2D.Simulate(Time.fixedDeltaTime);
    }

    /// <summary>The controller's own jump (a direct write of linearVelocity.y = jumpForce).</summary>
    public void Jump() => mJump.Invoke(Pc, null);

    /// <summary>
    /// Runs a full dash through the controller's own PerformDash coroutine.
    /// afterEachStep is called after every physics tick (dash ticks and the
    /// extraSteps ticks that follow it).
    /// </summary>
    public void Dash(Vector2 direction, float distance, int extraSteps, Action afterEachStep)
    {
        var dash = (IEnumerator)mPerformDash.Invoke(Pc, new object[] { direction.normalized, distance });
        // StartCoroutine runs up to the first yield synchronously.
        bool running = dash.MoveNext();
        int  guard   = 0;
        while (running && guard++ < 500)
        {
            mFixedUpdate.Invoke(Pc, null);           // early-outs while dashing, as at runtime
            Physics2D.Simulate(Time.fixedDeltaTime);
            afterEachStep();
            running = dash.MoveNext();               // resumes after WaitForFixedUpdate
        }
        for (int i = 0; i < extraSteps; i++)
        {
            Step(0f);
            afterEachStep();
        }
    }
}

/// <summary>
/// Swaps the open scene(s) for an empty temporary scene and restores them
/// afterwards. Refuses to run over unsaved changes rather than discarding them.
/// </summary>
internal sealed class TempScene : IDisposable
{
    private readonly SceneSetup[] previous;

    public TempScene()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                Assert.Ignore("Save the open scene(s) first — this test temporarily replaces them.");

        previous = EditorSceneManager.GetSceneManagerSetup();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    public void Dispose()
    {
        Undo.ClearAll();
        bool restorable = previous.Length > 0 && Array.TrueForAll(previous, s => !string.IsNullOrEmpty(s.path));
        if (restorable) EditorSceneManager.RestoreSceneManagerSetup(previous);
        else            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}
