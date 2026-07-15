using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TMPro;

public class StackManager : MonoBehaviour
{
    public List<GameObject> books;
    public GameObject key;
    public GameObject levelCompleteText;

    // Matches "Book_<number>" exactly - used to auto-discover every book already
    // placed in the Hierarchy instead of instantiating/destroying them at runtime.
    private static readonly Regex BookNamePattern = new Regex(@"^Book_(\d+)$");

    [Header("Bookshelf Population")]
    public float bookSpacing = 1f; // equal to bookSize.y - books stack with zero gap between them

    [Header("Book Appearance")]
    public Vector2 bookSize = new Vector2(1.80f, 1f); // ~35% larger than the original 1.2 x 0.7
    public int bookCornerRadius = 12;
    public Color[] bookPalette = new Color[]
    {
        new Color32(0xD9, 0x38, 0x32, 0xFF), // bright red
        new Color32(0x33, 0xA6, 0x59, 0xFF), // bright green
        new Color32(0xD9, 0xAD, 0x33, 0xFF), // mustard yellow
        new Color32(0x3D, 0x6B, 0xBF, 0xFF), // bright blue
        new Color32(0x8C, 0x59, 0x33, 0xFF), // warm brown
        new Color32(0xE0, 0x7B, 0x39, 0xFF), // orange
        new Color32(0x8E, 0x44, 0xAD, 0xFF), // purple
        new Color32(0x1A, 0xBC, 0x9C, 0xFF), // teal
        new Color32(0xE0, 0x5C, 0x7A, 0xFF), // rose pink
        new Color32(0x5D, 0x6D, 0x7E, 0xFF), // slate blue-gray
    };
    private static readonly string[] SpineTitles = { "STACK", "LIFO", "PUSH", "POP", "TOP", "DATA" };

    [Header("Table Appearance")]
    public Vector2 tableSize = new Vector2(6f, 0.45f);
    public Color tableColor = new Color32(0x8B, 0x5E, 0x3C, 0xFF); // medium brown (library palette)
    public float tableGap = 0f; // zero gap - the bottom book sits flush on the table

    [Header("Feedback")]
    public float wrongClickShakeDuration = 0.35f;
    public float correctClickSlideDuration = 0.35f;
    public float toastDuration = 1.5f;
    public float keyRevealDuration = 0.6f;
    public float keyRevealScale = 1.4f; // the key's authored scene scale (0.5) is tiny and easy to miss behind the popup
    public AudioClip pageTurnSound; // optional - assign a free SFX clip; safely no-ops if left empty
    public AudioClip wrongBookSound; // optional - played on a wrong-book click
    public AudioClip keyCollectSound; // optional - played when the key is revealed
    public AudioClip levelCompleteSound; // optional - played when the completion popup appears

    private TextMeshProUGUI counterText;
    private TextMeshProUGUI toastText;
    private GameObject toastGroup;
    private Coroutine toastRoutine;
    private GameObject levelCompletePanel;
    private Material bookMaterial;
    private SpriteRenderer doorSR;
    private SpriteRenderer doorHandleSR;

    // Level Complete popup entrance-animation targets (set in BuildLevelCompletePopup,
    // used by AnimatePopupReveal).
    private Image popupOverlayImage;
    private CanvasGroup popupCardGroup;
    private CanvasGroup popupTextGroup;
    private CanvasGroup popupButtonGroup;
    private float popupOverlayTargetAlpha;

    // The camera's computed vertical frame (center + half-height), captured by
    // FrameCameraOnStack so LibraryEnvironment can size the wall/floor to always
    // cover the visible area, however many books exist.
    private float frameCenterY;
    private float frameHalfHeight = 5f;

    // Bottom/top book positions, captured once in Start() before any books are removed -
    // used to place the hidden key "where the stack was" once it's revealed (see RevealKey)
    // and to size the camera frame/lamp position around the original stack footprint.
    private Vector3 bottomBookAnchor;
    private Vector3 topBookAnchor;

    void Start()
    {
        if (key != null) key.SetActive(false);
        ApplyKeyAppearance();

        PopulateBooksFromScene();
        EnsureExactlySixBooks();
        RepositionBooksEvenly();
        bottomBookAnchor = books.Count > 0 ? books[0].transform.position : Vector3.zero;
        CaptureBookMaterial();
        ApplyBookAppearance();

        Vector3 tableAnchor = CreateTable();

        // The table's own legs rest on the floor, so its bottom edge is the natural floor
        // baseline for the door too - previously the door was positioned by a hand-tuned
        // offset above the table's CENTER (tableAnchor.y + 1.3), which floated its bottom
        // edge well above the floor instead of resting on it next to the wall.
        float floorY = tableAnchor.y - tableSize.y * 0.5f;
        const float doorHeight = 2.4f; // matches CreateExitDoor's own hardcoded door size

        // z=2 keeps the door in front of the floor/shelves/wall/window (z 5-9) so it isn't
        // occluded by them - it was previously at z=6.4, which put it BEHIND the floor (z=5),
        // hiding most of its height under the floor sprite.
        CreateExitDoor(new Vector3(-3.6f, floorY + doorHeight * 0.5f, 2f));
        FrameCameraOnStack();
        ApplyWarmLighting();

        topBookAnchor = books.Count > 0 ? books[books.Count - 1].transform.position : tableAnchor;
        float aspect = Camera.main != null ? Camera.main.aspect : 1.6f;
        float frameHalfWidth = frameHalfHeight * aspect;
        // floorTopY pins the drawn floor sprite's top edge to the same floorY the door now
        // stands on, instead of the older camera-frame-derived approximation, so the door
        // and floor graphic align exactly rather than the door appearing to float above it.
        LibraryEnvironment.Build(tableAnchor, topBookAnchor, frameCenterY, frameHalfHeight, frameHalfWidth, bookMaterial, floorTopY: floorY);
        FlattenDecorativeGlow();

        BuildUI();
        UpdateBookColliders();
        UpdateCounter();

        Debug.Log("Stack ready! Total books: " + books.Count);
    }

    // Finds every GameObject named "Book_<number>" anywhere in the scene (active or
    // not), sorts them numerically (Book_1 = bottom of the stack, highest number =
    // top), and uses that as the books list. This never creates or destroys a book -
    // the Hierarchy itself is the only source of truth for what books exist, so
    // stopping Play mode can never lose any of them. Add/remove/rename a "Book_N"
    // GameObject in the Hierarchy and it's picked up automatically next time you Play.
    void PopulateBooksFromScene()
    {
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        books = allObjects
            .Where(go => BookNamePattern.IsMatch(go.name))
            .OrderBy(go => ExtractBookNumber(go.name))
            .ToList();
    }

    private static int ExtractBookNumber(string objectName)
    {
        Match match = BookNamePattern.Match(objectName);
        return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
    }

    // The stack must always have exactly 6 books. If the Hierarchy is missing some
    // (e.g. only Book_1..Book_4 exist), this duplicates the last existing book as a
    // template and adds correctly-numbered clones until there are 6. It never removes
    // anything, and it's a no-op once 6 or more already exist - so a scene that already
    // has all 6 authored is completely unaffected.
    void EnsureExactlySixBooks()
    {
        const int targetCount = 6;

        if (books.Count == 0)
        {
            Debug.LogWarning("StackManager: no Book_N GameObjects found in the scene - cannot build a stack from nothing.");
            return;
        }

        if (books.Count >= targetCount) return;

        GameObject template = books[books.Count - 1];
        int nextNumber = ExtractBookNumber(template.name) + 1;

        while (books.Count < targetCount)
        {
            GameObject clone = Instantiate(template, template.transform.parent);
            clone.name = "Book_" + nextNumber;
            books.Add(clone);
            Debug.LogWarning("StackManager: " + clone.name + " was missing from the scene - " +
                "auto-created by duplicating " + template.name + ". Add it permanently in the Hierarchy to skip this.");
            nextNumber++;
        }
    }

    // The original scene-authored books use whatever spacing was hand-placed in the
    // editor, which can drift out of sync with bookSpacing/bookSize (visible as a gap
    // or overlap at the seam between authored and newly-cloned books). Repositioning
    // every book from a single formula guarantees a neat, gap-free stack regardless.
    void RepositionBooksEvenly()
    {
        if (books.Count == 0) return;
        float x = books[0].transform.position.x;
        float tableTopY = -1.5f;
        for (int i = 0; i < books.Count; i++)
        {
            if (books[i] == null) continue;
            float centerY = tableTopY + (bookSize.y * 0.5f) + (i * bookSize.y);
            books[i].transform.position = new Vector3(x, centerY, -i * 0.1f);
        }
    }

    void CaptureBookMaterial()
    {
        if (books.Count == 0) return;
        SpriteRenderer sr = books[0].GetComponent<SpriteRenderer>();
        if (sr != null) bookMaterial = sr.sharedMaterial;
    }

    // Gives the key a recognizable key-shaped silhouette instead of the plain square
    // sprite it was originally authored with in the scene.
    void ApplyKeyAppearance()
    {
        if (key == null) return;
        SpriteRenderer keySR = key.GetComponent<SpriteRenderer>();
        if (keySR != null) keySR.sprite = GameUIHelper.GetKeySprite();
    }

    // Makes each book look like a real library book: rounded cover, a warm color per
    // book, a glossy highlight, a spine stripe with a small title, a drop shadow, a
    // dark outline, and a number label (bottom = 1) so stack order stays readable.
    // Also defensively adds any component a manually-created "Book_N" GameObject is
    // missing (SpriteRenderer/BoxCollider2D/BookClick), so a bare empty GameObject
    // named e.g. "Book_5" becomes a fully working, clickable book with zero other
    // setup required in the Inspector.
    void ApplyBookAppearance()
    {
        Sprite bookSprite = GameUIHelper.GetRoundedSprite(bookCornerRadius);

        for (int i = 0; i < books.Count; i++)
        {
            GameObject book = books[i];
            if (book == null) continue;

            book.transform.localScale = Vector3.one;

            SpriteRenderer sr = book.GetComponent<SpriteRenderer>();
            if (sr == null) sr = book.AddComponent<SpriteRenderer>();

            Color bookColor = bookPalette.Length > 0 ? bookPalette[i % bookPalette.Length] : Color.white;
            sr.sprite = bookSprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = bookSize;
            sr.color = bookColor;

            // The visual size no longer comes from transform scale, so the collider
            // must be told the new footprint explicitly to keep clicks accurate.
            BoxCollider2D col = book.GetComponent<BoxCollider2D>();
            if (col == null) col = book.AddComponent<BoxCollider2D>();
            col.size = bookSize;

            if (book.GetComponent<BookClick>() == null) book.AddComponent<BookClick>();

            AddOutline(book, sr);
            AddShadow(book, sr);
            AddSpine(book, sr, bookColor);
            AddSpineTitle(book, SpineTitles[i % SpineTitles.Length]);
            AddPages(book, sr);
            AddHighlight(book);
            AddTitlePlate(book, sr, bookColor);
            AddNumberLabel(book, i + 1, bookColor);
        }
    }

    // Returns the existing named child if one is already there, otherwise creates it.
    // Without this, re-running ApplyBookAppearance for the same book (e.g. if Start()
    // were ever invoked more than once) would stack up duplicate Outline/Shadow/Spine/
    // SpineTitle/Highlight/Label GameObjects instead of just updating the existing ones.
    private static GameObject GetOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    void AddOutline(GameObject book, SpriteRenderer bookSR)
    {
        GameObject outline = GetOrCreateChild(book.transform, "Outline");
        outline.transform.localPosition = new Vector3(0f, 0f, 0.05f); // farther back -> peeks out behind the book face

        SpriteRenderer sr = outline.GetComponent<SpriteRenderer>();
        if (sr == null) sr = outline.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedSprite(bookCornerRadius);
        sr.drawMode = SpriteDrawMode.Sliced;
        // No vertical padding: with books stacked at zero gap (bookSpacing == bookSize.y),
        // any vertical outline padding here would poke 0.05 past each book's own top/
        // bottom edge into the neighboring book's territory - doubling up with that
        // book's own outline into a visible dark seam at every book-to-book boundary,
        // even though the books themselves are perfectly flush. Horizontal padding is
        // kept since there's no horizontal neighbor to create that overlap.
        sr.size = bookSize + new Vector2(0.12f, 0f);
        sr.color = new Color(0.13f, 0.08f, 0.05f, 1f);
        if (bookSR != null) sr.sharedMaterial = bookSR.sharedMaterial;
    }

    void AddShadow(GameObject book, SpriteRenderer bookSR)
    {
        GameObject shadow = GetOrCreateChild(book.transform, "Shadow");
        shadow.transform.localPosition = new Vector3(0.07f, -0.07f, 0.08f); // farthest back, offset down-right

        SpriteRenderer sr = shadow.GetComponent<SpriteRenderer>();
        if (sr == null) sr = shadow.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedSprite(bookCornerRadius);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = bookSize;
        sr.color = new Color(0f, 0f, 0f, 0.35f);
        if (bookSR != null) sr.sharedMaterial = bookSR.sharedMaterial;
    }

    void AddSpine(GameObject book, SpriteRenderer bookSR, Color bookColor)
    {
        GameObject spine = GetOrCreateChild(book.transform, "Spine");
        spine.transform.localPosition = new Vector3(-bookSize.x * 0.5f, 0f, -0.01f);

        SpriteRenderer sr = spine.GetComponent<SpriteRenderer>();
        if (sr == null) sr = spine.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedWoodSprite(0);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(bookSize.x * 0.12f, bookSize.y);
        if (bookSR != null) sr.sharedMaterial = bookSR.sharedMaterial;

        Color darker = bookColor * 0.55f;
        darker.a = 1f;
        sr.color = darker;
    }

    // A small rotated title running along the spine's length, like a real book.
    void AddSpineTitle(GameObject book, string title)
    {
        GameObject titleGO = GetOrCreateChild(book.transform, "SpineTitle");
        titleGO.transform.localPosition = new Vector3(-bookSize.x * 0.5f, 0f, -0.015f);
        titleGO.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        TextMeshPro tmp = titleGO.GetComponent<TextMeshPro>();
        if (tmp == null) tmp = titleGO.AddComponent<TextMeshPro>();
        tmp.text = title;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.02f;
        tmp.fontSizeMax = 1f;
        tmp.overflowMode = TextOverflowModes.Truncate;

        // Authored pre-rotation: local X maps to the spine's height, local Y to its thickness.
        tmp.rectTransform.sizeDelta = new Vector2(bookSize.y * 0.85f, bookSize.x * 0.10f);
    }

    // A thin cream strip on the edge opposite the spine, like the visible edge of a
    // book's paper pages - reads much more like a real closed book than a flat card.
    void AddPages(GameObject book, SpriteRenderer bookSR)
    {
        GameObject pages = GetOrCreateChild(book.transform, "Pages");
        pages.transform.localPosition = new Vector3(bookSize.x * 0.5f, 0f, -0.01f);

        SpriteRenderer sr = pages.GetComponent<SpriteRenderer>();
        if (sr == null) sr = pages.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedSprite(0);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(bookSize.x * 0.08f, bookSize.y * 0.9f);
        sr.color = new Color(0.93f, 0.89f, 0.78f);
        if (bookSR != null) sr.sharedMaterial = bookSR.sharedMaterial;
    }

    // A soft glossy sheen near the top of the cover for a cartoon/polished look.
    void AddHighlight(GameObject book)
    {
        GameObject highlight = GetOrCreateChild(book.transform, "Highlight");
        highlight.transform.localPosition = new Vector3(0.05f, bookSize.y * 0.28f, -0.005f);

        SpriteRenderer sr = highlight.GetComponent<SpriteRenderer>();
        if (sr == null) sr = highlight.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedSprite(bookCornerRadius);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(bookSize.x * 0.7f, bookSize.y * 0.18f);
        sr.color = new Color(1f, 1f, 1f, 0.22f);
    }

    // A subtle darker "nameplate" panel behind the number label, like a framed title
    // block printed on a real book cover, instead of the number floating on bare cover.
    void AddTitlePlate(GameObject book, SpriteRenderer bookSR, Color bookColor)
    {
        GameObject plate = GetOrCreateChild(book.transform, "TitlePlate");
        plate.transform.localPosition = new Vector3(0f, 0f, -0.012f);

        SpriteRenderer sr = plate.GetComponent<SpriteRenderer>();
        if (sr == null) sr = plate.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedSprite(4);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(bookSize.x * 0.55f, bookSize.y * 0.42f);
        if (bookSR != null) sr.sharedMaterial = bookSR.sharedMaterial;

        Color darker = bookColor * 0.65f;
        darker.a = 0.9f;
        sr.color = darker;
    }

    void AddNumberLabel(GameObject book, int number, Color bookColor)
    {
        GameObject labelGO = GetOrCreateChild(book.transform, "Label");
        labelGO.transform.localPosition = new Vector3(0f, 0f, -0.02f);

        TextMeshPro label = labelGO.GetComponent<TextMeshPro>();
        if (label == null) label = labelGO.AddComponent<TextMeshPro>();
        label.text = "Book " + number;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;

        // Light book covers (tan/cream) need dark text to stay readable; dark covers need white.
        float luminance = 0.299f * bookColor.r + 0.587f * bookColor.g + 0.114f * bookColor.b;
        label.color = luminance > 0.6f ? (Color)GameUIHelper.DarkWood : Color.white;

        // World-space TMP font size is in world units, not pixels - a fixed size would
        // either overflow tiny books or look microscopic on bigger ones. Auto-size instead
        // so the label always shrinks/grows to fit inside the book's footprint.
        label.enableAutoSizing = true;
        label.fontSizeMin = 0.05f;
        label.fontSizeMax = 2f;
        label.overflowMode = TextOverflowModes.Truncate;
        label.rectTransform.sizeDelta = new Vector2(bookSize.x * 0.8f, bookSize.y * 0.7f);
    }

    // Returns the world position used as the anchor for the table + environment dressing.
    Vector3 CreateTable()
    {
        if (books.Count == 0) return Vector3.zero;

        GameObject template = books[0];
        Vector3 basePos = template.transform.position;

        // Matches RepositionBooksEvenly()'s tableTopY (-1.5f) exactly: the table's top
        // edge lands at -1.5, the same fixed Y the bottom book's bottom edge is placed
        // at, so they're flush with zero gap by construction.
        float tableY = -1.5f - tableSize.y * 0.5f;
        Vector3 tablePos = new Vector3(basePos.x, tableY, 1f);

        GameObject table = new GameObject("LibraryTable");
        table.transform.position = tablePos;

        SpriteRenderer sr = table.AddComponent<SpriteRenderer>();
        sr.sprite = GameUIHelper.GetRoundedWoodSprite(6);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = tableSize;
        sr.color = tableColor;
        if (bookMaterial != null) sr.sharedMaterial = bookMaterial;

        return tablePos;
    }

    // Purely decorative exit door: starts "locked" (dark/dull) and brightens once the
    // key is found. No scene-loading or navigation logic is attached to it.
    void CreateExitDoor(Vector3 position)
    {
        GameObject door = new GameObject("ExitDoor");
        door.transform.position = position;

        doorSR = door.AddComponent<SpriteRenderer>();
        doorSR.sprite = GameUIHelper.GetRoundedWoodSprite(6);
        doorSR.drawMode = SpriteDrawMode.Sliced;
        doorSR.size = new Vector2(1.2f, 2.4f);
        doorSR.color = new Color(0.16f, 0.11f, 0.08f);
        if (bookMaterial != null) doorSR.sharedMaterial = bookMaterial;

        GameObject handle = new GameObject("DoorHandle");
        handle.transform.SetParent(door.transform, false);
        handle.transform.localPosition = new Vector3(0.4f, 0f, -0.02f);

        doorHandleSR = handle.AddComponent<SpriteRenderer>();
        doorHandleSR.sprite = GameUIHelper.GetBlobSprite();
        doorHandleSR.drawMode = SpriteDrawMode.Sliced;
        doorHandleSR.size = new Vector2(0.14f, 0.14f);
        doorHandleSR.color = new Color(0.32f, 0.28f, 0.24f);
        if (bookMaterial != null) doorHandleSR.sharedMaterial = bookMaterial;
    }

    void UnlockDoor()
    {
        if (doorSR != null) doorSR.color = new Color(0.48f, 0.32f, 0.17f);
        if (doorHandleSR != null) doorHandleSR.color = GameUIHelper.GoldColor;
    }

    // Auto-frames the camera around the current stack + table so the composition stays
    // balanced (no large empty margins) regardless of how many books are in the scene.
    void FrameCameraOnStack()
    {
        if (books.Count == 0) return;

        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return;

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (GameObject book in books)
        {
            if (book == null) continue;
            float y = book.transform.position.y;
            minY = Mathf.Min(minY, y);
            maxY = Mathf.Max(maxY, y);
        }

        float halfBookHeight = bookSize.y * 0.5f;

        // Room for the FULL table under the bottom book. This must mirror CreateTable's
        // real placement math (halfBookHeight + tableGap + tableSize.y) - it previously
        // used bookSpacing as a placeholder, which was unrelated to the table's actual
        // footprint and cut off roughly the bottom third of the table on-screen.
        minY -= halfBookHeight + tableGap + tableSize.y + 0.15f;
        float topBookEdge = maxY + halfBookHeight; // top edge of the top book, before UI headroom

        // The header banner + instruction pill together occupy roughly the top 12% of
        // the screen (70px + 44px out of the UI's 600px reference height); padded to 16%
        // for safety margin across aspect ratios. Solving for maxY algebraically (instead
        // of a flat guessed offset) guarantees the top book can never be covered by the
        // banners, regardless of how tall the stack is.
        const float uiTopReservedFraction = 0.16f;
        maxY = minY + (topBookEdge - minY) / (1f - uiTopReservedFraction);

        frameCenterY = (minY + maxY) * 0.5f;
        frameHalfHeight = Mathf.Max((maxY - minY) * 0.5f, 1f);

        Vector3 pos = cam.transform.position;
        cam.transform.position = new Vector3(pos.x, frameCenterY, pos.z);
        cam.orthographicSize = frameHalfHeight;
        cam.backgroundColor = BrightLibraryBackground;
    }

    // Matches the new Light Beige wall color (LibraryEnvironment.LightBeigeWall) so any
    // sliver of camera clear color peeking past the (generously oversized) wall sprite
    // blends in seamlessly instead of showing the old dark brown.
    private static readonly Color32 BrightLibraryBackground = new Color32(0xE8, 0xDD, 0xC8, 0xFF);

    void ApplyWarmLighting()
    {
        Light2D light = Object.FindFirstObjectByType<Light2D>();
        if (light == null) return;

        light.color = new Color(1f, 0.92f, 0.8f); // matches Level 2's warm tone
        light.intensity = 1.6f;
    }

    // LibraryEnvironment's hanging lamp/window carry a Point Light2D and painted "glow"
    // sprites (independent of any real light) tuned as a cozy accent - fine at Level 2's
    // wider scale, but Level 1's much more compact stack framing makes that same radius/
    // alpha read as a bright white spotlight/hotspot on the right side instead of a subtle
    // touch. Rather than editing the shared LibraryEnvironment code (which Level 2 also
    // uses and isn't reported as having this problem), this dims/hides just those elements
    // here, after they're created, so Level 1's brightness comes from the even Global
    // Light 2D (ApplyWarmLighting) instead of a localized glow.
    void FlattenDecorativeGlow()
    {
        Light2D[] lights = Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        foreach (Light2D light in lights)
        {
            if (light.lightType != Light2D.LightType.Point) continue;
            light.intensity = 0.12f; // extremely subtle warm accent, not a visible hotspot
            light.pointLightOuterRadius = Mathf.Min(light.pointLightOuterRadius, 1f);
        }

        GameObject envRoot = GameObject.Find("LibraryEnvironment");
        if (envRoot == null) return;

        Transform lampGlow = envRoot.transform.Find("HangingLamp/Glow");
        if (lampGlow != null)
        {
            SpriteRenderer sr = lampGlow.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                Color c = sr.color;
                c.a = 0.08f; // near-invisible, decorative only - matches the lamp's own dimmed light
                sr.color = c;
            }
        }

        Transform sunbeam = envRoot.transform.Find("Window/Sunbeam");
        if (sunbeam != null)
        {
            SpriteRenderer sr = sunbeam.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false; // the window itself should not emit any glow
        }
    }

    void BuildUI()
    {
        Canvas canvas = GameUIHelper.EnsureCanvas();

        GameUIHelper.CreateWoodPanel(canvas.transform, "HeaderBanner", GameUIHelper.DarkWood,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(400, 70));

        GameUIHelper.CreateText(canvas.transform, "HeaderText", "LEVEL 1 – STACK (LIFO)", 22,
            GameUIHelper.GoldColor, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(400, 44));

        BuildCounter(canvas.transform);
        BuildToast(canvas.transform);

        BuildLevelCompletePopup(canvas.transform);
    }

    // A bespoke Level 1 popup, built directly here (rather than via the shared
    // GameUIHelper.BuildLevelCompletePanel used before, or the generic BuildOverlayCard
    // used as-is) so its specific look - cream parchment card, thin gold border,
    // staggered fade/scale entrance - doesn't have to be threaded through as more
    // one-off optional parameters on a helper Level 2's own popup also relies on.
    // Level 2's popup code is completely untouched.
    // Card is 300x230 (~25% narrower, ~32% shorter than the previous 400x340) - a
    // lightweight notification rather than a large dialog - with the icon graphic
    // dropped entirely (the layout is just title/subtitle/button/button) to fit that
    // compact size with comfortable ~24-28px padding instead of cramming content in.
    void BuildLevelCompletePopup(Transform canvasTransform)
    {
        Color creamCard = new Color32(0xF7, 0xF1, 0xE3, 0xFF);
        Color goldBorder = new Color32(0xD4, 0xAF, 0x37, 0xFF);
        Color darkBrownText = new Color32(0x4A, 0x2F, 0x1B, 0xFF);

        GameObject overlay = GameUIHelper.BuildOverlayCard(canvasTransform, "LevelCompleteOverlay",
            new Vector2(300, 230), out Transform card, creamCard, useWoodTexture: false, borderColorOverride: goldBorder);

        // Dim enough to make the popup stand out, light enough that the library scene
        // behind it stays clearly visible.
        popupOverlayImage = overlay.GetComponent<Image>();
        popupOverlayTargetAlpha = 0.35f;
        Color overlayColor = popupOverlayImage.color;
        overlayColor.a = popupOverlayTargetAlpha;
        popupOverlayImage.color = overlayColor;

        Shadow shadow = card.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(0, -6);

        popupCardGroup = card.gameObject.AddComponent<CanvasGroup>();

        // Title + subtitle, grouped so both fade in together as one "step". The group
        // itself stretches to fill the card so child positions below are simple
        // distances from the card's own top edge, rather than nested-anchor math.
        GameObject textGroupGO = new GameObject("TextGroup", typeof(RectTransform));
        textGroupGO.transform.SetParent(card, false);
        RectTransform textGroupRect = textGroupGO.GetComponent<RectTransform>();
        textGroupRect.anchorMin = Vector2.zero;
        textGroupRect.anchorMax = Vector2.one;
        textGroupRect.sizeDelta = Vector2.zero;
        textGroupRect.anchoredPosition = Vector2.zero;
        popupTextGroup = textGroupGO.AddComponent<CanvasGroup>();

        // Title: 24px top padding, 32 tall. Plain text, no emoji - the project's TMP font
        // doesn't have a trophy glyph, so the old "\U0001F3C6" showed as a missing-glyph
        // box next to the title instead of an actual trophy icon.
        GameUIHelper.CreateText(textGroupGO.transform, "Title", "Level Complete!", 22,
            darkBrownText, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(260, 32));

        if (levelCompleteText != null)
        {
            // 8px gap below the title (which ends at 24+32=56), so the subtitle's
            // center sits at 64 + 10 (half of its own 20px height) = 74 below the top.
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

        // Button row: Next Level (gold) above Replay (medium brown), equal size,
        // centered, grouped so both fade in together as the final "step". Same
        // fill-the-card trick as the text group above.
        GameObject buttonGroupGO = new GameObject("ButtonGroup", typeof(RectTransform));
        buttonGroupGO.transform.SetParent(card, false);
        RectTransform buttonGroupRect = buttonGroupGO.GetComponent<RectTransform>();
        buttonGroupRect.anchorMin = Vector2.zero;
        buttonGroupRect.anchorMax = Vector2.one;
        buttonGroupRect.sizeDelta = Vector2.zero;
        buttonGroupRect.anchoredPosition = Vector2.zero;
        popupButtonGroup = buttonGroupGO.AddComponent<CanvasGroup>();

        // Subtitle ends at 64+20=84; 16px gap -> button 1 starts at 100, 40 tall ->
        // ends at 140; 10px gap -> button 2 starts at 150, 40 tall -> ends at 190;
        // leaves 230-190=40px of bottom padding. Narrower (200 vs 240) so they don't
        // stretch too wide across the card.
        GameUIHelper.CreateFlatButton(buttonGroupGO.transform, "NextLevelButton", "Next Level",
            goldBorder, darkBrownText,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(200, 40),
            () => StartCoroutine(UseKeyAndOpenDoor()));

        GameUIHelper.CreateFlatButton(buttonGroupGO.transform, "ReplayButton", "Replay",
            new Color32(0x8B, 0x5E, 0x3C, 0xFF), Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(200, 40),
            () => SceneManager.LoadScene(SceneManager.GetActiveScene().name));

        overlay.SetActive(false);
        levelCompletePanel = overlay;
    }

    // Plays the popup's entrance animation: the card fades/scales in (90% -> 100%) over
    // ~0.3s alongside the overlay dimming in, then the title/subtitle fade in, then
    // finally the buttons fade in.
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

    // Reuses UnlockDoor()'s existing "door turns wood brown, handle turns gold" visual
    // instead of duplicating a new color, then loads Level 2 after a short pause so the
    // door-opening feedback is visible first.
    private IEnumerator UseKeyAndOpenDoor()
    {
        UnlockDoor();
        yield return new WaitForSeconds(1f);
        SceneManager.LoadScene("Level2_Queue");
    }

    // A compact top-right card: an icon+label line and a large number below it, all in
    // one wood panel group so position/size only needs to be set in a single place -
    // this is what keeps it from clipping off-screen on different resolutions.
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

        GameUIHelper.CreateText(group.transform, "CounterLabel", "Books Remaining", 13,
            GameUIHelper.Cream, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(100, 20));

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
        groupRect.sizeDelta = new Vector2(380, 42); // smaller than the previous 560x56

        // Dark brown, almost fully opaque - built via the Color32->Color implicit
        // conversion (which correctly normalizes 0-255 byte channels to 0-1 floats).
        // The previous version read GameUIHelper.DarkWood.r/.g/.b directly as Color32
        // bytes (e.g. 62) and fed them straight into new Color(...)'s 0-1 float channels -
        // ~62x overbright, which clips to white. That's why the toast looked washed
        // out/white instead of dark.
        Color toastPanelColor = new Color32(0x3D, 0x1F, 0x0A, 0xFF);
        toastPanelColor.a = 0.95f;
        GameUIHelper.CreateWoodPanel(toastGroup.transform, "ToastPanel", toastPanelColor,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 16);

        // Bright gold, bold, for contrast against the now much darker panel.
        Color toastTextColor = new Color32(0xFF, 0xD7, 0x00, 0xFF);
        toastText = GameUIHelper.CreateText(toastGroup.transform, "ToastText", "", 15,
            toastTextColor, FontStyles.Bold, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        toastGroup.SetActive(false);
    }

    void UpdateCounter()
    {
        if (counterText != null)
            counterText.text = books.Count.ToString();
    }

    // Every book keeps its collider enabled so BookClick.OnMouseDown actually fires for
    // ALL of them, not just the top one - a disabled collider means Unity never calls
    // OnMouseDown at all, which was silently swallowing clicks on non-top books before
    // they could ever reach the "wrong book" check. "Only the top book can be removed"
    // is enforced by the topBook == this.gameObject comparison in BookClick, not by
    // disabling clicks on the others.
    void UpdateBookColliders()
    {
        foreach (GameObject book in books)
        {
            if (book == null) continue;
            BoxCollider2D col = book.GetComponent<BoxCollider2D>();
            if (col != null) col.enabled = true;
        }
    }

    public GameObject GetTopBook()
    {
        if (books.Count == 0) return null;
        return books[books.Count - 1];
    }

    public void RemoveTopBook()
    {
        if (books.Count == 0) return;

        GameObject top = books[books.Count - 1];
        books.RemoveAt(books.Count - 1);

        Debug.Log("Book removed! Remaining: " + books.Count);
        UpdateCounter();
        StartCoroutine(AnimateBookRemoval(top));

        if (books.Count == 0)
        {
            StartCoroutine(RevealKey());
            UnlockDoor();
            // The popup is a full-screen ScreenSpaceOverlay Canvas element, which always
            // draws on top of world-space sprites regardless of Z - showing it immediately
            // would completely cover the key's fade-in. Delaying it lets the player see the
            // key clearly first.
            StartCoroutine(ShowLevelCompletePanelDelayed(1f));
            Debug.Log("STACK EMPTY! Level Complete!");
        }
        else
        {
            UpdateBookColliders();
        }
    }

    // Purely cosmetic: the book is already removed from game state by the time this runs,
    // so it only animates the leftover visual object before destroying it.
    private IEnumerator AnimateBookRemoval(GameObject book)
    {
        if (book == null) yield break;

        Collider2D col = book.GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        SpriteRenderer mainSR = book.GetComponent<SpriteRenderer>();
        SpawnRemovalParticles(book.transform.position, mainSR != null ? mainSR.color : Color.white);

        // Routed through AudioManager so this respects the Settings panel's Sound
        // Effects toggle in real time instead of always playing regardless of it.
        AudioManager.PlaySfx(pageTurnSound, book.transform.position, 0.7f);

        List<SpriteRenderer> spriteRenderers = new List<SpriteRenderer>(book.GetComponentsInChildren<SpriteRenderer>());
        List<TMP_Text> textRenderers = new List<TMP_Text>(book.GetComponentsInChildren<TMP_Text>());

        Vector3 startPos = book.transform.position;
        Vector3 endPos = startPos + new Vector3(0f, 1.2f, 0f);
        float t = 0f;

        while (t < correctClickSlideDuration)
        {
            if (book == null) yield break;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / correctClickSlideDuration);
            book.transform.position = Vector3.Lerp(startPos, endPos, p);

            float alpha = 1f - p;
            foreach (SpriteRenderer sr in spriteRenderers)
            {
                if (sr == null) continue;
                Color c = sr.color;
                c.a = alpha;
                sr.color = c;
            }
            foreach (TMP_Text text in textRenderers)
            {
                if (text == null) continue;
                Color c = text.color;
                c.a = alpha;
                text.color = c;
            }

            yield return null;
        }

        if (book != null) Destroy(book);
    }

    private void SpawnRemovalParticles(Vector3 position, Color color)
    {
        GameObject psGO = new GameObject("RemovalBurst");
        psGO.transform.position = position;

        ParticleSystem ps = psGO.AddComponent<ParticleSystem>();
        // AddComponent<ParticleSystem> starts playing immediately (playOnAwake defaults to
        // true), and ParticleSystem.main.duration can't be changed while playing - stopping
        // it first (and disabling playOnAwake) avoids the "Setting the duration while system
        // is still playing" console error, then Play() is called explicitly once configured.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.5f;
        main.startLifetime = 0.5f;
        main.startSpeed = 2f;
        main.startSize = 0.1f;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 14) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.05f;

        ParticleSystemRenderer psRenderer = ps.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null) psRenderer.material = new Material(shader);

        ps.Play();
        Destroy(psGO, 1.2f);
    }

    // Called by BookClick when the player taps a book that isn't on top.
    public void NotifyWrongBook(GameObject clickedBook)
    {
        if (clickedBook != null) StartCoroutine(ShakeAndFlash(clickedBook));
        ShowToast("Wrong Book!");
        AudioManager.PlaySfx(wrongBookSound, clickedBook != null ? clickedBook.transform.position : Vector3.zero);
    }

    private IEnumerator ShakeAndFlash(GameObject book)
    {
        SpriteRenderer sr = book.GetComponent<SpriteRenderer>();
        Color original = sr != null ? sr.color : Color.white;
        Vector3 basePos = book.transform.position;

        if (sr != null) sr.color = Color.Lerp(original, Color.red, 0.75f);

        float t = 0f;
        float magnitude = 0.06f;
        while (t < wrongClickShakeDuration)
        {
            if (book == null) yield break;
            t += Time.deltaTime;
            float offset = Mathf.Sin(t * 40f) * magnitude * (1f - t / wrongClickShakeDuration);
            book.transform.position = basePos + new Vector3(offset, 0f, 0f);
            yield return null;
        }

        if (book == null) yield break;
        book.transform.position = basePos;
        if (sr != null) sr.color = original;
    }

    // Waits for the key reveal to be clearly visible before showing the popup, since the
    // popup is a full-screen UI overlay that would otherwise cover the key instantly.
    private IEnumerator ShowLevelCompletePanelDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (levelCompletePanel != null)
        {
            AudioManager.PlaySfx(levelCompleteSound, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            StartCoroutine(AnimatePopupReveal());
        }
    }

    // Reveals the hidden key with a soft pop (fade + scale) plus a lingering golden glow.
    private IEnumerator RevealKey()
    {
        if (key == null) yield break;

        AudioManager.PlaySfx(keyCollectSound, key.transform.position);

        // The key's scene position was authored to match the original bottom book's spot,
        // before RepositionBooksEvenly/tighter spacing existed - it no longer lines up with
        // where the stack actually ends up. Placing it at the midpoint of bottomBookAnchor/
        // topBookAnchor (both captured in Start(), before any books were removed) puts it
        // exactly where the stack was, well within the camera frame that was sized around
        // those same two points. Z is closer to the camera than the table/environment so it
        // always renders on top of them, never hidden behind anything.
        Vector3 stackCenter = (bottomBookAnchor + topBookAnchor) * 0.5f;
        key.transform.position = new Vector3(stackCenter.x, stackCenter.y, -1f);

        key.SetActive(true);
        SpriteRenderer keySR = key.GetComponent<SpriteRenderer>();
        // The key's authored scene scale (0.5) renders small enough to get lost behind the
        // popup card - reveal it at keyRevealScale instead so it's clearly visible.
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
