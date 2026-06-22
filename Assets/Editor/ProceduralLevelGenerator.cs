// ProceduralLevelGenerator.cs
// ─────────────────────────────────────────────────────────────────────────────
// Design choices — Random Walk + Platform Constraints:
//
//   Algorithm overview:
//     1. A "walker" starts at (0, 0) and moves horizontally across the level.
//     2. Every MIN_PLATFORM_WIDTH steps the walker may step up or down by 1 tile.
//        This guarantees that each horizontal section is wide enough for the player
//        to land on before the next elevation change.
//     3. Y is clamped to [Y_MIN, Y_MAX] so platforms are always reachable.
//     4. Connectivity constraint: the maximum vertical gap between consecutive
//        platforms is MAX_JUMP_HEIGHT (2 tiles).  If the random step would exceed
//        this, the delta is clamped to 1.
//     5. A solid floor row at Y_MIN is written last to ensure the player always
//        has a fallback landing surface, preventing a "no ground" death loop.
//     6. Thin "bridge" columns are placed on the left wall of each elevation
//        change so the transition is always climbable.
//
//   Connectivity is formally ensured by:
//     • Never moving more than 1 tile vertically per elevation change.
//     • Keeping elevation changes spaced MIN_PLATFORM_WIDTH tiles apart.
//     • Standard platformer jump formula: maxJumpHeight = jumpForce² / (2 × gravity)
//       ≈ 12² / (2 × 3 × 9.81) ≈ 2.4 tiles with the default PlayerController values.
//       Using MAX_JUMP_HEIGHT = 2 gives a safe margin.
//
// ─────────────────────────────────────────────────────────────────────────────
// Failure modes:
//   • No Tilemap named "GroundTilemap" in scene: logs error and aborts.
//   • No tile asset at Assets/Tiles/GroundTile.asset: creates a white fallback tile.
//   • Tilemap.SetTile throws: caught per-tile; generation continues for other tiles.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class ProceduralLevelGenerator
{
    // ── Generator parameters ──────────────────────────────────────────────────
    private const int   LEVEL_WIDTH        = 60;   // tiles from x = 0 to LEVEL_WIDTH
    private const int   Y_MIN              = -6;
    private const int   Y_MAX              =  2;
    private const int   MIN_PLATFORM_WIDTH =  4;   // min tiles before an elevation change
    private const int   MAX_JUMP_HEIGHT    =  2;   // max tiles reachable in one jump
    private const float ELEVATION_CHANCE   = 0.35f; // probability of changing height each step

    [MenuItem("Tools/Generate Procedural Level")]
    public static void GenerateLevel()
    {
        // ── Locate the GroundTilemap in the active scene ──────────────────────
        Tilemap tilemap = FindGroundTilemap();
        if (tilemap == null)
        {
            Debug.LogError("[ProceduralLevelGenerator] 'GroundTilemap' not found in the active scene. " +
                           "Run 'Tools/Build Platformer Scene' first to create the base scene.");
            return;
        }

        // ── Load or create tile asset ─────────────────────────────────────────
        Tile tile = LoadOrCreateTile();
        if (tile == null)
        {
            Debug.LogError("[ProceduralLevelGenerator] Could not obtain a Tile asset. " +
                           "Generation aborted.");
            return;
        }

        // ── Clear existing tiles ──────────────────────────────────────────────
        Undo.RecordObject(tilemap, "Procedural Level Generation");
        tilemap.ClearAllTiles();

        // ── Run the random walk ───────────────────────────────────────────────
        var platforms = RunRandomWalk();

        // ── Paint platforms ───────────────────────────────────────────────────
        int tilesPlaced = 0;
        foreach (var cell in platforms)
        {
            try
            {
                tilemap.SetTile(cell, tile);
                tilesPlaced++;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProceduralLevelGenerator] SetTile failed at {cell}: {e.Message}");
            }
        }

        // ── Paint solid floor row ─────────────────────────────────────────────
        // Connectivity guarantee: the player always has a floor-level landing zone.
        for (int x = 0; x <= LEVEL_WIDTH; x++)
        {
            try { tilemap.SetTile(new Vector3Int(x, Y_MIN, 0), tile); tilesPlaced++; }
            catch (Exception e) { Debug.LogError($"[ProceduralLevelGenerator] Floor tile error at x={x}: {e.Message}"); }
        }

        tilemap.RefreshAllTiles();
        EditorUtility.SetDirty(tilemap);

        // ── Random object placement ────────────────────────────────────────────
        // After the tilemap is committed, derive the set of "surface" positions
        // (cells whose immediate neighbour above is empty) and scatter game objects
        // across them using the specified probabilities.
        //
        // Constraints / reasoning:
        //   • Only surface tiles are used so objects sit ON platforms, not inside them.
        //   • The floor row (Y_MIN) is excluded from element pickups to avoid burying
        //     them under the regular ground — it's less interesting design-space.
        //   • A ranged enemy requires a flat run of MIN_PLATFORM_WIDTH tiles so the
        //     player has room to dodge the shots.
        //   • All placements are try-caught individually; one failure does not abort
        //     the rest of the generation pass.

        var surfaceTiles = FindSurfaceTiles(platforms, Y_MIN);
        PlaceObjectsOnSurface(surfaceTiles, tilesPlaced);

        Debug.Log($"[ProceduralLevelGenerator] Generation complete. " +
                  $"{tilesPlaced} tiles placed across {LEVEL_WIDTH} columns.");
    }

    // ── Random walk algorithm ─────────────────────────────────────────────────

    /// <summary>
    /// Walks horizontally from x=0 to x=LEVEL_WIDTH.
    /// At each column a full platform segment (multiple tiles deep) is placed
    /// so the player cannot fall through one-tile-thick floors.
    /// Returns a HashSet of all tile positions (no duplicates).
    /// </summary>
    private static HashSet<Vector3Int> RunRandomWalk()
    {
        var cells         = new HashSet<Vector3Int>();
        int currentY      = -2;  // start at a comfortable height above the floor
        int stepsSinceChange = 0;

        for (int x = 0; x <= LEVEL_WIDTH; x++)
        {
            // ── Maybe change elevation ────────────────────────────────────────
            // Constraint: must have walked MIN_PLATFORM_WIDTH columns since last change.
            // Constraint: new Y must remain within [Y_MIN+1, Y_MAX] (leave floor row free).
            if (stepsSinceChange >= MIN_PLATFORM_WIDTH && UnityEngine.Random.value < ELEVATION_CHANCE)
            {
                // Clamped to MAX_JUMP_HEIGHT (= 1 in practice since we only step by 1)
                int delta = UnityEngine.Random.value < 0.5f ? -1 : 1;
                int newY  = Mathf.Clamp(currentY + delta, Y_MIN + 1, Y_MAX);

                // Bridge column: fill tiles between the old and new Y so the
                // elevation change is always physically traversable.
                int lo = Mathf.Min(currentY, newY);
                int hi = Mathf.Max(currentY, newY);
                for (int bridgeY = lo; bridgeY <= hi; bridgeY++)
                    cells.Add(new Vector3Int(x, bridgeY, 0));

                currentY           = newY;
                stepsSinceChange   = 0;
            }
            else
            {
                stepsSinceChange++;
            }

            // ── Place surface tile and two tiles below it (platform thickness) ─
            // Three tiles deep prevents the player seeing "through" thin platforms.
            cells.Add(new Vector3Int(x, currentY,     0));
            cells.Add(new Vector3Int(x, currentY - 1, 0));
            cells.Add(new Vector3Int(x, currentY - 2, 0));
        }

        return cells;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Tilemap FindGroundTilemap()
    {
        // Search all Tilemap components in the scene for one named "GroundTilemap".
        var tilemaps = UnityEngine.Object.FindObjectsOfType<Tilemap>();
        foreach (var tm in tilemaps)
            if (tm.gameObject.name == "GroundTilemap") return tm;
        return null;
    }

    private static Tile LoadOrCreateTile()
    {
        Tile t = AssetDatabase.LoadAssetAtPath<Tile>("Assets/Tiles/GroundTile.asset");
        if (t != null) return t;

        Debug.LogWarning("[ProceduralLevelGenerator] Assets/Tiles/GroundTile.asset missing – " +
                         "generating a white fallback tile.");
        try
        {
            System.IO.Directory.CreateDirectory("Assets/Tiles");

            var tex    = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color[256];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, 16, 16),
                                        new Vector2(0.5f, 0.5f), 16f);
            var tile   = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;

            AssetDatabase.CreateAsset(tile, "Assets/Tiles/GroundTile.asset");
            AssetDatabase.SaveAssets();
            return tile;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ProceduralLevelGenerator] Fallback tile creation failed: {e.Message}");
            return null;
        }
    }

    // ── Surface tile discovery ────────────────────────────────────────────────

    /// <summary>
    /// Returns positions of cells that are in the platform set and whose
    /// immediate above neighbour is NOT in the set.  These are walkable surfaces.
    /// The floor row (Y_MIN) is excluded to keep it free for enemy patrol paths.
    /// </summary>
    private static List<Vector3Int> FindSurfaceTiles(HashSet<Vector3Int> allTiles, int excludeY)
    {
        var surface = new List<Vector3Int>();
        foreach (var cell in allTiles)
        {
            if (cell.y == excludeY) continue;
            var above = new Vector3Int(cell.x, cell.y + 1, 0);
            if (!allTiles.Contains(above))
                surface.Add(cell);
        }
        surface.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return surface;
    }

    // ── Procedural object placement ───────────────────────────────────────────
    // Probabilities:
    //   20 % of surface tiles get an ElementPickup
    //   10 % of surface tiles get a SpikeHazard
    // Ranged enemies are placed on flat runs of >= MIN_PLATFORM_WIDTH tiles at the same Y.

    private static void PlaceObjectsOnSurface(List<Vector3Int> surface, int tilesPlaced)
    {
        if (surface.Count == 0)
        {
            Debug.LogWarning("[ProceduralLevelGenerator] No surface tiles found for object placement.");
            return;
        }

        // We place objects one world-unit above the surface tile centre.
        const float ABOVE = 1.1f;
        int pickupsPlaced = 0, spikesPlaced = 0, enemiesPlaced = 0;

        // Track consecutive same-Y runs for enemy placement.
        int runLength = 1;
        int prevY     = surface[0].y;
        int runStart  = 0;

        ElementStats[] elementCycle =
        {
            PlayerController.CreateEarthElement(),
            PlayerController.CreateFireElement(),
            PlayerController.CreateWaterElement(),
        };
        int elementIndex = 0;

        for (int i = 0; i < surface.Count; i++)
        {
            var cell  = surface[i];
            var world = new Vector3(cell.x + 0.5f, cell.y + ABOVE, 0f);

            // Track horizontal flat run length (for enemy placement).
            bool sameY = (i > 0 && surface[i].y == prevY && surface[i].x == surface[i - 1].x + 1);
            runLength  = sameY ? runLength + 1 : 1;
            prevY      = cell.y;

            float roll = UnityEngine.Random.value;

            // 20 % chance: element pickup.
            if (roll < 0.20f)
            {
                try
                {
                    var go = new GameObject($"Pickup_{cell.x}_{cell.y}");
                    go.transform.position   = world;
                    go.transform.localScale = Vector3.one * 0.5f;

                    go.AddComponent<SpriteRenderer>();
                    var cc = go.AddComponent<CircleCollider2D>();
                    cc.isTrigger = true;

                    var ep  = go.AddComponent<ElementPickup>();
                    if (ep != null)
                    {
                        ep.elementToGive = elementCycle[elementIndex % elementCycle.Length];
                        ep.respawnTime   = 5f;
                        elementIndex++;
                        pickupsPlaced++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ProceduralLevelGenerator] ElementPickup at {cell} failed: {e.Message}");
                }
            }
            // 10 % chance: spike hazard (only if the above slot wasn't already used).
            else if (roll < 0.30f)
            {
                try
                {
                    var go = new GameObject($"Spike_{cell.x}_{cell.y}");
                    go.transform.position   = new Vector3(cell.x + 0.5f, cell.y + 0.6f, 0f);
                    go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.color = new Color(0.7f, 0.7f, 0.8f);

                    var bc = go.AddComponent<BoxCollider2D>();
                    bc.isTrigger = true;

                    go.AddComponent<SpikeHazard>();
                    spikesPlaced++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ProceduralLevelGenerator] SpikeHazard at {cell} failed: {e.Message}");
                }
            }

            // Ranged enemy: place at the midpoint of flat runs >= MIN_PLATFORM_WIDTH.
            bool runEnded = (i == surface.Count - 1) ||
                            (surface[i + 1].y != cell.y) ||
                            (surface[i + 1].x != cell.x + 1);

            if (runEnded && runLength >= MIN_PLATFORM_WIDTH && UnityEngine.Random.value < 0.4f)
            {
                try
                {
                    int midX = surface[runStart + runLength / 2].x;
                    var go   = new GameObject($"RangedEnemy_{midX}_{cell.y}");
                    go.transform.position = new Vector3(midX + 0.5f, cell.y + ABOVE, 0f);

                    go.AddComponent<SpriteRenderer>();
                    var rb = go.AddComponent<Rigidbody2D>();
                    rb.bodyType = RigidbodyType2D.Kinematic;
                    go.AddComponent<BoxCollider2D>();

                    var patrol = go.AddComponent<EnemyPatrol>();
                    if (patrol != null)
                    {
                        patrol.leftBound      = surface[runStart].x;
                        patrol.rightBound     = surface[runStart + runLength - 1].x + 1f;
                        patrol.shootInterval  = UnityEngine.Random.Range(2f, 4f);
                        patrol.shootRange     = 12f;
                    }
                    enemiesPlaced++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ProceduralLevelGenerator] RangedEnemy placement failed: {e.Message}");
                }

                runStart = i + 1;
            }
            else if (!sameY)
            {
                runStart = i;
            }
        }

        Debug.Log($"[ProceduralLevelGenerator] Objects placed: " +
                  $"{pickupsPlaced} pickups, {spikesPlaced} spikes, {enemiesPlaced} enemies.");
    }}
