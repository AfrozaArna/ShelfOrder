using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TMPro;

// Level 2 - Queue (FIFO): a library service desk where visitors wait in line and must
// be served strictly in arrival order. Mirrors StackManager's structure/conventions
// (procedural visuals via GameUIHelper, shared LibraryEnvironment backdrop, camera
// auto-framing, wood-panel UI, shake/flash/toast wrong-input feedback) so Level 2
// matches Level 1's polish without duplicating its Stack-specific logic.
public class QueueManager : MonoBehaviour
{
    public List<GameObject> visitors;
    public GameObject levelCompleteText;

    [Header("Queue Layout")]
    public float lineHalfWidth = 2.45f; // ~30% tighter than the original 3.5, still leaves clear gaps between visitors
    public Vector2 visitorSize = new Vector2(0.6f, 1.3f);
    public Color[] visitorPalette = new Color[]
    {
        new Color32(0xD9, 0x38, 0x32, 0xFF), // red shirt
        new Color32(0x33, 0xA6, 0x59, 0xFF), // green shirt
        new Color32(0xD9, 0xAD, 0x33, 0xFF), // mustard shirt
        new Color32(0x3D, 0x6B, 0xBF, 0xFF), // blue shirt
        new Color32(0x8E, 0x44, 0xAD, 0xFF), // purple shirt
        new Color32(0xE0, 0x7B, 0x39, 0xFF), // orange shirt
    };
    private static readonly Color32 SkinTone = new Color32(0xE8, 0xC3, 0x9E, 0xFF);

    // Matches the new Light Beige wall color (LibraryEnvironment.LightBeigeWall) so any
    // sliver of camera clear color peeking past the (generously oversized) wall sprite
    // blends in seamlessly instead of showing the old dark brown.
    private static readonly Color32 BrightLibraryBackground = new Color32(0xE8, 0xDD, 0xC8, 0xFF);

    [Header("Service Desk Appearance")]
    public float deskWidth = 0.8f;   // narrower reception-counter footprint (-20% from 1.0)
    public float deskHeight = 0.65f; // reception-counter height - shorter than the librarian standing behind it
    public Color deskColor = new Color32(0x8B, 0x5E, 0x3C, 0xFF); // medium brown (library palette)
    public float deskToQueueGap = 0.6f; // horizontal gap between the desk and the first (closest) visitor

    [Header("Feedback")]
    public float wrongClickShakeDuration = 0.35f;
    public float serveMoveDuration = 0.4f;
    public float toastDuration = 1.5f;
    public AudioClip successSound; // optional - assign a free SFX clip; safely no-ops if left empty
    public AudioClip wrongVisitorSound; // optional - played on a wrong-visitor click
    public AudioClip keyCollectSound; // optional - played when the key is revealed
    public AudioClip levelCompleteSound; // optional - played when the completion popup appears

    // Duration matches Level 1's key-reveal timing exactly (StackManager.keyRevealDuration).
    // Scale is intentionally smaller than Level 1's 1.4 (~35% reduction) so the key looks
    // proportional next to the librarian and desk, rather than Level 1's book-stack scale.
    public float keyRevealDuration = 0.6f;
    public float keyRevealScale = 0.9f;

    private TextMeshProUGUI counterText;
    private TextMeshProUGUI toastText;
    private GameObject toastGroup;
    private Coroutine toastRoutine;
    private GameObject levelCompletePanel;
    private Material visitorMaterial;
    private SpriteRenderer doorSR;
    private SpriteRenderer doorHandleSR;
    private GameObject key; // procedural "Library Key", revealed when the queue empties

    // Level Complete popup entrance-animation targets (set in BuildLevelCompletePopup,
    // used by AnimatePopupReveal) - same approach as StackManager's Level 1 popup.
    private Image popupOverlayImage;
    private CanvasGroup popupCardGroup;
    private CanvasGroup popupTextGroup;
    private CanvasGroup popupButtonGroup;
    private float popupOverlayTargetAlpha;

    // Camera's computed frame (center + half-height), same pattern as
    // StackManager.FrameCameraOnStack, so LibraryEnvironment can size/center the
    // wall/floor/bookshelf/window to always cover the visible area. Unlike Level 1's
    // stack (always centered at X=0), the desk-on-left + queue-extending-right layout
    // isn't symmetric, so frameCenterX is tracked explicitly and threaded through to
    // LibraryEnvironment.Build instead of assuming 0.
    private float frameCenterX;
    private float frameCenterY;
    private float frameHalfHeight = 5f;
    private Vector3 deskAnchor;
    private Vector2 deskSize; // actual final desk size, set once in CreateServiceDesk
    private float librarianTopY; // her head's top Y, set in CreateLibrarian, used by FrameCameraOnQueue
    private float queueStartX; // x position of visitor 0 (closest to the desk)
    private float visitorSpacing; // fixed gap between queue slots, locked in once from the original full queue size
    private Vector3 frontAnchor; // where a served visitor walks toward before fading out
    private GameObject librarian;

    // The single shared floor baseline: the Y where every floor-touching element's
    // BOTTOM edge sits (visitors' shirt sprite, the desk, the librarian, and the floor
    // graphic itself). Everything derives its vertical position from this one value so
    // nothing can end up floating above - or sinking below - anything else.
    private float groundY;
    private float visitorShirtHeight; // set in ApplyVisitorAppearance; the visual (not collider) body height

    void Start()
    {
        ValidateVisitorCount();
        CaptureVisitorMaterial();
        ApplyVisitorAppearance();

        float visitorY = visitors.Count > 0 ? visitors[0].transform.position.y : 0f;
        groundY = visitorY - visitorShirtHeight * 0.5f;

        // The desk is built first (a fixed, standalone anchor); the queue is then laid
        // out to its right, so "first visitor closest to desk, last visitor farthest"
        // falls straight out of the existing index-0-is-front convention.
        deskAnchor = CreateServiceDesk();
        queueStartX = deskAnchor.x + deskSize.x * 0.5f + deskToQueueGap + visitorSize.x * 0.5f;
        RepositionVisitorsInstant();

        librarian = CreateLibrarian();
        key = CreateLibraryKey();

        // Ground-level (visitor feet) reference for furniture/props that shouldn't float
        // at the desk's own, taller, height.
        Vector3 groundAnchor = new Vector3(deskAnchor.x, groundY, 0f);

        // A served visitor only takes one short step left, toward the desk, before fading
        // out - kept at the visitors' own floor Y (not the desk's taller center Y) so it
        // stays on the same baseline as the rest of the queue while it walks.
        frontAnchor = new Vector3(deskAnchor.x + deskSize.x * 0.5f + 0.15f, visitorY, -0.3f);

        CreateExitDoor(new Vector3(deskAnchor.x - deskSize.x * 0.5f - 0.8f, groundY + 1.3f, 2f));

        FrameCameraOnQueue();
        ApplyWarmLighting();

        Vector3 topAnchor = new Vector3(frameCenterX, frameCenterY + frameHalfHeight * 0.6f, 0f);
        float aspect = Camera.main != null ? Camera.main.aspect : 1.6f;
        float frameHalfWidth = frameHalfHeight * aspect;
        LibraryEnvironment.Build(groundAnchor, topAnchor, frameCenterY, frameHalfHeight, frameHalfWidth, visitorMaterial, frameCenterX, groundY);
        SoftenLampPointLight();

        BuildUI();
        UpdateVisitorColliders();
        UpdateCounter();

        Debug.Log("Queue ready! Visitors waiting: " + visitors.Count);
    }

    // The queue must always have exactly 6 visitors, and (unlike Level 1's book stack)
    // this level is never allowed to instantiate or destroy visitors to make that true -
    // they must already exist in the Hierarchy and be wired into the visitors list. This
    // only warns so a scene-content mistake is caught, it never creates/removes anything.
    void ValidateVisitorCount()
    {
        if (visitors.Count != 6)
        {
            Debug.LogWarning("QueueManager: expected exactly 6 visitors but found " + visitors.Count +
                ". Add/remove Visitor_N GameObjects in the Hierarchy and reassign the 'visitors' list in the Inspector.");
        }
    }

    void CaptureVisitorMaterial()
    {
        if (visitors.Count == 0) return;
        SpriteRenderer sr = visitors[0].GetComponent<SpriteRenderer>();
        if (sr != null) visitorMaterial = sr.sharedMaterial;
    }

    // Gives each visitor a simple cartoon look: a colored shirt (the root sprite/collider),
    // a round head with eyes, and a small ID badge. Uses GameUIHelper.GetOrCreateChild
    // throughout, so re-running this never creates duplicate child GameObjects.
    void ApplyVisitorAppearance()
    {
        Sprite bodySprite = GameUIHelper.GetRoundedSprite(6);
        float shirtHeight = visitorSize.y * 0.62f;
        visitorShirtHeight = shirtHeight; // recorded so Start() can pin groundY to the visual (not collider) body

        for (int i = 0; i < visitors.Count; i++)
        {
            GameObject visitor = visitors[i];
            if (visitor == null) continue;

            visitor.transform.localScale = Vector3.one;

            SpriteRenderer sr = visitor.GetComponent<SpriteRenderer>();
            if (sr == null) sr = visitor.AddComponent<SpriteRenderer>();

            Color shirtColor = visitorPalette.Length > 0 ? visitorPalette[i % visitorPalette.Length] : Color.white;
            sr.sprite = bodySprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(visitorSize.x, shirtHeight);
            sr.color = shirtColor;

            // The clickable area covers the whole character (shirt + head), not just the
            // shirt sprite, so it's offset upward to center over both.
            BoxCollider2D col = visitor.GetComponent<BoxCollider2D>();
            if (col == null) col = visitor.AddComponent<BoxCollider2D>();
            col.size = visitorSize;
            col.offset = new Vector2(0f, visitorSize.y * 0.14f);

            if (visitor.GetComponent<VisitorClick>() == null) visitor.AddComponent<VisitorClick>();

            AddVisitorHead(visitor, sr, shirtHeight);
            AddVisitorBadge(visitor, sr, shirtHeight);
        }
    }

    void AddVisitorHead(GameObject visitor, SpriteRenderer bodySR, float shirtHeight)
    {
        GameObject head = GameUIHelper.GetOrCreateChild(visitor.transform, "Head");
        float headDiameter = visitorSize.x * 0.85f;
        float headY = shirtHeight * 0.5f + headDiameter * 0.32f;
        head.transform.localPosition = new Vector3(0f, headY, -0.01f);

        SpriteRenderer sr = head.GetComponent<SpriteRenderer>();
        if (sr == null) sr = head.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetBlobSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(headDiameter, headDiameter);
        sr.color = SkinTone;
        if (bodySR != null) sr.sharedMaterial = bodySR.sharedMaterial;

        AddVisitorEye(head.transform, "EyeLeft", -headDiameter * 0.2f, headDiameter, bodySR);
        AddVisitorEye(head.transform, "EyeRight", headDiameter * 0.2f, headDiameter, bodySR);
    }

    void AddVisitorEye(Transform headTransform, string name, float offsetX, float headDiameter, SpriteRenderer bodySR)
    {
        GameObject eye = GameUIHelper.GetOrCreateChild(headTransform, name);
        eye.transform.localPosition = new Vector3(offsetX, headDiameter * 0.05f, -0.005f);

        SpriteRenderer sr = eye.GetComponent<SpriteRenderer>();
        if (sr == null) sr = eye.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetBlobSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        float eyeSize = headDiameter * 0.14f;
        sr.size = new Vector2(eyeSize, eyeSize);
        sr.color = new Color(0.15f, 0.1f, 0.08f);
        if (bodySR != null) sr.sharedMaterial = bodySR.sharedMaterial;
    }

    void AddVisitorBadge(GameObject visitor, SpriteRenderer bodySR, float shirtHeight)
    {
        GameObject badge = GameUIHelper.GetOrCreateChild(visitor.transform, "Badge");
        badge.transform.localPosition = new Vector3(visitorSize.x * 0.18f, shirtHeight * 0.12f, -0.008f);

        SpriteRenderer sr = badge.GetComponent<SpriteRenderer>();
        if (sr == null) sr = badge.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedSprite(2);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(visitorSize.x * 0.28f, visitorSize.x * 0.2f);
        sr.color = GameUIHelper.Cream;
        if (bodySR != null) sr.sharedMaterial = bodySR.sharedMaterial;
    }

    // Snaps every visitor to an evenly-spaced horizontal line starting at queueStartX
    // (index 0 = closest to the desk, highest index = farthest away). Used once at
    // Start(); ServeVisitor animates subsequent re-spacing smoothly instead.
    void RepositionVisitorsInstant()
    {
        if (visitors.Count == 0) return;

        // Locked in from the ORIGINAL (full) queue size and reused by
        // SlideRemainingVisitorsForward - recomputing spacing from the current, shrinking
        // count would stretch the line to always span lineHalfWidth*2, which pins the last
        // visitor's position at queueStartX + lineHalfWidth*2 forever (the (Count-1) term
        // cancels out of (Count-1)*spacing), making them look like they never move forward.
        // A fixed spacing means every Serve() shifts each remaining visitor left by exactly
        // one step, so the line visibly shortens toward the desk instead of re-stretching.
        visitorSpacing = visitors.Count > 1 ? (lineHalfWidth * 2f) / (visitors.Count - 1) : 0f;
        float y = visitors[0].transform.position.y;

        for (int i = 0; i < visitors.Count; i++)
        {
            if (visitors[i] == null) continue;
            float x = queueStartX + i * visitorSpacing;
            visitors[i].transform.position = new Vector3(x, y, 0f);
        }
    }

    // A tall, narrow "Library Desk" standing to the left of the queue. Visitors are laid
    // out to its right in strict left-to-right order (closest = index 0 = next to be
    // served), which visually reinforces FIFO: the line empties from the desk outward.
    // Returns the world position used as the anchor for the desk + environment dressing.
    Vector3 CreateServiceDesk()
    {
        if (visitors.Count == 0) return Vector3.zero;

        deskSize = new Vector2(deskWidth, deskHeight);

        // Bottom edge pinned to the shared floor baseline, exactly like every other
        // floor-touching element - not a fixed offset from the visitors' center Y, which
        // previously let the desk's bottom drift to a different height than their feet.
        float deskY = groundY + deskHeight * 0.5f;
        Vector3 deskPos = new Vector3(0f, deskY, 1f);

        GameObject desk = new GameObject("LibraryDesk");
        desk.transform.position = deskPos;

        SpriteRenderer sr = desk.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedWoodSprite(8);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = deskSize;
        sr.color = deskColor;
        if (visitorMaterial != null) sr.sharedMaterial = visitorMaterial;
        // Explicit sortingOrder (not just a smaller Z) so the desk reliably renders in
        // front of the librarian regardless of how this project's renderer breaks Z-ties -
        // sortingOrder takes priority over Z within the same sorting layer, everything
        // else here still defaults to 0.
        sr.sortingOrder = 1;

        return deskPos;
    }

    // A simple librarian standing behind the desk - built from the same procedural
    // head+body approach as the visitors (reusing AddVisitorEye), but taller and in a
    // distinct color so she reads as staff rather than another person in line. Her feet
    // are pinned to the exact same groundY baseline as the desk and every visitor.
    GameObject CreateLibrarian()
    {
        GameObject librarianGO = new GameObject("Librarian");

        float bodyHeight = visitorShirtHeight * 1.35f;
        float bodyWidth = visitorSize.x * 1.15f;
        float librarianY = groundY + bodyHeight * 0.5f;
        float headDiameter = bodyWidth * 0.8f;

        // Her head's top, used by FrameCameraOnQueue so the camera frame always includes
        // her now that she's taller than the (counter-height) desk in front of her.
        librarianTopY = librarianY + bodyHeight * 0.5f + headDiameter * 0.8f;

        // z=1.2, behind the desk's z=1 - a real service desk sits in front of the person
        // standing at it. The desk is now shorter than she is (counter height), so most
        // of her upper body and head are visible above it; centered on the desk.
        librarianGO.transform.position = new Vector3(deskAnchor.x, librarianY, 1.2f);

        SpriteRenderer sr = librarianGO.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedSprite(6);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(bodyWidth, bodyHeight);
        sr.color = new Color32(0x4A, 0x3B, 0x6B, 0xFF); // deep plum cardigan, distinct from any visitor shirt color
        if (visitorMaterial != null) sr.sharedMaterial = visitorMaterial;

        GameObject head = new GameObject("Head");
        head.transform.SetParent(librarianGO.transform, false);
        head.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f + headDiameter * 0.3f, -0.01f);

        SpriteRenderer headSR = head.AddComponent<SpriteRenderer>();
        headSR.sprite = GameUIHelper.GetBlobSprite();
        headSR.drawMode = SpriteDrawMode.Sliced;
        headSR.size = new Vector2(headDiameter, headDiameter);
        headSR.color = SkinTone;
        if (visitorMaterial != null) headSR.sharedMaterial = visitorMaterial;

        AddVisitorEye(head.transform, "EyeLeft", -headDiameter * 0.2f, headDiameter, sr);
        AddVisitorEye(head.transform, "EyeRight", headDiameter * 0.2f, headDiameter, sr);

        return librarianGO;
    }

    // A procedural "Library Key" (Level 1's key is a scene-authored GameObject; Level 2
    // has none, so it's built here instead) - starts hidden, resting on the desk in front
    // of the librarian, revealed via RevealKey() once the queue is cleared. Same gold
    // tone as Level 1's key (0.766, 0.596, 0.178).
    GameObject CreateLibraryKey()
    {
        GameObject keyGO = new GameObject("LibraryKey");
        keyGO.transform.position = new Vector3(deskAnchor.x, deskAnchor.y + deskHeight * 0.5f + 0.3f, -1f);

        SpriteRenderer sr = keyGO.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetKeySprite();
        sr.color = new Color(0.7660377f, 0.5960206f, 0.1777785f, 1f);
        if (visitorMaterial != null) sr.sharedMaterial = visitorMaterial;
        keyGO.transform.localScale = Vector3.one * 0.5f;

        keyGO.SetActive(false);
        return keyGO;
    }

    // Purely decorative exit door: starts "locked" (dark/dull) and brightens once the
    // queue is cleared. No scene-loading or navigation logic is attached to it. (Mirrors
    // StackManager's CreateExitDoor/UnlockDoor - kept as a separate small implementation
    // here rather than a shared helper so Level 1 never has to be touched.)
    void CreateExitDoor(Vector3 position)
    {
        GameObject door = new GameObject("ExitDoor");
        door.transform.position = position;

        doorSR = door.AddComponent<SpriteRenderer>();
        doorSR.sprite = GameUIHelper.GetRoundedWoodSprite(6);
        doorSR.drawMode = SpriteDrawMode.Sliced;
        doorSR.size = new Vector2(1.2f, 2.4f);
        doorSR.color = new Color(0.16f, 0.11f, 0.08f);
        if (visitorMaterial != null) doorSR.sharedMaterial = visitorMaterial;

        GameObject handle = new GameObject("DoorHandle");
        handle.transform.SetParent(door.transform, false);
        handle.transform.localPosition = new Vector3(0.4f, 0f, -0.02f);

        doorHandleSR = handle.AddComponent<SpriteRenderer>();
        doorHandleSR.sprite = GameUIHelper.GetBlobSprite();
        doorHandleSR.drawMode = SpriteDrawMode.Sliced;
        doorHandleSR.size = new Vector2(0.14f, 0.14f);
        doorHandleSR.color = new Color(0.32f, 0.28f, 0.24f);
        if (visitorMaterial != null) doorHandleSR.sharedMaterial = visitorMaterial;
    }

    void UnlockDoor()
    {
        if (doorSR != null) doorSR.color = new Color(0.48f, 0.32f, 0.17f);
        if (doorHandleSR != null) doorHandleSR.color = GameUIHelper.GoldColor;
    }

    // Auto-frames the camera around the queue line + desk so the whole line, the desk,
    // and the UI are all visible with no clipping and minimal empty space, however many
    // visitors remain. Horizontal fit and vertical fit are computed separately and the
    // camera uses whichever needs the larger orthographic size.
    void FrameCameraOnQueue()
    {
        if (visitors.Count == 0) return;

        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        float visitorY = visitors[0].transform.position.y;
        foreach (GameObject v in visitors)
        {
            if (v == null) continue;
            minX = Mathf.Min(minX, v.transform.position.x);
            maxX = Mathf.Max(maxX, v.transform.position.x);
        }
        minX = Mathf.Min(minX - visitorSize.x * 0.5f, deskAnchor.x - deskSize.x * 0.5f) - 0.4f;
        maxX = Mathf.Max(maxX + visitorSize.x * 0.5f, deskAnchor.x + deskSize.x * 0.5f) + 0.4f;

        frameCenterX = (minX + maxX) * 0.5f;
        float requiredHalfWidth = (maxX - minX) * 0.5f + 0.6f;

        // The desk now stands beside the queue (not above it) and the desk is
        // counter-height (shorter than the librarian standing behind it), so the
        // vertical frame must cover whichever of the visitors/desk/librarian extends
        // furthest up/down - not just the visitors alone.
        float visitorBottom = visitorY - visitorSize.y * 0.5f;
        float visitorTop = visitorY + visitorSize.y * 0.7f; // shirt+head combined, see AddVisitorHead
        float deskBottom = deskAnchor.y - deskSize.y * 0.5f;
        float deskTop = deskAnchor.y + deskSize.y * 0.5f;

        float minY = Mathf.Min(visitorBottom, deskBottom) - 0.4f;
        float rawMaxY = Mathf.Max(Mathf.Max(visitorTop, deskTop), librarianTopY) + 0.3f;

        // Same algebraic headroom trick as StackManager.FrameCameraOnStack: reserve the
        // top ~16% of the screen (header + instruction banners) so they can never cover
        // the desk or the visitors, instead of guessing a flat offset.
        const float uiTopReservedFraction = 0.16f;
        float maxY = minY + (rawMaxY - minY) / (1f - uiTopReservedFraction);

        frameCenterY = (minY + maxY) * 0.5f;
        float verticalHalfHeight = Mathf.Max((maxY - minY) * 0.5f, 1f);

        float aspect = cam.aspect;
        float widthDrivenHalfHeight = requiredHalfWidth / aspect;

        frameHalfHeight = Mathf.Max(verticalHalfHeight, widthDrivenHalfHeight);

        cam.transform.position = new Vector3(frameCenterX, frameCenterY, cam.transform.position.z);
        cam.orthographicSize = frameHalfHeight;
        cam.backgroundColor = BrightLibraryBackground;
    }

    void ApplyWarmLighting()
    {
        Light2D light = Object.FindFirstObjectByType<Light2D>();
        if (light == null)
        {
            // Unlike Level 1's scene, Level 2's scene has no pre-placed Global Light 2D -
            // so the hanging lamp's Point Light was the only light source, and its falloff
            // over Level 2's much wider layout read as a large dark shadow/vignette. Create
            // the missing Global Light here (Level 2's own scene object, doesn't touch
            // Level 1) so the whole gameplay area gets flat, even illumination like Level 1.
            GameObject lightGO = new GameObject("Global Light 2D");
            light = lightGO.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
        }

        // Brighter/lighter than Level 1's tone - this is Level 2's own separate Light2D
        // (each scene has its own), so raising it here has no effect on Level 1.
        light.color = new Color(1f, 0.92f, 0.8f);
        light.intensity = 1.6f;
    }

    // LibraryEnvironment's hanging lamp includes a Point Light2D with a fixed radius
    // (fine for Level 1's compact vertical stack). Level 2's queue is much wider, so that
    // radius doesn't reach the desk/far visitors, and its falloff reads as a dark
    // vignette. Rather than changing the shared LibraryEnvironment code (which would also
    // affect Level 1), this widens/softens that one light here, after it's been created,
    // so the scene-wide Global Light carries the even "soft warm library light" and the
    // point light is just a subtle accent near the lamp instead of the dominant source.
    void SoftenLampPointLight()
    {
        Light2D[] lights = Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        foreach (Light2D light in lights)
        {
            if (light.lightType != Light2D.LightType.Point) continue;

            light.intensity = 0.5f;
            light.pointLightOuterRadius = Mathf.Max(light.pointLightOuterRadius, frameHalfHeight * 4f);
        }

        // The window's painted "Sunbeam" glow sprite (independent of any real light) reads
        // as an unwanted bright patch on the right side - same fix already applied to
        // Level 1 (StackManager.FlattenDecorativeGlow), done here as a post-Build tweak so
        // the shared LibraryEnvironment code (and Level 3/menus) aren't affected.
        GameObject envRoot = GameObject.Find("LibraryEnvironment");
        if (envRoot == null) return;

        Transform sunbeam = envRoot.transform.Find("Window/Sunbeam");
        if (sunbeam != null)
        {
            SpriteRenderer sr = sunbeam.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        // Level 2 only - shrinks further on top of the shared bookshelf size in
        // LibraryEnvironment.cs (which Level 1/3/menus also use and aren't asked to
        // change here). 0.8 on top of that shared 0.87 scale = ~30% smaller than the
        // original size overall.
        Transform bookshelf = envRoot.transform.Find("Bookshelf");
        if (bookshelf != null)
            bookshelf.localScale *= 0.8f;

        // Level 2 only, same reasoning as the bookshelf above - shrinks the window
        // (frame + glass) in place without touching the shared LibraryEnvironment size
        // used by Level 1/3/menus.
        Transform window = envRoot.transform.Find("Window");
        if (window != null)
            window.localScale *= 0.75f;
    }

    void BuildUI()
    {
        Canvas canvas = GameUIHelper.EnsureCanvas();

        GameUIHelper.CreateWoodPanel(canvas.transform, "HeaderBanner", GameUIHelper.DarkWood,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(400, 70));

        GameUIHelper.CreateText(canvas.transform, "HeaderText", "LEVEL 2 – QUEUE (FIFO)", 22,
            GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(400, 44));

        BuildCounter(canvas.transform);
        BuildToast(canvas.transform);

        BuildLevelCompletePopup(canvas.transform);
    }

    // Exact port of StackManager.BuildLevelCompletePopup (Level 1) so both levels' popups
    // are pixel-for-pixel identical - same cream card, gold border, rounded corners,
    // shadow, size, entrance animation, and typography. Only the two buttons' actions
    // differ (Level 3 instead of Level 2, and this scene instead of Level 1's).
    void BuildLevelCompletePopup(Transform canvasTransform)
    {
        Color creamCard = new Color32(0xF7, 0xF1, 0xE3, 0xFF);
        Color goldBorder = new Color32(0xD4, 0xAF, 0x37, 0xFF);
        Color darkBrownText = new Color32(0x4A, 0x2F, 0x1B, 0xFF);

        GameObject overlay = GameUIHelper.BuildOverlayCard(canvasTransform, "LevelCompleteOverlay",
            new Vector2(300, 230), out Transform card, creamCard, useWoodTexture: false, borderColorOverride: goldBorder);

        popupOverlayImage = overlay.GetComponent<Image>();
        // Dim enough to make the popup stand out, light enough that the library scene
        // behind it stays clearly visible.
        popupOverlayTargetAlpha = 0.35f;
        Color overlayColor = popupOverlayImage.color;
        overlayColor.a = popupOverlayTargetAlpha;
        popupOverlayImage.color = overlayColor;

        Shadow shadow = card.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(0, -6);

        popupCardGroup = card.gameObject.AddComponent<CanvasGroup>();

        GameObject textGroupGO = new GameObject("TextGroup", typeof(RectTransform));
        textGroupGO.transform.SetParent(card, false);
        RectTransform textGroupRect = textGroupGO.GetComponent<RectTransform>();
        textGroupRect.anchorMin = Vector2.zero;
        textGroupRect.anchorMax = Vector2.one;
        textGroupRect.sizeDelta = Vector2.zero;
        textGroupRect.anchoredPosition = Vector2.zero;
        popupTextGroup = textGroupGO.AddComponent<CanvasGroup>();

        // Plain text, no emoji - the project's TMP font doesn't have a trophy glyph, so
        // the old "\U0001F3C6" showed as a missing-glyph box next to the title instead
        // of an actual trophy icon.
        GameUIHelper.CreateText(textGroupGO.transform, "Title", "Level Complete!", 22,
            darkBrownText, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(260, 32));

        if (levelCompleteText != null)
        {
            levelCompleteText.transform.SetParent(textGroupGO.transform, false);
            RectTransform subtitleRect = levelCompleteText.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0.5f, 1f);
            subtitleRect.anchorMax = new Vector2(0.5f, 1f);
            subtitleRect.pivot = new Vector2(0.5f, 0.5f);
            subtitleRect.anchoredPosition = new Vector2(0, -74);
            subtitleRect.sizeDelta = new Vector2(260, 20);

            TextMeshProUGUI subtitleTMP = levelCompleteText.GetComponent<TextMeshProUGUI>();
            if (subtitleTMP != null)
            {
                subtitleTMP.text = "You found the Library Key.";
                subtitleTMP.fontSize = 13;
                subtitleTMP.fontStyle = FontStyles.Normal;
                subtitleTMP.color = darkBrownText;
                subtitleTMP.alignment = TextAlignmentOptions.Center;
            }

            levelCompleteText.SetActive(true);
        }

        GameObject buttonGroupGO = new GameObject("ButtonGroup", typeof(RectTransform));
        buttonGroupGO.transform.SetParent(card, false);
        RectTransform buttonGroupRect = buttonGroupGO.GetComponent<RectTransform>();
        buttonGroupRect.anchorMin = Vector2.zero;
        buttonGroupRect.anchorMax = Vector2.one;
        buttonGroupRect.sizeDelta = Vector2.zero;
        buttonGroupRect.anchoredPosition = Vector2.zero;
        popupButtonGroup = buttonGroupGO.AddComponent<CanvasGroup>();

        // Next Level -> Level 3 (Quiz); the door is already unlocked by ServeVisitor when
        // the queue empties, so this button only needs to load the next scene. Narrower
        // (200 vs 240) so they don't stretch too wide across the card.
        GameUIHelper.CreateFlatButton(buttonGroupGO.transform, "NextLevelButton", "Next Level",
            goldBorder, darkBrownText,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(200, 40),
            () => SceneManager.LoadScene("Level3_Quiz"));

        GameUIHelper.CreateFlatButton(buttonGroupGO.transform, "ReplayButton", "Replay",
            new Color32(0x8B, 0x5E, 0x3C, 0xFF), Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(200, 40),
            () => SceneManager.LoadScene(SceneManager.GetActiveScene().name));

        overlay.SetActive(false);
        levelCompletePanel = overlay;
    }

    // Exact port of StackManager.AnimatePopupReveal/FadeGroup (Level 1) - card fades/
    // scales in (90% -> 100%) over ~0.3s alongside the overlay dimming in, then the
    // title/subtitle fade in, then finally the buttons fade in.
    private IEnumerator AnimatePopupReveal()
    {
        if (levelCompletePanel == null) yield break;
        levelCompletePanel.SetActive(true);

        if (popupTextGroup != null) popupTextGroup.alpha = 0f;
        if (popupButtonGroup != null) popupButtonGroup.alpha = 0f;
        if (popupCardGroup != null) popupCardGroup.alpha = 0f;
        if (popupCardGroup != null) popupCardGroup.transform.localScale = Vector3.one * 0.9f;

        Color startOverlay = popupOverlayImage.color;
        startOverlay.a = 0f;
        popupOverlayImage.color = startOverlay;

        const float duration = 0.3f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            if (popupCardGroup != null)
            {
                popupCardGroup.alpha = p;
                popupCardGroup.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, p);
            }
            Color c = popupOverlayImage.color;
            c.a = Mathf.Lerp(0f, popupOverlayTargetAlpha, p);
            popupOverlayImage.color = c;
            yield return null;
        }
        if (popupCardGroup != null)
        {
            popupCardGroup.alpha = 1f;
            popupCardGroup.transform.localScale = Vector3.one;
        }
        Color finalOverlay = popupOverlayImage.color;
        finalOverlay.a = popupOverlayTargetAlpha;
        popupOverlayImage.color = finalOverlay;

        yield return StartCoroutine(FadeGroup(popupTextGroup, 0.2f));
        yield return StartCoroutine(FadeGroup(popupButtonGroup, 0.2f));
    }

    private IEnumerator FadeGroup(CanvasGroup group, float duration)
    {
        if (group == null) yield break;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Clamp01(t / duration);
            yield return null;
        }
        group.alpha = 1f;
    }

    // Mirrors StackManager.BuildCounter: one wood panel group with a label line and a
    // large number, so its position/size only needs to be set in one place.
    void BuildCounter(Transform canvasTransform)
    {
        GameObject group = new GameObject("CounterGroup", typeof(RectTransform));
        group.transform.SetParent(canvasTransform, false);
        RectTransform groupRect = group.GetComponent<RectTransform>();
        groupRect.anchorMin = new Vector2(1f, 1f);
        groupRect.anchorMax = new Vector2(1f, 1f);
        groupRect.pivot = new Vector2(1f, 1f);
        groupRect.anchoredPosition = new Vector2(-24, -24);
        groupRect.sizeDelta = new Vector2(170, 64);

        GameUIHelper.CreateWoodPanel(group.transform, "CounterBackground", GameUIHelper.DarkWood,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 14);

        GameUIHelper.CreateText(group.transform, "CounterLabel", "Visitors Remaining", 13,
            GameUIHelper.Cream, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(150, 20));

        counterText = GameUIHelper.CreateText(group.transform, "CounterValue", "", 24,
            GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 16), new Vector2(100, 28));
    }

    void BuildToast(Transform canvasTransform)
    {
        toastGroup = new GameObject("ToastGroup", typeof(RectTransform));
        toastGroup.transform.SetParent(canvasTransform, false);
        RectTransform groupRect = toastGroup.GetComponent<RectTransform>();
        groupRect.anchorMin = new Vector2(0.5f, 0f);
        groupRect.anchorMax = new Vector2(0.5f, 0f);
        groupRect.anchoredPosition = new Vector2(0, 50);
        groupRect.sizeDelta = new Vector2(380, 42); // matches Level 1's toast exactly

        // Same dark brown/gold styling as Level 1's toast (StackManager.BuildToast) -
        // built via the Color32->Color implicit conversion (which correctly normalizes
        // 0-255 byte channels to 0-1 floats). The previous version read
        // GameUIHelper.DarkWood.r/.g/.b directly as Color32 bytes and fed them straight
        // into new Color(...)'s 0-1 float channels - massively overbright, clipping to
        // white. That's the "large white notification bar" this replaces.
        Color toastPanelColor = new Color32(0x3D, 0x1F, 0x0A, 0xFF);
        toastPanelColor.a = 0.95f;
        GameUIHelper.CreateWoodPanel(toastGroup.transform, "ToastPanel", toastPanelColor,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 16);

        Color toastTextColor = new Color32(0xFF, 0xD7, 0x00, 0xFF);
        toastText = GameUIHelper.CreateText(toastGroup.transform, "ToastText", "", 15,
            toastTextColor, FontStyles.Bold, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        toastGroup.SetActive(false);
    }

    void UpdateCounter()
    {
        if (counterText != null)
            counterText.text = visitors.Count.ToString();
    }

    // Every visitor's collider stays enabled so wrong-visitor clicks actually register -
    // a disabled Collider2D means Unity never calls OnMouseDown at all, which would
    // silently swallow clicks on non-front visitors before they could ever trigger the
    // "wrong visitor" feedback. "Only the front visitor can be served" is enforced by the
    // frontVisitor == this.gameObject comparison in VisitorClick, not by disabling clicks.
    void UpdateVisitorColliders()
    {
        foreach (GameObject v in visitors)
        {
            if (v == null) continue;
            BoxCollider2D col = v.GetComponent<BoxCollider2D>();
            if (col != null) col.enabled = true;
        }
    }

    public GameObject GetFrontVisitor()
    {
        if (visitors.Count == 0) return null;
        return visitors[0]; // first in list = front of queue
    }

    public void ServeVisitor()
    {
        if (visitors.Count == 0) return;

        GameObject served = visitors[0];
        visitors.RemoveAt(0);

        Debug.Log("Visitor served! Remaining: " + visitors.Count);
        UpdateCounter();
        StartCoroutine(AnimateVisitorServed(served));
        StartCoroutine(SlideRemainingVisitorsForward());

        if (visitors.Count == 0)
        {
            StartCoroutine(RevealKey());
            UnlockDoor();
            StartCoroutine(ShowLevelCompletePanelDelayed(1f));
            Debug.Log("QUEUE EMPTY! Level Complete!");
        }
        else
        {
            UpdateVisitorColliders();
        }
    }

    // Reveals the hidden key with a soft pop (fade + scale) plus a lingering golden glow -
    // exact port of StackManager.RevealKey (Level 1), minus the stack-center repositioning
    // step since this key's position (fixed, next to the desk) never needs to move.
    private IEnumerator RevealKey()
    {
        if (key == null) yield break;

        AudioManager.PlaySfx(keyCollectSound, key.transform.position);

        key.SetActive(true);
        SpriteRenderer keySR = key.GetComponent<SpriteRenderer>();
        Vector3 baseScale = Vector3.one * keyRevealScale;
        Color baseColor = keySR != null ? keySR.color : Color.white;

        GameObject glow = new GameObject("KeyGlow");
        glow.transform.SetParent(key.transform, false);
        glow.transform.localPosition = new Vector3(0f, 0f, 0.2f);
        glow.transform.localScale = new Vector3(3f, 3f, 1f);
        SpriteRenderer glowSR = glow.AddComponent<SpriteRenderer>();
        glowSR.sprite = GameUIHelper.GetGlowSprite();
        glowSR.color = new Color(1f, 0.85f, 0.3f, 0f);

        if (keySR != null)
        {
            Color transparent = baseColor;
            transparent.a = 0f;
            keySR.color = transparent;
        }
        key.transform.localScale = baseScale * 0.6f;

        float t = 0f;
        while (t < keyRevealDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / keyRevealDuration);

            if (keySR != null)
            {
                Color c = baseColor;
                c.a = p;
                keySR.color = c;
            }
            key.transform.localScale = Vector3.Lerp(baseScale * 0.6f, baseScale, p);

            Color gc = glowSR.color;
            gc.a = Mathf.Sin(p * Mathf.PI) * 0.6f;
            glowSR.color = gc;

            yield return null;
        }

        if (keySR != null) keySR.color = baseColor;
        key.transform.localScale = baseScale;

        Color finalGlow = glowSR.color;
        finalGlow.a = 0.35f;
        glowSR.color = finalGlow;
    }

    // Purely cosmetic: the visitor is already removed from game state (FIFO list) by the
    // time this runs, so it only animates the leftover visual object - walking a short
    // distance toward the desk while fading out - before destroying it.
    private IEnumerator AnimateVisitorServed(GameObject visitor)
    {
        if (visitor == null) yield break;

        Collider2D col = visitor.GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        // Routed through AudioManager so this respects the Settings panel's Sound
        // Effects toggle in real time instead of always playing regardless of it.
        AudioManager.PlaySfx(successSound, visitor.transform.position, 0.7f);

        List<SpriteRenderer> spriteRenderers = new List<SpriteRenderer>(visitor.GetComponentsInChildren<SpriteRenderer>());

        Vector3 startPos = visitor.transform.position;
        float t = 0f;

        while (t < serveMoveDuration)
        {
            if (visitor == null) yield break;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / serveMoveDuration);
            visitor.transform.position = Vector3.Lerp(startPos, frontAnchor, p);

            float alpha = 1f - p;
            foreach (SpriteRenderer sr in spriteRenderers)
            {
                if (sr == null) continue;
                Color c = sr.color;
                c.a = alpha;
                sr.color = c;
            }

            yield return null;
        }

        if (visitor != null) Destroy(visitor);
    }

    // Smoothly slides the remaining visitors into their new (index-shifted) evenly-spaced
    // positions, instead of snapping instantly - runs concurrently with the served
    // visitor's own walk-and-fade animation.
    private IEnumerator SlideRemainingVisitorsForward()
    {
        if (visitors.Count == 0) yield break;

        // Reuses the fixed visitorSpacing set once in RepositionVisitorsInstant (not
        // recomputed from the current, shrinking count) so each Serve() shifts every
        // remaining visitor forward by exactly one step, including the one at the back.
        Vector3[] startPositions = new Vector3[visitors.Count];
        Vector3[] targetPositions = new Vector3[visitors.Count];

        for (int i = 0; i < visitors.Count; i++)
        {
            if (visitors[i] == null) continue;
            startPositions[i] = visitors[i].transform.position;
            float x = queueStartX + i * visitorSpacing;
            targetPositions[i] = new Vector3(x, startPositions[i].y, startPositions[i].z);
        }

        float t = 0f;
        while (t < serveMoveDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / serveMoveDuration);
            for (int i = 0; i < visitors.Count; i++)
            {
                if (visitors[i] == null) continue;
                visitors[i].transform.position = Vector3.Lerp(startPositions[i], targetPositions[i], p);
            }
            yield return null;
        }

        for (int i = 0; i < visitors.Count; i++)
        {
            if (visitors[i] == null) continue;
            visitors[i].transform.position = targetPositions[i];
        }
    }

    // Called by VisitorClick when the player taps a visitor that isn't at the front.
    public void NotifyWrongVisitor(GameObject clickedVisitor)
    {
        if (clickedVisitor != null) StartCoroutine(ShakeAndFlash(clickedVisitor));
        ShowToast("Wrong Visitor!");
        AudioManager.PlaySfx(wrongVisitorSound, clickedVisitor != null ? clickedVisitor.transform.position : Vector3.zero);
    }

    private IEnumerator ShakeAndFlash(GameObject visitor)
    {
        SpriteRenderer sr = visitor.GetComponent<SpriteRenderer>();
        Color original = sr != null ? sr.color : Color.white;
        Vector3 basePos = visitor.transform.position;

        if (sr != null) sr.color = Color.Lerp(original, Color.red, 0.75f);

        float t = 0f;
        float magnitude = 0.06f;
        while (t < wrongClickShakeDuration)
        {
            if (visitor == null) yield break;
            t += Time.deltaTime;
            float offset = Mathf.Sin(t * 40f) * magnitude * (1f - t / wrongClickShakeDuration);
            visitor.transform.position = basePos + new Vector3(offset, 0f, 0f);
            yield return null;
        }

        if (visitor == null) yield break;
        visitor.transform.position = basePos;
        if (sr != null) sr.color = original;
    }

    // Waits briefly so the door-unlock/served state reads clearly before the popup
    // (a full-screen UI element) appears on top of it.
    private IEnumerator ShowLevelCompletePanelDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (levelCompletePanel != null)
        {
            AudioManager.PlaySfx(levelCompleteSound, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            StartCoroutine(AnimatePopupReveal());
        }
    }

    private void ShowToast(string message)
    {
        if (toastGroup == null || toastText == null) return;
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ToastRoutine(message));
    }

    private IEnumerator ToastRoutine(string message)
    {
        toastText.text = message;
        toastGroup.SetActive(true);
        yield return new WaitForSeconds(toastDuration);
        toastGroup.SetActive(false);
    }
}
