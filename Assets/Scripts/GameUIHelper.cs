using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

// Shared helpers for building the in-game HUD (header/instructions/counters/panels)
// and procedural world-space sprites (rounded rects, wood texture, soft glow) at
// runtime so level scripts don't need extra manual scene wiring or imported art.
public static class GameUIHelper
{
    public static readonly Color32 NavyBackground = new Color32(0x0F, 0x34, 0x60, 0xFF);   // Level 1 - Stack (legacy)
    public static readonly Color32 TealBackground = new Color32(0x11, 0x4B, 0x5F, 0xFF);   // Level 2 - Queue
    public static readonly Color32 PurpleBackground = new Color32(0x3A, 0x10, 0x78, 0xFF); // Level 3 - Quiz
    public static readonly Color32 PanelColor = new Color32(0x16, 0x21, 0x3E, 0xF0);
    public static readonly Color32 OverlayColor = new Color32(0x00, 0x00, 0x00, 0xA0);
    public static readonly Color32 GoldColor = new Color32(0xFF, 0xD4, 0x6A, 0xFF);
    public static readonly Color32 TextColor = new Color32(0xE9, 0xF1, 0xFA, 0xFF);

    // Warm library palette (used by the Level 1 redesign).
    public static readonly Color32 LibraryBackground = new Color32(0x24, 0x1C, 0x15, 0xFF);
    public static readonly Color32 DarkWood = new Color32(0x3E, 0x27, 0x23, 0xFF);
    public static readonly Color32 WoodBrown = new Color32(0x6D, 0x4C, 0x33, 0xFF);
    public static readonly Color32 Cream = new Color32(0xED, 0xE0, 0xC8, 0xFF);

    private static readonly Dictionary<int, Sprite> roundedSprites = new Dictionary<int, Sprite>();
    private static readonly Dictionary<int, Sprite> roundedWoodSprites = new Dictionary<int, Sprite>();
    private static Sprite glowSprite;
    private static Sprite blobSprite;
    private static Sprite keySprite;

    // Returns the existing named child if one is already there, otherwise creates it.
    // Shared by level managers that build decorative/visual child GameObjects at runtime
    // (e.g. a body part or UI piece) so re-running that setup never stacks up duplicates.
    public static GameObject GetOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    public static Canvas EnsureCanvas()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        // Balanced width/height matching avoids edge elements (like a top-right badge)
        // clipping off-screen on aspect ratios far from the reference resolution.
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800, 600);
            scaler.matchWidthOrHeight = 0.5f;
        }

        return canvas;
    }

    public static TextMeshProUGUI CreateText(Transform parent, string name, string content, int fontSize,
        Color color, FontStyles style, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;

        return text;
    }

    public static Image CreatePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        Image image = go.AddComponent<Image>();
        image.color = color;

        return image;
    }

    // Same as CreatePanel but with softly rounded corners (procedurally generated
    // 9-sliced sprite, so no external art asset is required).
    public static Image CreateRoundedPanel(Transform parent, string name, Color color, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, int cornerRadius = 18)
    {
        Image image = CreatePanel(parent, name, color, anchorMin, anchorMax, anchoredPos, sizeDelta);
        image.sprite = GetRoundedSprite(cornerRadius);
        image.type = Image.Type.Sliced;
        return image;
    }

    // Same as CreateRoundedPanel but backed by a wood-grain-textured sprite instead
    // of a flat color, for "wooden banner"/"wooden panel" style UI.
    public static Image CreateWoodPanel(Transform parent, string name, Color tint, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, int cornerRadius = 18)
    {
        Image image = CreatePanel(parent, name, tint, anchorMin, anchorMax, anchoredPos, sizeDelta);
        image.sprite = GetRoundedWoodSprite(cornerRadius);
        image.type = Image.Type.Sliced;
        return image;
    }

    // A clickable wood-panel button (used by the Main Menu/Level Select screens) - reuses
    // CreateWoodPanel for the visual, adds a Button with Unity's own default ColorTint
    // transition (the same scheme Level 3's Stack/Queue buttons already use, so hover/
    // press feedback is automatic and consistent without any extra config here) and a
    // centered label.
    public static Button CreateWoodButton(Transform parent, string name, string label, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, UnityAction onClick, int cornerRadius = 16)
    {
        Image image = CreateWoodPanel(parent, name, WoodBrown, anchorMin, anchorMax, anchoredPos, sizeDelta, cornerRadius);

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        // ButtonHoverEffect owns hover feedback (scale + brighten) instead of the default
        // ColorTint transition, so the two don't fight over the same Image color.
        button.transition = Selectable.Transition.None;
        image.gameObject.AddComponent<ButtonHoverEffect>();
        button.onClick.AddListener(AudioManager.PlayButtonClick);
        if (onClick != null) button.onClick.AddListener(onClick);

        CreateText(image.transform, "Label", label, 18, Cream, FontStyles.Bold,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        return button;
    }

    // A clickable flat-color rounded button (no wood-grain texture) - for cases (e.g.
    // Level 1's key/door and "play again" popup buttons) that need a specific solid
    // background/text color rather than the wood theme. Same hover-scale/brighten
    // behavior as CreateWoodButton, via the same ButtonHoverEffect component.
    public static Button CreateFlatButton(Transform parent, string name, string label, Color bgColor, Color textColor,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, UnityAction onClick, int cornerRadius = 16)
    {
        Image image = CreateRoundedPanel(parent, name, bgColor, anchorMin, anchorMax, anchoredPos, sizeDelta, cornerRadius);

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        image.gameObject.AddComponent<ButtonHoverEffect>();
        button.onClick.AddListener(AudioManager.PlayButtonClick);
        if (onClick != null) button.onClick.AddListener(onClick);

        // Auto-sizing so longer labels (e.g. an emoji + several words) always fit the
        // button instead of overflowing or being clipped.
        TextMeshProUGUI labelText = CreateText(image.transform, "Label", label, 18, textColor, FontStyles.Bold,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 12;
        labelText.fontSizeMax = 18;

        return button;
    }

    // A simple checkbox-style toggle (cream box + gold checkmark) with its label placed
    // to the right, for Settings-style ON/OFF options.
    public static Toggle CreateWoodToggle(Transform parent, string name, string label, bool initialValue,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, UnityAction<bool> onValueChanged)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        Image box = go.AddComponent<Image>();
        box.sprite = GetRoundedSprite(6);
        box.type = Image.Type.Sliced;
        box.color = Cream;

        GameObject checkGO = new GameObject("Checkmark", typeof(RectTransform));
        checkGO.transform.SetParent(go.transform, false);
        RectTransform checkRect = checkGO.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.2f, 0.2f);
        checkRect.anchorMax = new Vector2(0.8f, 0.8f);
        checkRect.sizeDelta = Vector2.zero;
        checkRect.anchoredPosition = Vector2.zero;
        Image check = checkGO.AddComponent<Image>();
        check.sprite = GetRoundedSprite(4);
        check.color = GoldColor;

        Toggle toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = check;
        toggle.isOn = initialValue;
        if (onValueChanged != null) toggle.onValueChanged.AddListener(onValueChanged);

        CreateText(parent, name + "Label", label, 16, Cream, FontStyles.Normal,
            anchorMin, anchorMax, anchoredPos + new Vector2(sizeDelta.x * 0.5f + 100, 0), new Vector2(180, sizeDelta.y));

        return toggle;
    }

    // A dim full-screen overlay + centered rounded card, for menu popups (Level Guide/
    // Settings) that need several child elements (title, body, toggles, a Back button)
    // rather than BuildLevelCompletePanel's single baked-in message. Starts inactive;
    // the caller adds whatever content it needs under `card`.
    // useWoodTexture defaults to true (existing callers keep the wood-grain card look).
    // borderColorOverride defaults to null, which keeps the old Outline-based edge glow
    // (a single-direction drop-shadow-like effect); passing a color instead draws a real
    // uniform border frame (a slightly larger colored panel behind the card) since
    // Outline can't produce an even border on all four sides.
    public static GameObject BuildOverlayCard(Transform canvasTransform, string name, Vector2 cardSize,
        out Transform card, Color? cardColorOverride = null, bool useWoodTexture = true,
        Color? borderColorOverride = null)
    {
        GameObject overlay = new GameObject(name, typeof(RectTransform));
        overlay.transform.SetParent(canvasTransform, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.sizeDelta = Vector2.zero;
        overlayRect.anchoredPosition = Vector2.zero;
        overlay.AddComponent<Image>().color = OverlayColor;

        Color cardColor = cardColorOverride ?? PanelColor;

        if (borderColorOverride.HasValue)
        {
            Vector2 borderSize = cardSize + new Vector2(6, 6);
            CreateRoundedPanel(overlay.transform, name + "Border", borderColorOverride.Value,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, borderSize, 26);
        }

        Image cardImage = useWoodTexture
            ? CreateWoodPanel(overlay.transform, name + "Card", cardColor,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, cardSize, 24)
            : CreateRoundedPanel(overlay.transform, name + "Card", cardColor,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, cardSize, 24);

        if (!borderColorOverride.HasValue)
        {
            Outline outline = cardImage.gameObject.AddComponent<Outline>();
            outline.effectColor = GoldColor;
            outline.effectDistance = new Vector2(3, -3);
        }

        card = cardImage.transform;
        overlay.SetActive(false);
        return overlay;
    }

    public static Sprite GetRoundedSprite(int cornerRadius)
    {
        // TryGetValue only checks whether the C# dictionary entry exists - it can't tell
        // that a previously-cached Sprite's underlying Unity object was destroyed when a
        // prior Play session ended (these caches are static, so they outlive the scene).
        // The extra "|| sprite == null" uses Unity's overloaded equality to catch that
        // stale/destroyed case and regenerate, instead of handing back a dead reference
        // that silently renders nothing on the next Play session.
        if (!roundedSprites.TryGetValue(cornerRadius, out Sprite sprite) || sprite == null)
        {
            sprite = CreateProceduralSprite(64, cornerRadius, false);
            roundedSprites[cornerRadius] = sprite;
        }
        return sprite;
    }

    // A rounded-rect sprite whose RGB carries a subtle procedural wood-grain pattern
    // (Perlin noise bands). Tint via the SpriteRenderer/Image color to get any wood tone.
    public static Sprite GetRoundedWoodSprite(int cornerRadius)
    {
        if (!roundedWoodSprites.TryGetValue(cornerRadius, out Sprite sprite) || sprite == null)
        {
            sprite = CreateProceduralSprite(128, cornerRadius, true);
            roundedWoodSprites[cornerRadius] = sprite;
        }
        return sprite;
    }

    // A fully rounded (circular/oval when stretched) solid sprite - handy for leaves,
    // lamp shades, and other soft blob shapes.
    public static Sprite GetBlobSprite()
    {
        if (blobSprite == null)
            blobSprite = CreateProceduralSprite(64, 32, false);
        return blobSprite;
    }

    // A soft radial gradient (opaque at center, fading to transparent) for glows
    // (window sunbeam, lamp light) - not 9-sliced, meant to be scaled uniformly.
    public static Sprite GetGlowSprite()
    {
        if (glowSprite != null) return glowSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxDist = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = Mathf.Clamp01(1f - dist / maxDist);
                alpha *= alpha; // ease-out falloff
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return glowSprite;
    }

    // A procedural key silhouette (ring "bow" + shaft + two teeth on the bit) - solid
    // white so it can be tinted via SpriteRenderer.color, same convention as the other
    // procedural sprites here. Not 9-sliced: it's meant to be scaled uniformly.
    public static Sprite GetKeySprite()
    {
        if (keySprite != null) return keySprite;

        int width = 64;
        int height = 128;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Vector2 ringCenter = new Vector2(width * 0.5f, height * 0.78f);
        float ringOuter = width * 0.34f;
        float ringInner = width * 0.17f;
        float shaftHalfWidth = width * 0.11f;
        float shaftTop = ringCenter.y - ringOuter * 0.75f;
        float shaftBottom = height * 0.14f;
        float shaftRight = width * 0.5f + shaftHalfWidth;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool filled = false;

                float distFromRing = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), ringCenter);
                if (distFromRing <= ringOuter && distFromRing >= ringInner) filled = true;

                if (!filled && x >= width * 0.5f - shaftHalfWidth && x <= shaftRight &&
                    y >= shaftBottom && y <= shaftTop)
                    filled = true;

                // Tooth 1 (lower, longer)
                if (!filled && x > shaftRight && x <= shaftRight + width * 0.28f &&
                    y >= shaftBottom && y <= shaftBottom + height * 0.09f)
                    filled = true;

                // Tooth 2 (higher, shorter)
                if (!filled && x > shaftRight && x <= shaftRight + width * 0.16f &&
                    y >= shaftBottom + height * 0.16f && y <= shaftBottom + height * 0.24f)
                    filled = true;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, filled ? 1f : 0f));
            }
        }
        tex.Apply();

        keySprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        return keySprite;
    }

    private static Sprite CreateProceduralSprite(int size, int cornerRadius, bool woodGrain)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < size; y++)
        {
            float rowShade = 1f;
            if (woodGrain)
            {
                float grain = Mathf.PerlinNoise(0f, y * 0.12f);
                rowShade = 0.82f + 0.18f * grain;
            }

            for (int x = 0; x < size; x++)
            {
                float v = 1f;
                if (woodGrain)
                {
                    float knot = Mathf.PerlinNoise(x * 0.05f, y * 0.18f) * 0.08f;
                    v = Mathf.Clamp01(rowShade + knot - 0.04f);
                }

                float alpha = RoundedCornerAlpha(x, y, size, cornerRadius);
                tex.SetPixel(x, y, new Color(v, v, v, alpha));
            }
        }
        tex.Apply();

        Vector4 border = new Vector4(cornerRadius, cornerRadius, cornerRadius, cornerRadius);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, border);
    }

    private static float RoundedCornerAlpha(int x, int y, int size, int cornerRadius)
    {
        if (cornerRadius <= 0) return 1f;

        bool nearLeft = x < cornerRadius;
        bool nearRight = x >= size - cornerRadius;
        bool nearBottom = y < cornerRadius;
        bool nearTop = y >= size - cornerRadius;

        if (!((nearLeft || nearRight) && (nearBottom || nearTop))) return 1f;

        float cx = nearLeft ? cornerRadius : size - cornerRadius;
        float cy = nearBottom ? cornerRadius : size - cornerRadius;
        float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
        return Mathf.Clamp01(cornerRadius - dist + 0.5f);
    }

    // Config for an optional popup button (see BuildLevelCompletePanel's button1/button2
    // params) - a plain data holder, not a MonoBehaviour, so callers can build one inline.
    public struct PopupButtonConfig
    {
        public string label;
        public Color bgColor;
        public Color textColor;
        public UnityAction onClick;

        public PopupButtonConfig(string label, Color bgColor, Color textColor, UnityAction onClick)
        {
            this.label = label;
            this.bgColor = bgColor;
            this.textColor = textColor;
            this.onClick = onClick;
        }
    }

    // Wraps an existing (scene-assigned) TMP text object in a dimmed full-screen
    // overlay + centered rounded card, restyles the text, and returns the overlay (start
    // hidden). cardScale/verticalOffset/overlayAlphaOverride/messageFontSize default to
    // the original full-size/centered/opaque look (used by Level 2's Queue popup, which
    // calls this with all defaults) - pass overrides (as Level 1's Stack popup now does)
    // to get a smaller, repositioned, more translucent popup without touching other callers.
    // button1/button2 default to null (no buttons, exact previous behavior/layout for any
    // existing caller that doesn't pass them) - passing one or both reserves extra card
    // height/width and stacks them below the message instead of the message filling the
    // whole card.
    public static GameObject BuildLevelCompletePanel(Transform canvasTransform, GameObject existingText, string message,
        float cardScale = 1f, float verticalOffset = 0f, float? overlayAlphaOverride = null, float messageFontSize = 34f,
        Color? cardColorOverride = null, Color? messageColorOverride = null,
        PopupButtonConfig? button1 = null, PopupButtonConfig? button2 = null)
    {
        GameObject overlay = new GameObject("LevelCompleteOverlay", typeof(RectTransform));
        overlay.transform.SetParent(canvasTransform, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.sizeDelta = Vector2.zero;
        overlayRect.anchoredPosition = Vector2.zero;

        Color overlayColor = OverlayColor;
        if (overlayAlphaOverride.HasValue) overlayColor.a = overlayAlphaOverride.Value;
        overlay.AddComponent<Image>().color = overlayColor;

        // The dim backdrop being translucent isn't enough on its own to keep something
        // behind the popup visible - the card itself defaults to ~94% opaque (PanelColor's
        // alpha), which would still hide it. cardColorOverride lets a caller (Level 1's Stack
        // popup) make the card genuinely see-through too.
        Color cardColor = cardColorOverride ?? PanelColor;
        Color messageColor = messageColorOverride ?? GoldColor;

        bool hasButtons = button1.HasValue || button2.HasValue;
        // Extra height reserved for stacked buttons below the message, and a minimum
        // width so long button labels have room - only applied when buttons are actually
        // requested, so a caller passing neither (Level 2's Queue popup) gets the exact
        // same card size/layout as before.
        const float buttonAreaHeight = 190f;
        const float minWidthForButtons = 460f;
        Vector2 baseSize = new Vector2(560, 260) * cardScale;
        Vector2 cardSize = hasButtons
            ? new Vector2(Mathf.Max(baseSize.x, minWidthForButtons), baseSize.y + buttonAreaHeight)
            : baseSize;

        Image cardImage = CreateWoodPanel(overlay.transform, "LevelCompleteCard", cardColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, verticalOffset), cardSize, 24);
        GameObject card = cardImage.gameObject;

        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = GoldColor;
        outline.effectDistance = new Vector2(3, -3);

        // When buttons are present, the message occupies only the top portion of the
        // card (as a fraction of the card's now-taller height) instead of the whole
        // thing, leaving the bottom reserved for the button stack.
        float textAnchorMinY = hasButtons ? buttonAreaHeight / cardSize.y : 0f;

        if (existingText != null)
        {
            existingText.transform.SetParent(card.transform, false);
            RectTransform textRect = existingText.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, textAnchorMinY);
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;

            TextMeshProUGUI tmp = existingText.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = message;
                tmp.fontSize = messageFontSize;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = messageColor;
                tmp.alignment = TextAlignmentOptions.Center;
            }

            existingText.SetActive(true);
        }
        else
        {
            CreateText(card.transform, "LevelCompleteText", message, Mathf.RoundToInt(messageFontSize), messageColor,
                FontStyles.Bold, new Vector2(0f, textAnchorMinY), Vector2.one, Vector2.zero, Vector2.zero);
        }

        if (hasButtons)
        {
            const float btnHeight = 64f;
            const float btnSpacing = 16f;
            const float bottomMargin = 24f;
            float btnWidth = cardSize.x - 80f;

            // button2 ("Play Again") sits at the very bottom; button1 ("Use Key - Open
            // Door") stacks directly above it if both are present.
            if (button2.HasValue)
            {
                PopupButtonConfig cfg = button2.Value;
                CreateFlatButton(card.transform, "PopupButton2", cfg.label, cfg.bgColor, cfg.textColor,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, bottomMargin),
                    new Vector2(btnWidth, btnHeight), cfg.onClick);
            }
            if (button1.HasValue)
            {
                PopupButtonConfig cfg = button1.Value;
                float y = bottomMargin + (button2.HasValue ? btnHeight + btnSpacing : 0f);
                CreateFlatButton(card.transform, "PopupButton1", cfg.label, cfg.bgColor, cfg.textColor,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, y),
                    new Vector2(btnWidth, btnHeight), cfg.onClick);
            }
        }

        overlay.SetActive(false);
        return overlay;
    }
}
