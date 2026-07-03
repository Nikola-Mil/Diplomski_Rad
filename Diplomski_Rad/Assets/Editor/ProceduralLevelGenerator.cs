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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class ProceduralLevelGenerator
{
    // ── Zone constants ────────────────────────────────────────────────────────
    private const int PROC_RIGHT_START_X = 21;    // first right-zone tile column
    private const int PROC_RIGHT_END_X   = 81;    // exclusive upper bound (60 cols wide)
    private const int PROC_LEFT_START_X  = -75;   // first left-zone tile column
    private const int PROC_LEFT_END_X    = -15;   // exclusive upper bound (60 cols wide, flush with SCENE_LEFT)
    private const int FLOOR_Y      = -4;    // same floor row as regular scene
    // Matches BuildGameScene.FLOOR_THICKNESS — deep enough that the camera's
    // full 9.5-unit orthographic half-height never reveals empty space below it.
    private const int FLOOR_THICKNESS = 20;
    // Platform heights (cell y). Surface = cell.y + 1 in world coordinates.
    private static readonly int[] PLATFORM_HEIGHTS = { -3, -2, -1 };
    // Min/max number of tiles in a platform segment.
    private const int MIN_PLAT_W = 3;
    private const int MAX_PLAT_W = 6;
    // Horizontal gap between platform end and next platform start.
    private const int MIN_GAP    = 2;
    private const int MAX_GAP    = 4;
    // How far above the surface tile center objects are spawned.
    // Tile top = cell.y + 1.  Object center for 1-unit height = cell.y + 1 + 0.5 = cell.y + 1.5
    private const float ABOVE    = 1.5f;

    [MenuItem("Tools/Generate Procedural Level")]
    public static void GenerateLevel()
    {
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

        GenerateZone(tilemap, tile, allTiles, PROC_LEFT_START_X,  PROC_LEFT_END_X);
        GenerateZone(tilemap, tile, allTiles, PROC_RIGHT_START_X, PROC_RIGHT_END_X);

        tilemap.RefreshAllTiles();
        var comp = tilemap.GetComponent<CompositeCollider2D>();
        if (comp != null) comp.GenerateGeometry();
        EditorUtility.SetDirty(tilemap);

        // Spawn gameplay objects on top of the new surfaces.
        var surface = FindSurfaceTiles(allTiles);
        PlaceObjectsOnSurface(surface);

        Debug.Log($"[ProcGen] Done. {allTiles.Count} tiles in x={PROC_LEFT_START_X}..{PROC_LEFT_END_X - 1} " +
                  $"and x={PROC_RIGHT_START_X}..{PROC_RIGHT_END_X - 1}.");
    }

    // ── Zone generation ──────────────────────────────────────────────────────

    private static void GenerateZone(Tilemap tm, Tile tile, HashSet<Vector3Int> allTiles,
                                      int zoneStart, int zoneEndExclusive)
    {
        // Floor: FLOOR_THICKNESS tiles thick across the full zone.
        for (int x = zoneStart; x < zoneEndExclusive; x++)
            for (int dy = 0; dy < FLOOR_THICKNESS; dy++)
                Place(tm, tile, allTiles, x, FLOOR_Y - dy);

        // Floating platforms scattered above the floor.
        GeneratePlatforms(tm, tile, allTiles, zoneStart, zoneEndExclusive);
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

    private static void GeneratePlatforms(Tilemap tm, Tile tile, HashSet<Vector3Int> allTiles,
                                           int zoneStart, int zoneEndExclusive)
    {
        // Walk left-to-right through the zone, placing platforms with random
        // height/width/gap. Alternate low/high heights to keep the layout varied.
        int x = zoneStart + 3;   // short gap after zone start before first platform
        int prevHeight = PLATFORM_HEIGHTS[1];  // start at mid height

        while (x < zoneEndExclusive - MAX_PLAT_W - 2)
        {
            // Pick a height different from the previous one for variety.
            int heightIdx;
            do { heightIdx = UnityEngine.Random.Range(0, PLATFORM_HEIGHTS.Length); }
            while (PLATFORM_HEIGHTS[heightIdx] == prevHeight);
            int platY = PLATFORM_HEIGHTS[heightIdx];
            prevHeight = platY;

            int width = UnityEngine.Random.Range(MIN_PLAT_W, MAX_PLAT_W + 1);
            int end   = Mathf.Min(x + width - 1, zoneEndExclusive - 2);

            for (int px = x; px <= end; px++)
                Place(tm, tile, allTiles, px, platY);

            int gap = UnityEngine.Random.Range(MIN_GAP, MAX_GAP + 1);
            x = end + 1 + gap;
        }
    }

    // ── Object placement ──────────────────────────────────────────────────────

    private static void PlaceObjectsOnSurface(List<Vector3Int> surface)
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

            float roll = UnityEngine.Random.value;

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
                    var go = new GameObject($"ProcSpike_{cell.x}_{cell.y}");
                    go.transform.position   = new Vector3(cell.x + 0.5f, cell.y + 1.2f, 0f);
                    go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.color = new Color(0.7f, 0.7f, 0.8f);
                    var bc = go.AddComponent<BoxCollider2D>();
                    bc.isTrigger = true;
                    go.AddComponent<SpikeHazard>();
                    spikes++;
                }
                catch (Exception e) { Debug.LogError($"[ProcGen] Spike: {e.Message}"); }
            }

            // Enemy: place one at midpoint of flat runs >= 4 tiles (40 % chance).
            bool runEnded = i == surface.Count - 1
                            || surface[i + 1].y != cell.y
                            || surface[i + 1].x != cell.x + 1;

            if (runEnded && runLength >= 4 && UnityEngine.Random.value < 0.4f)
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
                        eGO.AddComponent<SpriteRenderer>();
                        var erb = eGO.AddComponent<Rigidbody2D>();
                        erb.bodyType = RigidbodyType2D.Kinematic;
                        var ebc = eGO.AddComponent<BoxCollider2D>();
                        ebc.isTrigger = true;
                        // No sprite is assigned here, so BoxCollider2D's auto-size
                        // (from SpriteRenderer bounds) locks in a degenerate
                        // near-zero size. Set explicitly to match the 1-unit box
                        // the position math above already assumes.
                        ebc.size = new Vector2(1f, 1f);
                        var ep = eGO.AddComponent<EnemyPatrol>();
                        if (ep != null)
                        {
                            ep.leftBound     = surface[runStart].x;
                            ep.rightBound    = surface[runStart + runLength - 1].x + 1f;
                            ep.shootInterval = UnityEngine.Random.Range(2f, 4f);
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

    // Returns tiles where the cell directly above is empty (walkable surface).
    // Sub-floor tiles (y < FLOOR_Y) are excluded since they're never walked on.
    private static List<Vector3Int> FindSurfaceTiles(HashSet<Vector3Int> allTiles)
    {
        var result = new List<Vector3Int>();
        foreach (var cell in allTiles)
        {
            if (cell.y < FLOOR_Y) continue;  // exclude sub-floor row
            if (!allTiles.Contains(new Vector3Int(cell.x, cell.y + 1, 0)))
                result.Add(cell);
        }
        result.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return result;
    }
}
