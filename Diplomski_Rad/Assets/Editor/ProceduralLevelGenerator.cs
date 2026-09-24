// ProceduralLevelGenerator.cs
// Generates a classic floating-platform level on BOTH sides of the hand-crafted
// scene — to the right (x >= PROC_RIGHT_START_X) and to the left
// (x < PROC_LEFT_END_X) — so the player can walk seamlessly from the regular
// zone into either procedural zone without a scene load.
//
// Layout contract:
//   • Regular scene floor: cell y = -4, x = -15..20 (surface at world y = -3).
//   • Right proc-gen zone: cell x = 21..80, same tilemap (GroundTilemap).
//   • Left proc-gen zone:  cell x = -75..-16, same tilemap.
//   • Proc-gen floor: same cell y = -4 so all three floors are flush.
//   • Platforms: cell y = -3 (surface world -2), -2 (surface -1), -1 (surface 0).
//     All are reachable with a standard platformer jump.
//   • Only the proc-gen zone tiles are cleared on each run; the regular scene
//     tiles (PROC_LEFT_END_X <= x < PROC_RIGHT_START_X) are never touched.
//
// Reproducibility: every random draw (layout AND object placement) comes from
// one System.Random seeded per run, so a given seed always rebuilds exactly the
// same level. The seed is logged on every run, including the unseeded menu
// command, so any level that was ever generated can be regenerated.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class ProceduralLevelGenerator
{
    // ── Zone constants ────────────────────────────────────────────────────────
    // internal (not private) so the EditMode tests in Assets/Editor/Tests read
    // the real values instead of keeping their own copies.
    internal const int PROC_RIGHT_START_X = 21;    // first right-zone tile column
    internal const int PROC_RIGHT_END_X   = 81;    // exclusive upper bound (60 cols wide)
    internal const int PROC_LEFT_START_X  = -75;   // first left-zone tile column
    internal const int PROC_LEFT_END_X    = -15;   // exclusive upper bound (60 cols wide, flush with SCENE_LEFT)
    internal const int FLOOR_Y      = -4;    // same floor row as regular scene
    // Matches BuildGameScene.FLOOR_THICKNESS — deep enough that the camera's
    // full 9.5-unit orthographic half-height never reveals empty space below it.
    internal const int FLOOR_THICKNESS = 20;
    // Platform heights (cell y). Surface = cell.y + 1 in world coordinates.
    internal static readonly int[] PLATFORM_HEIGHTS = { -3, -2, -1 };
    // Min/max number of tiles in a platform segment.
    internal const int MIN_PLAT_W = 3;
    internal const int MAX_PLAT_W = 6;
    // Horizontal gap between platform end and next platform start.
    internal const int MIN_GAP    = 2;
    internal const int MAX_GAP    = 4;
    // Empty cells needed above a surface tile before objects may spawn on it.
    // The player is 1.1 units tall (1x1 box + 0.05 edge radius), so a 1-cell
    // pocket under a cell-y -2 platform is somewhere it can never enter —
    // anything spawned there would be unreachable.
    internal const int MIN_OBJECT_CLEARANCE = 2;
    // How far above the surface tile center objects are spawned.
    // Tile top = cell.y + 1.  Object center for 1-unit height = cell.y + 1 + 0.5 = cell.y + 1.5
    private const float ABOVE    = 1.5f;

    /// <summary>One floating platform in cell coordinates (xStart..xEnd inclusive).</summary>
    internal struct PlatformSpan
    {
        public int xStart, xEnd, y;
    }

    [MenuItem("Tools/Generate Procedural Level")]
    public static void GenerateLevel()
    {
        // No seed chosen: draw one, but still log it so this exact level can
        // be rebuilt later via Tools/Generate Procedural Level (Seeded)...
        GenerateLevel(new System.Random().Next(1, int.MaxValue));
    }

    public static void GenerateLevel(int seed)
    {
        var rng = new System.Random(seed);
        Debug.Log($"[ProcGen] Seed = {seed}");

        Tilemap tilemap = FindGroundTilemap();
        if (tilemap == null)
        {
            Debug.LogError("[ProcGen] 'GroundTilemap' not found. " +
                           "Run 'Tools/Build Platformer Scene' first.");
            return;
        }

        Tile tile = LoadOrCreateTile();
        if (tile == null)
        {
            Debug.LogError("[ProcGen] Could not obtain a Tile asset. Aborted.");
            return;
        }

        Undo.RecordObject(tilemap, "Procedural Level Generation");

        // Clear only the proc-gen zones (never touch the hand-built scene between them).
        ClearProcGenZone(tilemap);

        // Collect all new tile positions (for surface detection later).
        var allTiles = new HashSet<Vector3Int>();

        GenerateZone(tilemap, tile, allTiles, rng, PROC_LEFT_START_X,  PROC_LEFT_END_X);
        GenerateZone(tilemap, tile, allTiles, rng, PROC_RIGHT_START_X, PROC_RIGHT_END_X);

        tilemap.RefreshAllTiles();
        var comp = tilemap.GetComponent<CompositeCollider2D>();
        if (comp != null) comp.GenerateGeometry();
        EditorUtility.SetDirty(tilemap);

        // Spawn gameplay objects on top of the new surfaces.
        var surface = FindSurfaceTiles(allTiles);
        PlaceObjectsOnSurface(surface, rng);

        Debug.Log($"[ProcGen] Done. {allTiles.Count} tiles in x={PROC_LEFT_START_X}..{PROC_LEFT_END_X - 1} " +
                  $"and x={PROC_RIGHT_START_X}..{PROC_RIGHT_END_X - 1}.");
    }

    // ── Zone generation ──────────────────────────────────────────────────────

    private static void GenerateZone(Tilemap tm, Tile tile, HashSet<Vector3Int> allTiles,
                                      System.Random rng, int zoneStart, int zoneEndExclusive)
    {
        // Floor: FLOOR_THICKNESS tiles thick across the full zone.
        for (int x = zoneStart; x < zoneEndExclusive; x++)
            for (int dy = 0; dy < FLOOR_THICKNESS; dy++)
                Place(tm, tile, allTiles, x, FLOOR_Y - dy);

        // Floating platforms scattered above the floor.
        foreach (var p in LayoutPlatforms(rng, zoneStart, zoneEndExclusive))
            for (int px = p.xStart; px <= p.xEnd; px++)
                Place(tm, tile, allTiles, px, p.y);
    }

    // ── Clear zone ────────────────────────────────────────────────────────────

    private static void ClearProcGenZone(Tilemap tm)
    {
        int floorBottom = FLOOR_Y - FLOOR_THICKNESS + 1;

        // Right zone + a generous buffer beyond it to catch leftover tiles
        // from a previous, wider run.
        for (int x = PROC_RIGHT_START_X; x < PROC_RIGHT_END_X + 5; x++)
            for (int y = floorBottom; y <= 3; y++)
                tm.SetTile(new Vector3Int(x, y, 0), null);

        // Left zone + a buffer beyond its far edge.
        for (int x = PROC_LEFT_START_X - 5; x < PROC_LEFT_END_X; x++)
            for (int y = floorBottom; y <= 3; y++)
                tm.SetTile(new Vector3Int(x, y, 0), null);
    }

    // ── Platform generation ───────────────────────────────────────────────────

    // Pure layout step: no tilemap access, all randomness from rng.
    internal static List<PlatformSpan> LayoutPlatforms(System.Random rng, int zoneStart, int zoneEndExclusive)
    {
        // Walk left-to-right through the zone, placing platforms with random
        // height/width/gap. Alternate low/high heights to keep the layout varied.
        var platforms  = new List<PlatformSpan>();
        int x          = zoneStart + 3;   // short gap after zone start before first platform
        int prevHeight = PLATFORM_HEIGHTS[1];  // start at mid height

        while (x < zoneEndExclusive - MAX_PLAT_W - 2)
        {
            // Pick a height different from the previous one for variety.
            int heightIdx;
            do { heightIdx = rng.Next(0, PLATFORM_HEIGHTS.Length); }
            while (PLATFORM_HEIGHTS[heightIdx] == prevHeight);
            int platY = PLATFORM_HEIGHTS[heightIdx];
            prevHeight = platY;

            int width = rng.Next(MIN_PLAT_W, MAX_PLAT_W + 1);
            int end   = Mathf.Min(x + width - 1, zoneEndExclusive - 2);

            platforms.Add(new PlatformSpan { xStart = x, xEnd = end, y = platY });

            int gap = rng.Next(MIN_GAP, MAX_GAP + 1);
            x = end + 1 + gap;
        }
        return platforms;
    }

    // ── Object placement ──────────────────────────────────────────────────────

    private static void PlaceObjectsOnSurface(List<Vector3Int> surface, System.Random rng)
    {
        if (surface.Count == 0)
        {
            Debug.LogWarning("[ProcGen] No surface tiles found for object placement.");
            return;
        }

        // Delete proc-gen objects from previous runs.
        CleanupOldProcGenObjects();

        ElementStats[] elementCycle =
        {
            PlayerController.CreateEarthElement(),
            PlayerController.CreateFireElement(),
            PlayerController.CreateWaterElement(),
            PlayerController.CreateAirElement(),
        };
        int elementIndex   = 0;
        int pickups = 0, spikes = 0, enemies = 0;

        // Track consecutive flat runs (same Y, consecutive X) for enemy placement.
        int runLength = 1, runStart = 0;
        int prevY     = surface[0].y;

        for (int i = 0; i < surface.Count; i++)
        {
            var cell  = surface[i];
            // World position of tile top-centre: x+0.5, y+1 (tile top) + 0.5 (half object)
            var world = new Vector3(cell.x + 0.5f, cell.y + ABOVE, 0f);

            bool sameRun = i > 0
                           && surface[i].y == prevY
                           && surface[i].x == surface[i - 1].x + 1;
            runLength = sameRun ? runLength + 1 : 1;
            prevY     = cell.y;

            float roll = (float)rng.NextDouble();

            // 18 % → element pickup
            if (roll < 0.18f)
            {
                try
                {
                    var go = new GameObject($"ProcPickup_{cell.x}_{cell.y}");
                    go.transform.position   = world;
                    go.transform.localScale = Vector3.one * 0.5f;
                    go.AddComponent<SpriteRenderer>();
                    var cc = go.AddComponent<CircleCollider2D>();
                    cc.isTrigger = true;
                    var ep = go.AddComponent<ElementPickup>();
                    if (ep != null)
                    {
                        ep.elementToGive = elementCycle[elementIndex % elementCycle.Length];
                        ep.respawnTime   = 5f;
                        elementIndex++;
                        pickups++;
                    }
                }
                catch (Exception e) { Debug.LogError($"[ProcGen] Pickup: {e.Message}"); }
            }
            // 10 % → spike hazard
            else if (roll < 0.28f)
            {
                try
                {
                    // Spike sits flush with the surface: tile top = cell.y+1 (world),
                    // spike half-height with scale 0.4 = 0.2 → centre at cell.y+1+0.2
                    // Same factory as the hand-placed spikes, so both get the same
                    // visible sprite and correctly sized trigger.
                    BuildGameScene.SpawnSpikeHazard($"ProcSpike_{cell.x}_{cell.y}",
                        new Vector3(cell.x + 0.5f, cell.y + 1.2f, 0f));
                    spikes++;
                }
                catch (Exception e) { Debug.LogError($"[ProcGen] Spike: {e.Message}"); }
            }

            // Enemy: place one at midpoint of flat runs >= 4 tiles (40 % chance).
            bool runEnded = i == surface.Count - 1
                            || surface[i + 1].y != cell.y
                            || surface[i + 1].x != cell.x + 1;

            if (runEnded && runLength >= 4 && rng.NextDouble() < 0.4)
            {
                try
                {
                    int midIdx = runStart + runLength / 2;
                    if (midIdx < surface.Count)
                    {
                        int midX = surface[midIdx].x;
                        // Kinematic enemy: centre must sit exactly on the surface.
                        // Surface world y = cell.y + 1. Half-height (1-unit box) = 0.5.
                        // Centre = surface + 0.5 = cell.y + 1.5
                        var eGO  = new GameObject($"ProcEnemy_{midX}_{cell.y}");
                        eGO.transform.position = new Vector3(midX + 0.5f, cell.y + 1.5f, 0f);
                        var eSr = eGO.AddComponent<SpriteRenderer>();
                        // Matches BuildGameScene.cs's hand-placed RangedEnemy colour —
                        // these are shooters too (shootInterval/shootRange below), so
                        // the same orange capsule keeps procedurally spawned enemies
                        // visually consistent with the hand-built ones instead of
                        // being invisible (a bare AddComponent<SpriteRenderer>() with
                        // no sprite assigned renders nothing at all; only the
                        // EnemyPatrol-added health bar, which has its own sprite, was
                        // ever visible).
                        eSr.sprite = CreateEnemySprite(new Color(0.9f, 0.5f, 0.1f));
                        var erb = eGO.AddComponent<Rigidbody2D>();
                        erb.bodyType = RigidbodyType2D.Kinematic;
                        var ebc = eGO.AddComponent<BoxCollider2D>();
                        ebc.isTrigger = true;
                        // BoxCollider2D auto-sizes from the SpriteRenderer bounds at
                        // the moment it's added, which is BEFORE the sprite assignment
                        // above — it would otherwise lock in a degenerate near-zero
                        // size. Set explicitly to match the 1-unit box the position
                        // math above already assumes.
                        ebc.size = new Vector2(1f, 1f);
                        var ep = eGO.AddComponent<EnemyPatrol>();
                        if (ep != null)
                        {
                            ep.leftBound     = surface[runStart].x;
                            ep.rightBound    = surface[runStart + runLength - 1].x + 1f;
                            ep.shootInterval = 2f + 2f * (float)rng.NextDouble();
                            ep.shootRange    = 12f;
                        }
                        enemies++;
                    }
                }
                catch (Exception e) { Debug.LogError($"[ProcGen] Enemy: {e.Message}"); }

                runStart = i + 1;
            }
            else if (!sameRun)
            {
                runStart = i;
            }
        }

        Debug.Log($"[ProcGen] Objects: {pickups} pickups, {spikes} spikes, {enemies} enemies.");
    }

    /// <summary>16x16 filled-ellipse sprite at 16 PPU (1x1 world units), matching the
    /// enemy's BoxCollider2D size exactly. Same technique as BuildGameScene.cs's
    /// CreateCapsuleSprite, just square instead of tall, since this script has no
    /// access to that private helper.</summary>
    private static Sprite CreateEnemySprite(Color color)
    {
        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                tex.SetPixel(x, y, dx * dx + dy * dy <= 1f ? color : Color.clear);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static void CleanupOldProcGenObjects()
    {
        // Destroy any GameObjects from a previous proc-gen run that are still in the scene.
        string[] prefixes = { "ProcPickup_", "ProcSpike_", "ProcEnemy_" };
        var all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var go in all)
        {
            if (go == null) continue;
            foreach (var prefix in prefixes)
            {
                if (go.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    break;
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Place(Tilemap tm, Tile tile, HashSet<Vector3Int> set, int x, int y)
    {
        var pos = new Vector3Int(x, y, 0);
        tm.SetTile(pos, tile);
        set.Add(pos);
    }

    private static Tilemap FindGroundTilemap()
    {
        var all = UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        foreach (var tm in all)
            if (tm.gameObject.name == "GroundTilemap") return tm;
        return null;
    }

    private static Tile LoadOrCreateTile()
    {
        var t = AssetDatabase.LoadAssetAtPath<Tile>("Assets/Tiles/GroundTile.asset");
        if (t != null) return t;
        try
        {
            System.IO.Directory.CreateDirectory("Assets/Tiles");
            var tex    = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color[256];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels); tex.Apply();
            var tile   = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = Sprite.Create(tex, new Rect(0, 0, 16, 16),
                                        new Vector2(0.5f, 0.5f), 16f);
            AssetDatabase.CreateAsset(tile, "Assets/Tiles/GroundTile.asset");
            AssetDatabase.SaveAssets();
            return tile;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ProcGen] Fallback tile: {e.Message}");
            return null;
        }
    }

    // Returns tiles the player can actually stand on: the MIN_OBJECT_CLEARANCE
    // cells above are empty. Checking only the cell directly above let floor
    // tiles under a cell-y -2 platform count as surface, and ~12 % of pickups
    // (plus spikes) landed in that 1-cell pocket where the player can't fit.
    // Sub-floor tiles (y < FLOOR_Y) are excluded since they're never walked on.
    private static List<Vector3Int> FindSurfaceTiles(HashSet<Vector3Int> allTiles)
    {
        var result = new List<Vector3Int>();
        foreach (var cell in allTiles)
        {
            if (cell.y < FLOOR_Y) continue;  // exclude sub-floor row
            bool clear = true;
            for (int dy = 1; dy <= MIN_OBJECT_CLEARANCE && clear; dy++)
                clear = !allTiles.Contains(new Vector3Int(cell.x, cell.y + dy, 0));
            if (clear)
                result.Add(cell);
        }
        result.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return result;
    }
}

/// <summary>
/// Tools/Generate Procedural Level (Seeded)... — regenerates the level from a
/// typed-in seed, e.g. one copied from an earlier "[ProcGen] Seed = N" log line.
/// </summary>
public class ProceduralLevelSeedWindow : EditorWindow
{
    private int seed = 2026;

    [MenuItem("Tools/Generate Procedural Level (Seeded)...")]
    private static void Open()
    {
        var w = GetWindow<ProceduralLevelSeedWindow>(true, "Procedural Level Seed");
        w.minSize = w.maxSize = new Vector2(260f, 70f);
    }

    private void OnGUI()
    {
        seed = EditorGUILayout.IntField("Seed", seed);
        if (GUILayout.Button("Generate"))
            ProceduralLevelGenerator.GenerateLevel(seed);
    }
}
