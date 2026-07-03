using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public static class BuildGameScene
{
    // Regular scene occupies x = -15 to 20 (cell coords).
    // Proc-gen zone starts at x = 21 so both share the same tilemap seamlessly.
    private const int SCENE_LEFT  = -15;
    private const int SCENE_RIGHT =  20;
    private const int FLOOR_Y     =  -4;   // cell row; surface at world y = -3
    // Camera orthographicSize is 9.5, so the bottom edge of the view can sit as
    // much as ~9.5 world units below whatever the camera is centred on — far
    // deeper than the old 2-tile floor, which let empty background show below
    // it any time the camera dipped even slightly. 20 tiles comfortably covers
    // that plus dash/catch-up slack.
    private const int FLOOR_THICKNESS = 20;

    [MenuItem("Tools/Build Platformer Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Dedicated layer for solid ground only. PlayerController.IsGrounded()
        // matches against this layer specifically — everything else (WindZone's
        // large trigger volume, pickups, spikes, enemies) stays off it, so none
        // of them can ever be misdetected as "standing on ground" no matter
        // their size or where they overlap the player. Previously ground
        // detection matched the whole "Default" layer, which the WindZone's
        // 30x10 trigger also sat on — inside that box (which visually lines up
        // with the parallax background) the player always read as grounded,
        // making jump available nonstop; outside it (e.g. the tower, well above
        // the trigger's y range) ground detection worked correctly.
        int groundLayerIndex = EnsureLayer("Ground");

        // ── 1. Main Camera ────────────────────────────────────────────────────
        try
        {
            var camGO = ObjectFactory.CreateGameObject("Main Camera",
                typeof(Camera), typeof(AudioListener));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            cam.orthographic     = true;
            // Air's aim-range circle (radius = dashMaxDistance = 8) needs at least
            // 8 units of vertical half-extent to fit on screen at all; 9.5 leaves
            // a comfortable margin around it instead of touching the screen edge.
            cam.orthographicSize = 9.5f;
            // Dark, neutral cave tone instead of navy — matches the parallax
            // background layers instead of clashing with them.
            cam.backgroundColor  = new Color(0.03f, 0.025f, 0.03f, 1f);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            camGO.AddComponent<CameraController>();
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] Camera: {e.Message}"); }

        // ── 2. Grid + Tilemap ─────────────────────────────────────────────────
        GameObject tilemapGO = null;
        try
        {
            var gridGO = ObjectFactory.CreateGameObject("Grid", typeof(Grid));
            tilemapGO  = ObjectFactory.CreateGameObject("GroundTilemap",
                typeof(Tilemap), typeof(TilemapRenderer));
            tilemapGO.transform.SetParent(gridGO.transform, false);
            tilemapGO.layer = groundLayerIndex;

            var tm = tilemapGO.GetComponent<Tilemap>();

            // Static Rigidbody2D makes the tilemap a proper static physics body.
            var tilemapRb = tilemapGO.AddComponent<Rigidbody2D>();
            tilemapRb.bodyType = RigidbodyType2D.Static;

            // TilemapCollider2D without CompositeCollider2D.
            // CompositeCollider2D doesn't generate geometry reliably when the tilemap
            // is built entirely from an editor script (shapes don't survive the
            // Edit→Play transition). TilemapCollider2D alone is straightforward:
            // with Tile.ColliderType.Grid every tile is an aligned 1×1 box, so
            // adjacent tiles share exact edges — but Box2D can still report a
            // phantom collision against the internal vertex where two of those
            // per-tile boxes meet, catching the player as if on a tiny ledge.
            // A friction-less shared material removes the tangential drag that
            // turns that phantom contact into a visible snag.
            var groundCollider = tilemapGO.AddComponent<TilemapCollider2D>();
            groundCollider.sharedMaterial = GetZeroFrictionMaterial();

            // Always delete and recreate the tile so PPU and colliderType are
            // guaranteed correct every run — stale assets from prior builds persist
            // with wrong settings and produce invisible tiles or broken collisions.
            AssetDatabase.DeleteAsset("Assets/Tiles/GroundTile.asset");
            Tile tile = CreateFallbackTile();

            if (tile != null)
            {
                // Floor: FLOOR_THICKNESS tiles thick — prevents player seeing empty
                // space below it even at the camera's full vertical extent.
                // Extends to SCENE_RIGHT so proc-gen zone connects flush.
                for (int x = SCENE_LEFT; x <= SCENE_RIGHT; x++)
                    for (int dy = 0; dy < FLOOR_THICKNESS; dy++)
                        tm.SetTile(new Vector3Int(x, FLOOR_Y - dy, 0), tile);

                // Floating platforms (cell y=-2 → surface at world y=-1;
                //                     cell y=-1 → surface at world y=0).
                // Gaps between platforms are 2-3 tiles wide for fast movement.
                PlacePlatformTiles(tm, tile, -13, -10, -2);  // far-left  (low)
                PlacePlatformTiles(tm, tile,  -7,  -3, -1);  // left      (high)
                PlacePlatformTiles(tm, tile,   1,   5, -2);  // centre    (low)
                PlacePlatformTiles(tm, tile,   8,  12, -1);  // right     (high)
                PlacePlatformTiles(tm, tile,  15,  19, -2);  // far-right (low, leads to proc-gen)

                // Vertical climbing tower, straight up from the "right (high)"
                // platform — dedicated space to test platforming on a tall
                // level (chained jumps, air-dashes) rather than the mostly-flat
                // main layout above.
                int towerTopCellY = PlaceVerticalTower(tm, tile);

                tm.RefreshAllTiles();
                EditorUtility.SetDirty(tilemapGO);

                Debug.Log("[BuildGameScene] Tiles: floor (-15..20) + 5 platforms + " +
                          $"vertical tower (top cell y={towerTopCellY}) placed.");
            }
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] Tilemap: {e.Message}"); }

        // ── 3. Player ─────────────────────────────────────────────────────────
        try
        {
            var playerGO = ObjectFactory.CreateGameObject("Player",
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            playerGO.tag = "Player";
            // Dedicated layer, separate from the ground tilemap (which stays on
            // Default). Previously both were on Default and PlayerController's
            // IsGrounded() ground-check BoxCast relied on a 0.01-unit gap alone
            // to avoid detecting the player's OWN collider as "ground" — with
            // edgeRadius, fast falls, and Continuous collision all inflating the
            // effective collision bounds, that gap wasn't reliable, and a
            // self-detected "always grounded" reading is exactly what an
            // infinite-jump bug looks like (jump is available every press,
            // regardless of actual height). Being on a different layer makes
            // that class of bug structurally impossible rather than relying on
            // a distance epsilon.
            playerGO.layer = EnsureLayer("Player");
            // x=-12 sits directly above the "far-left (low)" platform (cells
            // -13..-10 at cell y=-2, i.e. world y=[-2,-1], surface at y=-1) —
            // spawning at the old y=-2 put the player's collider (extents
            // ±0.5) squarely inside that platform's tile instead of on the
            // floor beneath it. Spawn standing on the platform's surface
            // instead (surface + half-height = -1 + 0.5 = -0.5).
            playerGO.transform.position = new Vector3(-12f, -0.5f, 0f);

            playerGO.GetComponent<SpriteRenderer>().sprite =
                CreateSquareSprite(new Color(0.3f, 0.8f, 1f));

            var pRb = playerGO.GetComponent<Rigidbody2D>();
            // Raised from 3 alongside PlayerController.jumpForce (14→17): higher
            // gravity + higher launch velocity keeps jump height about the same
            // but roughly halves hang time so jumps feel snappy, not floaty.
            pRb.gravityScale           = 4.5f;
            pRb.constraints            = RigidbodyConstraints2D.FreezeRotation;
            pRb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            // Interpolation smooths the rendered position between 50 Hz physics steps.
            pRb.interpolation          = RigidbodyInterpolation2D.Interpolate;

            // BoxCollider2D auto-sizes to the SpriteRenderer bounds at creation time,
            // but the sprite hasn't been assigned yet at that point — so Unity records
            // a degenerate 0.0001×0.0001 size.  Explicitly set it to match the 1×1
            // world-unit sprite (16 px at 16 PPU).
            var pCol = playerGO.GetComponent<BoxCollider2D>();
            if (pCol != null)
            {
                pCol.size = new Vector2(1f, 1f);
                // Rounds the collider's corners slightly so it slides over the internal
                // vertex between adjacent tile colliders instead of snagging on it —
                // the standard fix for "sticking" on flat multi-tile ground/ceilings.
                pCol.edgeRadius   = 0.05f;
                pCol.sharedMaterial = GetZeroFrictionMaterial();
            }

            var pc = playerGO.AddComponent<PlayerController>();
            if (pc != null)
            {
                // Start with no element picked up yet — PlayerController.Awake()
                // also defaults here, but setting it explicitly keeps the scene
                // in sync with the neutral (short, Fire-speed) baseline dash.
                pc.currentElement = PlayerController.CreateNeutralElement();
                pc.groundLayer    = 1 << groundLayerIndex;
            }

            var scarfRoot = ObjectFactory.CreateGameObject("ScarfRoot");
            scarfRoot.transform.SetParent(playerGO.transform, false);
            scarfRoot.transform.localPosition = new Vector3(0.3f, -0.2f, 0f);
            scarfRoot.AddComponent<ScarfController>();
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] Player: {e.Message}"); }

        // ── 4. Parallax Backgrounds ───────────────────────────────────────────
        // Placeholder cave look: three layers, deepest first. Aesthetics don't
        // matter yet (this is a stand-in for real art) — what matters is that
        // the parallax itself is correct. Plain transform-drift parallax (the
        // same technique the project already had, just with the time-based-
        // scroll bug fixed) rather than shader UV-offset scrolling — sprite
        // batching for SpriteRenderer can silently ignore per-material
        // texture offset, which is almost certainly why the triangle pattern
        // wasn't showing up at all last time. Each layer's pattern is instead
        // baked directly into a sprite sized generously for its own scroll
        // speed (the slower a layer scrolls, the more it drifts relative to
        // the camera over a normal play session, so it needs more world-space
        // coverage to avoid showing an edge).
        try
        {
            // Far: a single dark "cave hole" silhouette, barely drifts —
            // sized biggest since its very low scroll speed means it drifts
            // the most relative to the camera.
            CreateHoleParallaxLayer("ParallaxHole", new Vector3(0f, 0f, 6f),
                new Vector2(0.04f, 0.03f), 56f, new Color(0.05f, 0.045f, 0.05f));

            // Mid: smaller, darker triangles, moves at roughly half the rate
            // of the near layer — "less, because that's what parallax does".
            // Top (stalactite) sprite is much taller than the viewport and
            // shifted up (pos.y 0→3) so the top band is comfortably above the
            // ~30-unit tower for when you climb it — unchanged from before.
            CreateTriangleParallaxLayer("ParallaxMidTop", new Vector3(0f, 3f, 5f),
                new Vector2(0.4f, 0.3f), 100f, 54f, 24, 19f,
                false, new Color(0.16f, 0.14f, 0.13f));

            // Bottom (stalagmite) band used to share the top sprite's texture,
            // mirrored off the same random draws, and ended up buried around
            // world y ≈ -24..-5 — always below the floor and never visible. It
            // now gets its own independently-generated sprite, positioned so
            // its base sits underground (hidden behind the floor tiles) but
            // its tip pokes up to roughly world y ≈ 7 — comfortably inside the
            // normal play view. Same triangleWorldHeight (19) as the top band,
            // so individual triangles are still the same size as before.
            CreateTriangleParallaxLayer("ParallaxMidBottom", new Vector3(0f, 13f, 5f),
                new Vector2(0.4f, 0.3f), 100f, 50f, 24, 19f,
                true, new Color(0.16f, 0.14f, 0.13f));

            // Near: bigger, lighter gray triangles that move almost with the
            // camera, like cave walls right next to the player. Top band
            // unchanged from before (pos.y 18→7 pull-back).
            CreateTriangleParallaxLayer("ParallaxNearTop", new Vector3(0f, 7f, 4f),
                new Vector2(0.85f, 0.7f), 70f, 70f, 14, 14f,
                false, new Color(0.55f, 0.53f, 0.5f));

            // Bottom band, same treatment as Mid's: own sprite, lifted so its
            // tip (~world y ≈ 4.5) is clearly visible instead of buried
            // underground, same triangleWorldHeight (14) as the top band.
            CreateTriangleParallaxLayer("ParallaxNearBottom", new Vector3(0f, 13f, 4f),
                new Vector2(0.85f, 0.7f), 70f, 45f, 14, 14f,
                true, new Color(0.55f, 0.53f, 0.5f));
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] Parallax: {e.Message}"); }

        // ── 5. UI Canvas ──────────────────────────────────────────────────────
        try   { BuildUICanvas(); }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] UI: {e.Message}"); }

        // ── 6. WindZone ───────────────────────────────────────────────────────
        try
        {
            var windGO = ObjectFactory.CreateGameObject("WindZone", typeof(BoxCollider2D));
            var wc = windGO.GetComponent<BoxCollider2D>();
            wc.isTrigger = true;
            wc.size      = new Vector2(30f, 10f);
            windGO.AddComponent<WindZoneController>();

            var dragonPos = ObjectFactory.CreateGameObject("DragonPosition");
            dragonPos.transform.SetParent(windGO.transform, false);
            dragonPos.transform.position = new Vector3(25f, 15f, 0f);
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] WindZone: {e.Message}"); }

        // ── 7. Melee Enemy ────────────────────────────────────────────────────
        // Kinematic body — y must be set so feet touch the floor surface (world y=-3).
        // Collider half-height=0.75 (1x1.5 box, see below) → centre at
        // y=-3+0.75=-2.25 → feet at y=-3.
        try
        {
            var eGO = ObjectFactory.CreateGameObject("Enemy",
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            eGO.transform.position = new Vector3(5f, -2.25f, 0f);
            eGO.GetComponent<SpriteRenderer>().sprite =
                CreateCapsuleSprite(new Color(0.9f, 0.2f, 0.2f));
            var eRb = eGO.GetComponent<Rigidbody2D>();
            eRb.bodyType = RigidbodyType2D.Kinematic;
            var eCol = eGO.GetComponent<BoxCollider2D>();
            eCol.isTrigger = true;
            // BoxCollider2D auto-sizes from the SpriteRenderer at the moment it's
            // added, which is BEFORE the sprite assignment above — it locks in a
            // degenerate near-zero size otherwise, making the enemy's hitbox
            // effectively undetectable (16x24 px capsule sprite at 16 PPU = 1x1.5
            // world units).
            eCol.size = new Vector2(1f, 1.5f);
            var ep = eGO.AddComponent<EnemyPatrol>();
            if (ep != null) { ep.leftBound = 2f; ep.rightBound = 8f; }
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] Enemy: {e.Message}"); }

        // ── 8. Element Pickups ────────────────────────────────────────────────
        // Placed just above the platform surfaces so the player must jump to collect them.
        // Low platform surface: world y=-1  → pickup centre at y=-0.5
        // High platform surface: world y=0  → pickup centre at y=0.5
        try
        {
            SpawnElementPickup("EarthPickup",
                new Vector3(-11f, -0.5f, 0f), PlayerController.CreateEarthElement());
            SpawnElementPickup("FirePickup",
                new Vector3(  3f, -0.5f, 0f), PlayerController.CreateFireElement());
            SpawnElementPickup("WaterPickup",
                new Vector3( 10f,  0.5f, 0f), PlayerController.CreateWaterElement());
            // Far-right low platform (15..19, y=-2) → surface world y=-1 → centre y=-0.5.
            SpawnElementPickup("AirPickup",
                new Vector3( 17f, -0.5f, 0f), PlayerController.CreateAirElement());
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] Pickups: {e.Message}"); }

        // ── 9. Spike Hazards ──────────────────────────────────────────────────
        // Floor surface: world y=-3.  Spike scale=0.4 → half-height=0.2.
        // Centre at y=-2.8 → bottom at y=-3.0 (flush with surface).
        try
        {
            float[] spikeXs = { -12f, -6f, 3f, 9f, 14f };
            for (int i = 0; i < spikeXs.Length; i++)
                SpawnSpikeHazard($"Spike_{i}", new Vector3(spikeXs[i], -2.8f, 0f));
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] Spikes: {e.Message}"); }

        // ── 10. Ranged Enemy ──────────────────────────────────────────────────
        // On high platform (-7..-3, y=-1) → surface world y=0. Collider
        // half-height=0.75 (1x1.5 box) → centre at y=0+0.75=0.75.
        try
        {
            var rGO = ObjectFactory.CreateGameObject("RangedEnemy",
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            rGO.transform.position = new Vector3(-5f, 0.75f, 0f);
            rGO.GetComponent<SpriteRenderer>().sprite =
                CreateCapsuleSprite(new Color(0.9f, 0.5f, 0.1f));
            var rRb = rGO.GetComponent<Rigidbody2D>();
            rRb.bodyType = RigidbodyType2D.Kinematic;
            var rCol = rGO.GetComponent<BoxCollider2D>();
            rCol.isTrigger = true;
            // See the melee Enemy above: auto-sized before the sprite was assigned.
            rCol.size = new Vector2(1f, 1.5f);
            var rp = rGO.AddComponent<EnemyPatrol>();
            if (rp != null)
            {
                rp.leftBound     = -7f;
                rp.rightBound    = -3f;
                rp.shootInterval = 2f;
                rp.shootRange    = 12f;
            }
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] RangedEnemy: {e.Message}"); }

        // ── Save ──────────────────────────────────────────────────────────────
        try
        {
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/PlatformerScene.unity");
            AssetDatabase.Refresh();
            Debug.Log("[BuildGameScene] Saved -> Assets/Scenes/PlatformerScene.unity");
        }
        catch (Exception e) { Debug.LogError($"[BuildGameScene] Save: {e.Message}"); }
    }

    // ── Layer helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the layer index for <paramref name="layerName"/>, registering it
    /// in ProjectSettings/TagManager.asset if it does not yet exist.
    /// Falls back to 0 (Default) if all user-layer slots are occupied.
    /// </summary>
    private static int EnsureLayer(string layerName)
    {
        int idx = LayerMask.NameToLayer(layerName);
        if (idx != -1) return idx;

        var tagManager = new SerializedObject(
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/TagManager.asset"));
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(slot.stringValue)) continue;
            slot.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            Debug.Log($"[BuildGameScene] Layer '{layerName}' registered at index {i}.");
            return i;
        }

        Debug.LogError($"[BuildGameScene] No free user-layer slot found for '{layerName}'; " +
                       "falling back to Default layer (0). Assign a free layer manually.");
        return 0;
    }

    // ── Physics helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Shared friction=0 PhysicsMaterial2D for the player and the ground tilemap.
    /// Removes tangential drag at internal tile-collider seams so a flat run
    /// across multiple tiles never catches on the boundary between them.
    /// </summary>
    private static PhysicsMaterial2D GetZeroFrictionMaterial()
    {
        const string PATH = "Assets/Tiles/ZeroFriction.physicsMaterial2D";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(PATH);
        if (mat != null) return mat;

        Directory.CreateDirectory("Assets/Tiles");
        mat = new PhysicsMaterial2D("ZeroFriction") { friction = 0f, bounciness = 0f };
        AssetDatabase.CreateAsset(mat, PATH);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // ── Tile helpers ──────────────────────────────────────────────────────────

    private static void PlacePlatformTiles(Tilemap tm, Tile tile, int xStart, int xEnd, int y)
    {
        for (int x = xStart; x <= xEnd; x++)
            tm.SetTile(new Vector3Int(x, y, 0), tile);
    }

    /// <summary>
    /// Zigzag staircase climbing straight up from the "right (high)" platform
    /// (x 8..12, cell y=-1 → world surface y=0), alternating left/right so each
    /// step is a real jump rather than a flat vertical corridor. Vertical
    /// spacing (3 cells) is comfortably within the player's ~3.3-unit jump
    /// height; horizontal spacing (4 cells) is trivial at moveSpeed=13 during
    /// a jump arc, keeping the challenge purely about timing/climbing rather
    /// than also being a horizontal-precision test.
    /// Returns the cell Y of the topmost platform placed.
    /// </summary>
    private static int PlaceVerticalTower(Tilemap tm, Tile tile)
    {
        const int width      = 3;
        const int verticalStep = 3;
        int[] xOffsets = { 0, 4, -4, 4, -4, 4, -4, 4, -4, 4 };

        int x = 9;
        int y = 2; // first rung sits verticalStep cells above the y=-1 platform below

        for (int i = 0; i < xOffsets.Length; i++)
        {
            x += xOffsets[i];
            for (int px = x; px < x + width; px++)
                tm.SetTile(new Vector3Int(px, y, 0), tile);

            if (i < xOffsets.Length - 1)
                y += verticalStep;
        }

        return y;
    }

    private static Tile CreateFallbackTile()
    {
        // Sprites created with Sprite.Create() at runtime are NOT serializable assets —
        // the tile will save with a null sprite and render nothing.
        // Solution: write a PNG to disk, import it through Unity's importer pipeline,
        // then reference the resulting asset-backed Sprite in the Tile.
        const string PNG_PATH  = "Assets/Tiles/GroundTile.png";
        const string TILE_PATH = "Assets/Tiles/GroundTile.asset";
        try
        {
            Directory.CreateDirectory("Assets/Tiles");

            // Always write a fresh PNG so SaveAndReimport reliably applies
            // our importer settings — reusing an existing file can leave a
            // stale .meta with the wrong PPU baked in.
            {
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                var pix = new Color[256];
                for (int i = 0; i < pix.Length; i++) pix[i] = Color.white;
                tex.SetPixels(pix);
                tex.Apply();
                File.WriteAllBytes(PNG_PATH, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(PNG_PATH, ImportAssetOptions.ForceSynchronousImport);
            }

            // Always enforce sprite settings and PPU so the tile fills exactly one grid cell.
            // Default PPU in Unity is 100 → a 16×16 px sprite becomes 0.16×0.16 world units,
            // leaving huge visible gaps AND making all collision shapes tiny.
            // At 16 PPU: 16px / 16 = 1.0 world unit = exactly one 1×1 grid cell.
            var importer = AssetImporter.GetAtPath(PNG_PATH) as TextureImporter;
            if (importer != null)
            {
                importer.textureType         = TextureImporterType.Sprite;
                importer.spriteImportMode    = SpriteImportMode.Single;
                importer.filterMode          = FilterMode.Point;
                importer.spritePixelsPerUnit = 16f;
                importer.SaveAndReimport();
            }

            AssetDatabase.Refresh();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PNG_PATH);
            if (sprite == null)
            {
                Debug.LogError("[BuildGameScene] Failed to load sprite from " + PNG_PATH);
                return null;
            }

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite       = sprite;
            // Grid collider type: collision shape = full 1×1 grid cell,
            // independent of sprite size — prevents falling through gaps.
            tile.colliderType = Tile.ColliderType.Grid;
            AssetDatabase.CreateAsset(tile, TILE_PATH);
            AssetDatabase.SaveAssets();
            return tile;
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildGameScene] CreateFallbackTile: {e.Message}");
            return null;
        }
    }

    // ── Sprite factories ──────────────────────────────────────────────────────

    private static Sprite CreateSquareSprite(Color color)
    {
        var tex    = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        var pixels = new Color[256];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
    }

    private static Sprite CreateCapsuleSprite(Color color)
    {
        const int w = 16, h = 24;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        float cx = w * 0.5f, cy = h * 0.5f, rx = w * 0.5f, ry = h * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                tex.SetPixel(x, y, dx * dx + dy * dy <= 1f ? color : Color.clear);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    // ── Object spawners ───────────────────────────────────────────────────────

    /// <summary>
    /// Parallax layer with a repeating triangle silhouette pattern
    /// (stalagmites from the bottom, stalactites from the top) baked directly
    /// into its sprite, on a transparent background so whatever layer is
    /// behind it shows through in the gaps between triangles. Uses plain
    /// transform-drift parallax (ParallaxLayer.useTilingScroll = false) rather
    /// than shader UV-offset scrolling: SpriteRenderer batching can silently
    /// ignore per-material texture offset, so a scrolled-in-place tiling
    /// texture isn't reliable. worldWidth/worldHeight set the sprite's actual
    /// size directly (via a matched texture-resolution/PPU pair, so no
    /// distortion) — size these generously relative to the layer's own
    /// scrollSpeed: the slower it scrolls, the more it drifts relative to the
    /// camera over a normal play session, so it needs more coverage.
    /// </summary>
    private static void CreateTriangleParallaxLayer(string name, Vector3 pos, Vector2 speed,
                                                      float worldWidth, float worldHeight,
                                                      int triangleCount, float triangleWorldHeight,
                                                      bool tipUp, Color triangleColor)
    {
        var go = ObjectFactory.CreateGameObject(name, typeof(SpriteRenderer), typeof(ParallaxLayer));
        go.transform.position   = pos;
        go.transform.localScale = Vector3.one; // sizing comes entirely from the sprite's own PPU

        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = CreateCaveTriangleSprite(worldWidth, worldHeight, triangleCount,
                                              triangleWorldHeight, tipUp, triangleColor);
        sr.sortingOrder = -11;

        var pl = go.GetComponent<ParallaxLayer>();
        if (pl != null)
        {
            pl.scrollSpeed     = speed;
            pl.useTilingScroll = false;
        }
    }

    /// <summary>
    /// The single "cave hole" background layer — one big, soft, dark blob.
    /// Not tiling (there's only one instance of it), so it uses classic drift
    /// parallax instead; sized generously so it doesn't show an edge during
    /// normal play.
    /// </summary>
    private static void CreateHoleParallaxLayer(string name, Vector3 pos, Vector2 speed,
                                                 float diameter, Color color)
    {
        var go = ObjectFactory.CreateGameObject(name, typeof(SpriteRenderer), typeof(ParallaxLayer));
        go.transform.position   = pos;
        go.transform.localScale = new Vector3(diameter, diameter, 1f);

        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = CreateDarkHoleSprite(color);
        sr.sortingOrder = -12;

        var pl = go.GetComponent<ParallaxLayer>();
        if (pl != null)
        {
            pl.scrollSpeed     = speed;
            pl.useTilingScroll = false; // one shape — drifts instead of tiling
        }
    }

    /// <summary>
    /// Texture with a single band of triangle silhouettes, on a transparent
    /// background, repeated triangleCount times across its width — either
    /// stalagmites rising from the bottom edge (tipUp) or stalactites hanging
    /// from the top edge (!tipUp). Kept to one band per texture/GameObject (top
    /// and bottom triangles used to share one texture, mirrored off the same
    /// random draws) so each band gets its own independent procedural layout,
    /// and so the two bands can be positioned completely independently — that's
    /// what let the bottom band be lifted into view without touching the
    /// already-tuned top band at all. Texture resolution is derived from
    /// worldWidth/worldHeight at a fixed pixel density, so the texture's pixel
    /// aspect ratio always matches the requested world aspect ratio exactly —
    /// that's what lets the sprite use pixelsPerUnit for correct sizing with the
    /// layer's localScale left at (1,1,1), with zero distortion risk (see the
    /// health-bar sprite fix for what goes wrong when a texture's aspect ratio
    /// doesn't match how it's scaled).
    /// triangleWorldHeight is an ABSOLUTE world-unit size, not a fraction of
    /// worldHeight — deliberately decoupled so that making the overall sprite
    /// taller doesn't also balloon the triangles themselves to some enormous
    /// size, and so top/bottom bands can share the same triangleWorldHeight
    /// value even though they live in differently-sized textures.
    /// </summary>
    private static Sprite CreateCaveTriangleSprite(float worldWidth, float worldHeight,
                                                    int triangleCount, float triangleWorldHeight,
                                                    bool tipUp, Color color)
    {
        const float pixelsPerWorldUnit = 10f;
        int texWidth  = Mathf.Max(4, Mathf.RoundToInt(worldWidth  * pixelsPerWorldUnit));
        int texHeight = Mathf.Max(4, Mathf.RoundToInt(worldHeight * pixelsPerWorldUnit));

        var tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        var pixels = new Color[texWidth * texHeight];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        int avgWidth   = Mathf.Max(2, texWidth / Mathf.Max(1, triangleCount));
        // Only one band lives in this texture now, so it can use the texture's
        // full height instead of being capped to half of it — no second band
        // to collide with.
        int baseHeight = Mathf.Clamp(Mathf.RoundToInt(triangleWorldHeight * pixelsPerWorldUnit), 1, texHeight - 1);

        // Not tiling (this sprite doesn't wrap/repeat — it's baked once at its
        // full size), so there's no seam to keep seamless: each triangle can
        // freely get its own randomised width, height and gap for a more
        // natural, less uniformly-spaced silhouette instead of a perfect
        // repeating comb. Sequential placement (each triangle starts after the
        // previous one's width + gap) guarantees triangles within a band never
        // overlap each other horizontally.
        int x = 0;
        for (int t = 0; t < triangleCount && x < texWidth; t++)
        {
            int thisWidth  = Mathf.Max(2, Mathf.RoundToInt(avgWidth * UnityEngine.Random.Range(0.6f, 1.4f)));
            int thisHeight = Mathf.Clamp(Mathf.RoundToInt(baseHeight * UnityEngine.Random.Range(0.7f, 1.3f)),
                                          1, texHeight - 1);
            int gap        = Mathf.RoundToInt(avgWidth * UnityEngine.Random.Range(0f, 0.5f));

            int xCenter = x + thisWidth / 2;

            for (int y = 0; y < thisHeight; y++)
            {
                float taper     = 1f - (float)y / thisHeight; // 1 at base, 0 at tip
                int   halfWidth = Mathf.RoundToInt(thisWidth * 0.5f * taper);
                int   py        = tipUp ? y : texHeight - 1 - y; // bottom: base at y=0, tip up | top: base at y=texHeight-1, tip down

                for (int dx = -halfWidth; dx <= halfWidth; dx++)
                {
                    int px = xCenter + dx;
                    SetPixelClamped(pixels, texWidth, texHeight, px, py, color);
                }
            }

            x += thisWidth + gap;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, texWidth, texHeight), new Vector2(0.5f, 0.5f), pixelsPerWorldUnit);
    }

    private static void SetPixelClamped(Color[] pixels, int texWidth, int texHeight, int x, int y, Color color)
    {
        if (x < 0 || x >= texWidth || y < 0 || y >= texHeight) return;
        pixels[y * texWidth + x] = color;
    }

    /// <summary>Soft-edged, dark, roughly circular blob — the distant "cave hole".</summary>
    private static Sprite CreateDarkHoleSprite(Color color)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float r = size * 0.5f;

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                float dist  = Mathf.Sqrt(dx * dx + dy * dy);
                // Soft falloff instead of a hard edge, so it reads as a hazy
                // cave opening rather than a sharp cut-out disc.
                float alpha = Mathf.Clamp01(1f - dist);
                pixels[y * size + x] = new Color(color.r, color.g, color.b, color.a * alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static void SpawnElementPickup(string objName, Vector3 position, ElementStats element)
    {
        var go = ObjectFactory.CreateGameObject(objName,
            typeof(SpriteRenderer), typeof(CircleCollider2D));
        go.transform.position   = position;
        go.transform.localScale = Vector3.one * 0.5f;
        go.GetComponent<CircleCollider2D>().isTrigger = true;
        var ep = go.AddComponent<ElementPickup>();
        if (ep != null) { ep.elementToGive = element; ep.respawnTime = 5f; }
    }

    private static void SpawnSpikeHazard(string objName, Vector3 position)
    {
        var go = ObjectFactory.CreateGameObject(objName,
            typeof(SpriteRenderer), typeof(BoxCollider2D));
        go.transform.position   = position;
        go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
        go.GetComponent<BoxCollider2D>().isTrigger = true;
        go.GetComponent<SpriteRenderer>().sprite   = CreateSquareSprite(new Color(0.7f, 0.7f, 0.8f));
        go.AddComponent<SpikeHazard>();
    }

    // ── UI Canvas ─────────────────────────────────────────────────────────────

    private static void BuildUICanvas()
    {
        var canvasGO = ObjectFactory.CreateGameObject("Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        // ScreenSpaceOverlay ignores this Transform for actual rendering (Play Mode
        // is unaffected either way), but the Editor's Scene view still draws the
        // Canvas's large reference-resolution rect at its Transform position. Left
        // at the default (0,0,0) that rect sits directly on top of the level's
        // world-space geometry (floor spans roughly x=-15..20, y=-5..5), making the
        // menu look like it's "in the level" while scrubbing through the Scene view.
        // Parking it far away keeps the level clean to look at while editing.
        canvasGO.transform.position = new Vector3(0f, 500f, 0f);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<UIManager>();

        if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
#if ENABLE_INPUT_SYSTEM
            ObjectFactory.CreateGameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            ObjectFactory.CreateGameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
#endif
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // MenuPanel
        // Fully opaque and a neutral grey (not the game's dark-blue background
        // tone) so the menu reads as a distinct screen instead of a faint tint
        // over the level — at 95% navy-on-navy it was nearly indistinguishable
        // from the world behind it.
        var menuPanel = ObjectFactory.CreateGameObject("MenuPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        menuPanel.transform.SetParent(canvasGO.transform, false);
        StretchToFill(menuPanel.GetComponent<RectTransform>());
        menuPanel.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.09f, 1f);

        AddText(menuPanel, "TitleText", "2D Platformer", font, 40, FontStyle.Bold,
                Color.white, new Vector2(0f, 170f), new Vector2(500f, 80f));

        AddButton(canvasGO, menuPanel, "StartButton",   "Play",    font, new Vector2(0f,  70f));
        AddButton(canvasGO, menuPanel, "OptionsButton", "Options", font, new Vector2(0f,   0f));
        AddButton(canvasGO, menuPanel, "QuitButton",    "Quit",    font, new Vector2(0f, -70f));

        // OptionsPanel
        var optPanel = ObjectFactory.CreateGameObject("OptionsPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        optPanel.transform.SetParent(canvasGO.transform, false);
        var ort = optPanel.GetComponent<RectTransform>();
        ort.anchorMin = ort.anchorMax = ort.pivot = new Vector2(0.5f, 0.5f);
        ort.sizeDelta        = new Vector2(340f, 240f);
        ort.anchoredPosition = Vector2.zero;
        optPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 1f);

        var sliderGO = ObjectFactory.CreateGameObject("VolumeSlider",
            typeof(RectTransform), typeof(Slider));
        sliderGO.transform.SetParent(optPanel.transform, false);
        var srt = sliderGO.GetComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(260f, 30f);
        var sv = sliderGO.GetComponent<Slider>();
        if (sv != null) { sv.minValue = 0f; sv.maxValue = 1f; sv.value = 0.8f; }
        optPanel.SetActive(false);

        // GameOverPanel — hidden by default; shown by PlayerController via
        // UIManager.ShowGameOver() when health reaches 0. Dark red tint to
        // read as clearly distinct from the neutral-grey MenuPanel.
        var gameOverPanel = ObjectFactory.CreateGameObject("GameOverPanel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        gameOverPanel.transform.SetParent(canvasGO.transform, false);
        StretchToFill(gameOverPanel.GetComponent<RectTransform>());
        gameOverPanel.GetComponent<Image>().color = new Color(0.12f, 0.02f, 0.02f, 1f);

        AddText(gameOverPanel, "GameOverTitleText", "GAME OVER", font, 44, FontStyle.Bold,
                Color.white, new Vector2(0f, 60f), new Vector2(500f, 80f));

        AddButton(canvasGO, gameOverPanel, "RespawnButton", "Respawn", font, new Vector2(0f, -40f));

        gameOverPanel.SetActive(false);

        // HUD
        AddTextGO(canvasGO, "ScoreText",  "Score: 0", font, 18, Color.white,
                  new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(200f, 40f),
                  TextAnchor.UpperRight);

        // HealthDisplay – 5 HP dots, top-left. Filled/updated by
        // PlayerController.NotifyUIHealth() via UIManager.UpdateHealthDisplay.
        AddTextGO(canvasGO, "HealthDisplay", "●●●●●", font, 20, new Color(0.9f, 0.2f, 0.2f),
                  new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(200f, 40f),
                  TextAnchor.UpperLeft);

        // ElementDisplay: Image on the panel, Text on a child.
        // Unity forbids two Graphic components (Image and Text both inherit Graphic)
        // on the same GameObject — so the label must live on a child object.
        var elemGO = ObjectFactory.CreateGameObject("ElementDisplay",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        elemGO.transform.SetParent(canvasGO.transform, false);
        var ert = elemGO.GetComponent<RectTransform>();
        ert.anchorMin = ert.anchorMax = ert.pivot = Vector2.zero;
        ert.anchoredPosition = new Vector2(12f, 12f);
        ert.sizeDelta        = new Vector2(120f, 36f);
        elemGO.GetComponent<Image>().color = new Color(0.9f, 0.9f, 1f, 0.7f);

        var elemTextGO = ObjectFactory.CreateGameObject("ElementDisplayText",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        elemTextGO.transform.SetParent(elemGO.transform, false);
        var ett = elemTextGO.GetComponent<RectTransform>();
        ett.anchorMin = Vector2.zero; ett.anchorMax = Vector2.one;
        ett.sizeDelta = ett.anchoredPosition = Vector2.zero;
        var et = elemTextGO.GetComponent<Text>();
        if (et != null)
        {
            et.text = "None"; et.font = font; et.fontSize = 16;
            et.color = new Color(0.1f, 0.1f, 0.1f); et.alignment = TextAnchor.MiddleCenter;
        }

        AddTextGO(canvasGO, "DashChargesDisplay", "●", font, 18, Color.white,
                  Vector2.zero, new Vector2(12f, 52f), new Vector2(100f, 28f),
                  TextAnchor.MiddleLeft);

        // WindIndicator – shown at top-centre during dragon wind phases.
        // Hidden by default; WindZoneController activates it via UIManager.ShowWindIndicator.
        AddTextGO(canvasGO, "WindIndicator", ">> WIND >>", font, 22,
                  new Color(0.7f, 0.9f, 1f),
                  new Vector2(0.5f, 1f), new Vector2(0f, -50f),
                  new Vector2(260f, 40f), TextAnchor.UpperCenter);
    }

    // ── UI builder helpers ────────────────────────────────────────────────────

    private static void StretchToFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void AddButton(GameObject canvas, GameObject parent,
                                   string btnName, string label, Font font,
                                   Vector2 anchoredPos)
    {
        var btnGO = ObjectFactory.CreateGameObject(btnName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent.transform, false);
        var rt = btnGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(240f, 56f);
        rt.anchoredPosition = anchoredPos;

        var img = btnGO.GetComponent<Image>();
        img.color = new Color(0.18f, 0.18f, 0.32f, 1f);
        var btn = btnGO.GetComponent<Button>();
        if (btn != null)
        {
            var cb              = btn.colors;
            cb.normalColor      = new Color(0.18f, 0.18f, 0.32f, 1f);
            cb.highlightedColor = new Color(0.28f, 0.28f, 0.52f, 1f);
            cb.pressedColor     = new Color(0.38f, 0.38f, 0.62f, 1f);
            btn.colors          = cb;
        }

        var labelGO = ObjectFactory.CreateGameObject("Text",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelGO.transform.SetParent(btnGO.transform, false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.sizeDelta = lrt.anchoredPosition = Vector2.zero;
        var t = labelGO.GetComponent<Text>();
        if (t != null)
        {
            t.text      = label;
            t.font      = font;
            t.fontSize  = 22;
            t.color     = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
        }
    }

    private static void AddText(GameObject parent, string goName, string text,
                                 Font font, int size, FontStyle style,
                                 Color color, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = ObjectFactory.CreateGameObject(goName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = sizeDelta;
        rt.anchoredPosition = anchoredPos;
        var t = go.GetComponent<Text>();
        if (t == null) return;
        t.text = text; t.font = font; t.fontSize = size;
        t.fontStyle = style; t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
    }

    private static void AddTextGO(GameObject canvas, string goName, string text,
                                   Font font, int size, Color color,
                                   Vector2 anchor, Vector2 anchoredPos,
                                   Vector2 sizeDelta, TextAnchor alignment)
    {
        var go = ObjectFactory.CreateGameObject(goName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(canvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = sizeDelta;
        var t = go.GetComponent<Text>();
        if (t == null) return;
        t.text = text; t.font = font; t.fontSize = size;
        t.color = color; t.alignment = alignment;
    }
}
