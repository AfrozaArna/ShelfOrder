using UnityEngine;
using UnityEngine.Rendering.Universal;

// Procedurally dresses the Level 1 scene with a simple, deliberately minimal cozy-
// library backdrop: one wall, one floor, one small bookshelf, one window, one
// hanging lamp, one plant - using only primitive shapes and procedurally generated
// sprites (GameUIHelper), no imported art assets. Purely decorative: it does not
// touch gameplay, colliders, or the books list, and everything is placed behind the
// table in Z so it never overlaps the interactive stack.
public static class LibraryEnvironment
{
    // Shared environment palette (walls/floor/wooden structures) - kept as one place so
    // Level 1, Level 2, Level 3, and the Main Menu/Level Select screens (which all call
    // through this same Build method) stay visually consistent.
    private static readonly Color LightBeigeWall = new Color32(0xE8, 0xDD, 0xC8, 0xFF);
    // 12% darker than the base Light Oak hex (#D8B98A), same hue/appearance, just deeper.
    private static readonly Color LightOakFloor = new Color(
        0xD8 / 255f * 0.88f, 0xB9 / 255f * 0.88f, 0x8A / 255f * 0.88f, 1f);
    private static readonly Color MediumBrownWood = new Color32(0x8B, 0x5E, 0x3C, 0xFF);
    // Same ~70% darkening ratio the old frame/plank pair used (0.22/0.32 ~= 0.7), so the
    // shelf's plank still reads as a distinct ledge under the books instead of blending
    // into a single flat block now that both use the same medium-brown family.
    private static readonly Color MediumBrownWoodDark = new Color(
        MediumBrownWood.r * 0.7f, MediumBrownWood.g * 0.7f, MediumBrownWood.b * 0.7f, 1f);

    // frameCenterY/frameHalfHeight/frameHalfWidth describe the camera's actual
    // computed view (see StackManager.FrameCameraOnStack) so the wall/floor/window
    // can size and position themselves to always cover the visible area, however
    // many books are in the scene - instead of guessing fixed numbers that could
    // leave gaps (or overlap the stack) once the book count changes.
    // centerX defaults to 0 (Level 1's stack is always horizontally centered at world
    // X=0, so its call site never needs to pass this) - Level 2's queue/desk layout
    // isn't symmetric about X=0, so it passes its camera's actual horizontal center
    // to keep the wall/floor/bookshelf/window aligned with what the camera really sees.
    // floorTopY defaults to null (Level 1 derives the floor's top edge from the camera
    // frame, as before) - Level 2 passes its exact visitor/desk floor baseline so the
    // floor graphic lines up perfectly with their feet instead of an approximation.
    // includeLamp defaults to true (Levels 1/2 keep their hanging lamp) - Level 3 is a
    // pure-UI quiz screen that wants only the even Global Light 2D with no secondary
    // point-light/glow accent at all, so it passes false to skip the lamp entirely.
    public static void Build(Vector3 tableAnchor, Vector3 topAnchor, float frameCenterY, float frameHalfHeight,
        float frameHalfWidth, Material sharedMaterial, float centerX = 0f, float? floorTopY = null, bool includeLamp = true)
    {
        GameObject root = new GameObject("LibraryEnvironment");

        BuildWall(root.transform, centerX, frameCenterY, frameHalfHeight, frameHalfWidth, sharedMaterial);
        BuildBookshelf(root.transform, centerX, frameCenterY, frameHalfHeight, sharedMaterial);
        BuildWindow(root.transform, centerX, frameCenterY, frameHalfHeight, frameHalfWidth, sharedMaterial);
        BuildFloor(root.transform, centerX, frameCenterY, frameHalfHeight, frameHalfWidth, sharedMaterial, floorTopY);

        float sideReach = Mathf.Max(frameHalfWidth - 1f, 1.2f);
        if (includeLamp)
            BuildHangingLamp(root.transform, topAnchor + new Vector3(Mathf.Min(2.5f, sideReach), 1.6f, 0f), sharedMaterial);
        BuildPlant(root.transform, tableAnchor + new Vector3(-Mathf.Min(3.1f, sideReach), 0.1f, 0f), 0.7f, sharedMaterial);
    }

    private static SpriteRenderer CreateSprite(Transform parent, string name, Sprite sprite, Color color,
        Vector3 localPos, Vector2 size, Material sharedMaterial)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = color;
        if (sharedMaterial != null) sr.sharedMaterial = sharedMaterial;
        return sr;
    }

    // Sized generously larger (1.3x) than the camera's actual view so it always
    // fully covers the frame, however far the camera zooms out for a bigger stack.
    private static void BuildWall(Transform parent, float centerX, float centerY, float halfHeight, float halfWidth, Material mat)
    {
        Sprite sprite = GameUIHelper.GetRoundedWoodSprite(0);
        Vector2 size = new Vector2(halfWidth * 2.6f, halfHeight * 2.6f);
        CreateSprite(parent, "Wall", sprite, LightBeigeWall, new Vector3(centerX, centerY, 9f), size, mat);
    }

    // Anchored to the bottom edge of the actual camera frame instead of a fixed Y,
    // so it never floats above the visible area or leaves a gap below it. When an
    // explicit floorTopY is given (Level 2), that becomes the floor's top edge exactly,
    // instead of deriving it from the camera frame - the frame-derived formula is only
    // an approximation and could leave a visible gap/overlap for asymmetric layouts.
    private static void BuildFloor(Transform parent, float centerX, float centerY, float halfHeight, float halfWidth, Material mat, float? floorTopY = null)
    {
        Sprite sprite = GameUIHelper.GetRoundedWoodSprite(0);
        float floorHeight = Mathf.Max(halfHeight * 0.9f, 2f);
        float floorTop = floorTopY ?? (centerY - halfHeight + floorHeight * 0.75f);
        float floorY = floorTop - floorHeight * 0.5f;
        Vector2 size = new Vector2(halfWidth * 2.6f, floorHeight);
        CreateSprite(parent, "Floor", sprite, LightOakFloor, new Vector3(centerX, floorY, 5f), size, mat);
    }

    // A single simple bookshelf (one frame, one plank, a handful of spines) - kept
    // deliberately small so the background reads as "a bookshelf behind the player"
    // rather than a whole wall of shelving.
    private static void BuildBookshelf(Transform parent, float centerX, float centerY, float halfHeight, Material mat)
    {
        GameObject shelf = new GameObject("Bookshelf");
        shelf.transform.SetParent(parent, false);
        float shelfY = centerY + halfHeight * 0.3f;
        shelf.transform.localPosition = new Vector3(centerX, shelfY, 7f);
        // ~13% smaller overall so it doesn't dominate the scene - scaling the whole
        // group (frame + plank + spines, all children below) keeps it centered on the
        // same point instead of needing to rescale each piece's size individually.
        shelf.transform.localScale = Vector3.one * 0.87f;

        Sprite frameSprite = GameUIHelper.GetRoundedWoodSprite(4);
        CreateSprite(shelf.transform, "ShelfFrame", frameSprite, MediumBrownWood,
            Vector3.zero, new Vector2(4.4f, 2.2f), mat);

        CreateSprite(shelf.transform, "Plank", frameSprite, MediumBrownWoodDark,
            new Vector3(0f, -0.2f, -0.1f), new Vector2(4.1f, 0.12f), mat);

        Color[] spineColors =
        {
            new Color(0.55f, 0.18f, 0.16f), new Color(0.16f, 0.30f, 0.22f), new Color(0.66f, 0.55f, 0.16f),
            new Color(0.24f, 0.24f, 0.42f), new Color(0.45f, 0.30f, 0.16f)
        };

        const int columns = 5;
        float spacing = 4.1f / columns;
        float startX = -4.1f * 0.5f;
        for (int col = 0; col < columns; col++)
        {
            float x = startX + col * spacing + spacing * 0.5f;
            float height = Random.Range(0.75f, 1.0f);
            CreateSprite(shelf.transform, "Spine_" + col, frameSprite, spineColors[col % spineColors.Length],
                new Vector3(x, -0.2f + height * 0.5f, -0.05f), new Vector2(spacing * 0.7f, height), mat);
        }
    }

    private static void BuildWindow(Transform parent, float centerX, float centerY, float halfHeight, float halfWidth, Material mat)
    {
        GameObject window = new GameObject("Window");
        window.transform.SetParent(parent, false);

        float windowX = centerX + Mathf.Min(3.8f, Mathf.Max(halfWidth - 1.6f, 1.5f));
        float windowY = centerY + halfHeight * 0.4f;
        window.transform.localPosition = new Vector3(windowX, windowY, 6.5f);

        Sprite frameSprite = GameUIHelper.GetRoundedWoodSprite(6);
        CreateSprite(window.transform, "Frame", frameSprite, MediumBrownWood, Vector3.zero, new Vector2(2.4f, 2.8f), mat);
        CreateSprite(window.transform, "Glass", frameSprite, new Color(0.78f, 0.87f, 0.80f), new Vector3(0, 0, -0.1f), new Vector2(2.0f, 2.4f), mat);

        GameObject beam = new GameObject("Sunbeam");
        beam.transform.SetParent(window.transform, false);
        beam.transform.localPosition = new Vector3(-1.4f, -1.6f, -0.3f);
        beam.transform.localScale = new Vector3(5f, 5f, 1f);
        SpriteRenderer sr = beam.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetGlowSprite();
        sr.color = new Color(1f, 0.92f, 0.7f, 0.32f);
        if (mat != null) sr.sharedMaterial = mat;
    }

    // A lamp hanging from the ceiling on a cord, rather than standing on furniture.
    // Includes a real Light2D point light (in addition to the painted glow sprite)
    // so it actually illuminates nearby sprites under the warm 2D lighting setup.
    private static void BuildHangingLamp(Transform parent, Vector3 anchor, Material mat)
    {
        GameObject lamp = new GameObject("HangingLamp");
        lamp.transform.SetParent(parent, false);
        lamp.transform.localPosition = anchor;

        Sprite woodSprite = GameUIHelper.GetRoundedWoodSprite(2);
        CreateSprite(lamp.transform, "Cord", woodSprite, new Color(0.15f, 0.1f, 0.07f),
            new Vector3(0, 0.55f, 0f), new Vector2(0.05f, 1.1f), mat);

        Sprite shadeSprite = GameUIHelper.GetBlobSprite();
        CreateSprite(lamp.transform, "Shade", shadeSprite, new Color(0.80f, 0.60f, 0.24f),
            new Vector3(0, 0f, -0.01f), new Vector2(0.6f, 0.4f), mat);

        GameObject glowGO = new GameObject("Glow");
        glowGO.transform.SetParent(lamp.transform, false);
        glowGO.transform.localPosition = new Vector3(0f, -0.3f, 0.15f);
        glowGO.transform.localScale = new Vector3(2.4f, 2.4f, 1f);
        SpriteRenderer sr = glowGO.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetGlowSprite();
        sr.color = new Color(1f, 0.85f, 0.55f, 0.5f);
        if (mat != null) sr.sharedMaterial = mat;

        GameObject lightGO = new GameObject("LampLight");
        lightGO.transform.SetParent(lamp.transform, false);
        lightGO.transform.localPosition = new Vector3(0f, -0.2f, 0f);
        Light2D pointLight = lightGO.AddComponent<Light2D>();
        pointLight.lightType = Light2D.LightType.Point;
        pointLight.color = new Color(1f, 0.85f, 0.55f);
        pointLight.intensity = 1.3f;
        pointLight.pointLightInnerRadius = 0.6f;
        pointLight.pointLightOuterRadius = 4.5f;
    }

    private static void BuildPlant(Transform parent, Vector3 position, float scale, Material mat)
    {
        GameObject plant = new GameObject("Plant");
        plant.transform.SetParent(parent, false);
        plant.transform.localPosition = position;
        plant.transform.localScale = Vector3.one * scale;

        Sprite woodSprite = GameUIHelper.GetRoundedWoodSprite(4);
        CreateSprite(plant.transform, "Pot", woodSprite, new Color(0.55f, 0.3f, 0.18f),
            new Vector3(0, -0.25f, 0f), new Vector2(0.5f, 0.35f), mat);

        Sprite leafSprite = GameUIHelper.GetBlobSprite();
        Color leafColor = new Color(0.16f, 0.36f, 0.2f);
        CreateSprite(plant.transform, "Leaf1", leafSprite, leafColor, new Vector3(-0.15f, 0.15f, -0.05f), new Vector2(0.35f, 0.45f), mat);
        CreateSprite(plant.transform, "Leaf2", leafSprite, leafColor, new Vector3(0.15f, 0.2f, -0.06f), new Vector2(0.3f, 0.5f), mat);
        CreateSprite(plant.transform, "Leaf3", leafSprite, leafColor * 1.15f, new Vector3(0f, 0.38f, -0.07f), new Vector2(0.3f, 0.4f), mat);
    }
}
