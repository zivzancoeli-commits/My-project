using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class LevelEditor : MonoBehaviour
{
    [System.Serializable]
    public class Category
    {
        public string name;        // Only shown to you in the Inspector
        public Sprite icon;        // Icon on the category tab
        public TileBase[] items;   // What you can place from this category
    }

    public Tilemap tilemap;
    public Canvas canvas;
    public SpriteRenderer highlight;
    public GameObject player;
    public Category[] categories;
    public TextAsset[] premadeLevels;   // Level files you made, dragged in from Assets
    public TileBase startTile;          // Where the player begins
    public TileBase checkpointTile;     // Touch it to respawn there
    public TileBase finishTile;         // Touch it to win
    public TileBase enemyTile;
    public GameObject enemyPrefab;
    public KeyCode toggleKey = KeyCode.E;
    public KeyCode saveKey = KeyCode.K;
    public KeyCode loadKey = KeyCode.L;
    public float cameraSpeed = 10f;
    public float slotSize = 80f;
    public float padding = 10f;
    public TileBase coinTile;
    public TileBase heartTile;
    public TileBase keyTile;
    public TileBase lockTile;
    public GameObject heldKey;

    // True while the save or load window is open (PauseMenu checks this)
    public static bool menuOpen = false;

    private bool buildMode;
    private bool waitForRelease;
    private int currentCategory;
    private TileBase selected;
    private string currentTitle = "";
    private List<TileBase> allItems = new List<TileBase>();
    private bool levelDone;
    private Vector3Int lastCheckpoint;
    private List<GameObject> spawnedEnemies = new List<GameObject>();
    private List<Vector3Int> enemySpots = new List<Vector3Int>();
    private TextMeshProUGUI messageText;
    private Coroutine messageRoutine;
    private int coins;
    private bool hasKey;
    private Dictionary<Vector3Int, TileBase> pickedUp = new Dictionary<Vector3Int, TileBase>();

    // Thumbnails are drawn by a hidden camera looking at a hidden copy of the level
    private Grid previewGrid;
    private Tilemap previewMap;
    private Camera previewCam;
    private Coroutine thumbnailRoutine;
    private List<RenderTexture> thumbnails = new List<RenderTexture>();

    // Toolbar
    private RectTransform bar, tabRow, itemRow;
    private List<Image> tabSlots = new List<Image>();
    private List<Image> itemSlots = new List<Image>();
    private List<TileBase> itemSlotTiles = new List<TileBase>();
    private Color normalColor = new Color(0.25f, 0.25f, 0.3f, 1f);
    private Color selectedColor = new Color(1f, 0.85f, 0.3f, 1f);

    // Tooltip, save window, load window
    private GameObject tooltip;
    private TextMeshProUGUI tooltipText;
    private GameObject savePanel;
    private TextMeshProUGUI saveNameText;
    private string typedName = "";
    private GameObject loadPanel;
    private RectTransform loadViewport, loadList;
    private float loadScroll;

    [System.Serializable]
    class TileEntry { public Vector3Int pos; public int type; }

    [System.Serializable]
    class LevelData
    {
        public string title;
        public List<TileEntry> tiles = new List<TileEntry>();
    }

    void Start()
    {
        menuOpen = false;

        // Fill the Level category with start, checkpoint and finish if it's empty
        foreach (Category c in categories)
        {
            if (c.name == "Level" && (c.items == null || c.items.Length == 0))
                c.items = new TileBase[] { startTile, checkpointTile, finishTile };
        }

        // One combined list of everything placeable, used for saving
        foreach (Category c in categories)
            foreach (TileBase t in c.items)
                if (t != null && !allItems.Contains(t)) allItems.Add(t);

        if (allItems.Count > 0) selected = allItems[0];

        BuildToolbar();
        BuildTooltip();
        BuildSavePanel();
        BuildLoadPanel();
        BuildMessage();
        if (heldKey != null) heldKey.SetActive(false);   // Start without the key

        SetBuildMode(MainMenu.editorMode);
    }

    void Update()
    {
        // While a window is open, it gets all the input
        if (savePanel.activeSelf) { UpdateSaveTyping(); return; }
        if (loadPanel.activeSelf) { UpdateLoadScroll(); return; }

        if (Time.timeScale == 0f) return;

        // Checkpoints and the finish line work whenever you're playing
        if (!buildMode)
        {
            UpdateLevelRules();
            CheckItems();
        }

        // Editing only works when you came in through the Level Editor button
        if (!MainMenu.editorMode) return;

        if (Input.GetKeyDown(toggleKey)) SetBuildMode(!buildMode);
        if (!buildMode) return;

        if (Input.GetKeyDown(saveKey)) { OpenSavePanel(); return; }
        if (Input.GetKeyDown(loadKey)) { OpenLoadPanel(); return; }

        // Fly the camera around while building
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        Camera.main.transform.position += new Vector3(moveX, moveY, 0f) * cameraSpeed * Time.deltaTime;

        // Grow the toolbar while the mouse is over it
        bool overBar = RectTransformUtility.RectangleContainsScreenPoint(bar, Input.mousePosition, null);
        SetExpanded(overBar);

        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int cell = tilemap.WorldToCell(mouseWorld);

        if (highlight != null)
        {
            highlight.enabled = !overBar;
            highlight.transform.position = tilemap.GetCellCenterWorld(cell);
        }

        if (overBar) return;   // Don't build through the toolbar

        // After closing a window, wait until the mouse is let go before building
        if (waitForRelease)
        {
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) return;
            waitForRelease = false;
        }

        if (Input.GetMouseButton(0) && selected != null)
        {
            // Only one start allowed: placing a new one removes the old one
            if (selected == startTile && tilemap.GetTile(cell) != startTile)
                ReplaceAll(startTile, null);
            tilemap.SetTile(cell, selected);
        }
        else if (Input.GetMouseButton(1)) tilemap.SetTile(cell, null);
    }

    void SetBuildMode(bool on)
    {
        buildMode = on;
        bar.gameObject.SetActive(on);
        if (highlight != null) highlight.enabled = on;

        // Hide the player while building, bring it back to test-play
        if (player != null) player.SetActive(!on);

        // Stop the camera following the player while building
        CameraFollow follow = Camera.main.GetComponent<CameraFollow>();
        if (follow != null) follow.enabled = !on;

        // Starting to play: begin at the start flag
        if (!on)
        {
            RestoreItems();
            levelDone = false;
            PlacePlayerAtStart();
            SpawnEnemies();
        }
        else
        {
            RestoreItems();
            ClearEnemies();
        }

        // The tooltip only shows in the level editor
        tooltip.SetActive(MainMenu.editorMode);
        if (on)
        {
            tooltipText.text =
                "Left click: place\n" +
                "Right click: erase\n" +
                "WASD / arrows: move camera\n" +
                "Hover the bar: change category\n" +
                "E: test play\n" +
                "K: save level\n" +
                "L: load a level\n" +
                "Esc: pause";
        }
        else
        {
            tooltipText.text = "E: back to building\nEsc: pause";
        }
    }

    // ---------- Toolbar ----------

    void BuildToolbar()
    {
        bar = NewUI("EditorToolbar", canvas.transform);
        bar.SetAsFirstSibling();   // So the pause menu draws on top of it
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, padding);
        bar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        tabRow = NewUI("Tabs", bar);
        itemRow = NewUI("Items", bar);
        foreach (RectTransform row in new[] { tabRow, itemRow })
        {
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
        }
        itemRow.anchoredPosition = new Vector2(0f, padding);
        tabRow.anchoredPosition = new Vector2(0f, slotSize + padding * 2f);

        for (int i = 0; i < categories.Length; i++)
        {
            int index = i;
            tabSlots.Add(MakeSlot(tabRow, i, categories[i].icon, () => SelectCategory(index)));
        }
        tabRow.sizeDelta = new Vector2(RowWidth(categories.Length), slotSize);

        SelectCategory(0);
        SetExpanded(false);
    }

    void SelectCategory(int index)
    {
        if (categories.Length == 0) return;
        currentCategory = index;

        foreach (Transform child in itemRow) Destroy(child.gameObject);
        itemSlots.Clear();
        itemSlotTiles.Clear();

        TileBase[] items = categories[index].items;
        for (int i = 0; i < items.Length; i++)
        {
            TileBase tile = items[i];
            itemSlots.Add(MakeSlot(itemRow, i, IconFor(tile), () => SelectItem(tile)));
            itemSlotTiles.Add(tile);
        }
        itemRow.sizeDelta = new Vector2(RowWidth(items.Length), slotSize);

        RefreshColors();
    }

    void SelectItem(TileBase tile)
    {
        selected = tile;
        RefreshColors();
    }

    void SetExpanded(bool expanded)
    {
        tabRow.gameObject.SetActive(expanded);
        int itemCount = categories.Length > 0 ? categories[currentCategory].items.Length : 0;
        int widest = expanded ? Mathf.Max(categories.Length, itemCount) : itemCount;
        float height = expanded ? slotSize * 2f + padding * 3f : slotSize + padding * 2f;
        bar.sizeDelta = new Vector2(RowWidth(widest) + padding * 2f, height);
    }

    void RefreshColors()
    {
        for (int i = 0; i < tabSlots.Count; i++)
            tabSlots[i].color = i == currentCategory ? selectedColor : normalColor;
        for (int i = 0; i < itemSlots.Count; i++)
            itemSlots[i].color = itemSlotTiles[i] == selected ? selectedColor : normalColor;
    }

    float RowWidth(int count)
    {
        return Mathf.Max(count, 1) * (slotSize + padding) - padding;
    }

    Image MakeSlot(RectTransform row, int i, Sprite icon, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform slot = NewUI("Slot", row);
        slot.anchorMin = slot.anchorMax = Vector2.zero;
        slot.pivot = Vector2.zero;
        slot.sizeDelta = new Vector2(slotSize, slotSize);
        slot.anchoredPosition = new Vector2(i * (slotSize + padding), 0f);

        Image bg = slot.gameObject.AddComponent<Image>();
        Button button = slot.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(onClick);

        RectTransform iconRect = NewUI("Icon", slot);
        Stretch(iconRect, 8f);
        Image img = iconRect.gameObject.AddComponent<Image>();
        img.sprite = icon;
        img.preserveAspect = true;
        img.raycastTarget = false;

        return bg;
    }

    Sprite IconFor(TileBase t)
    {
        Tile tile = t as Tile;
        return tile != null ? tile.sprite : null;
    }

    // ---------- Tooltip ----------

    void BuildTooltip()
    {
        RectTransform box = NewUI("Tooltip", canvas.transform);
        box.SetSiblingIndex(1);   // Just above the toolbar, below the pause menu
        box.anchorMin = box.anchorMax = Vector2.zero;
        box.pivot = Vector2.zero;
        box.anchoredPosition = new Vector2(padding, padding);
        box.sizeDelta = new Vector2(380f, 250f);
        Image bg = box.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.5f);
        bg.raycastTarget = false;

        tooltipText = MakeText(box, "", 24f, TextAlignmentOptions.BottomLeft);
        Stretch((RectTransform)tooltipText.transform, 14f);
        tooltip = box.gameObject;
    }

    // ---------- Save window ----------

    void BuildSavePanel()
    {
        RectTransform panel = MakePanel("SavePanel", new Vector2(700f, 320f));

        TextMeshProUGUI title = MakeText(panel, "Name your level", 44f, TextAlignmentOptions.Center);
        Place((RectTransform)title.transform, new Vector2(0f, 110f), new Vector2(640f, 60f));

        RectTransform nameBox = NewUI("NameBox", panel);
        Place(nameBox, new Vector2(0f, 20f), new Vector2(600f, 70f));
        nameBox.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f, 1f);
        saveNameText = MakeText(nameBox, "", 36f, TextAlignmentOptions.Left);
        Stretch((RectTransform)saveNameText.transform, 16f);

        TextMeshProUGUI hint = MakeText(panel, "Type a name, then press Enter", 22f, TextAlignmentOptions.Center);
        Place((RectTransform)hint.transform, new Vector2(0f, -40f), new Vector2(640f, 30f));

        Place(MakeButton(panel, "Save", ConfirmSave), new Vector2(-130f, -110f), new Vector2(220f, 70f));
        Place(MakeButton(panel, "Cancel", CloseMenus), new Vector2(130f, -110f), new Vector2(220f, 70f));

        savePanel = panel.gameObject;
        savePanel.SetActive(false);
    }

    void OpenSavePanel()
    {
        typedName = currentTitle;
        saveNameText.text = typedName + "_";
        savePanel.SetActive(true);
        menuOpen = true;
        if (highlight != null) highlight.enabled = false;
    }

    void UpdateSaveTyping()
    {
        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (typedName.Length > 0) typedName = typedName.Substring(0, typedName.Length - 1);
            }
            else if (c == '\n' || c == '\r')
            {
                ConfirmSave();
                return;
            }
            else if (!char.IsControl(c) && typedName.Length < 30)
            {
                typedName += c;
            }
        }
        saveNameText.text = typedName + "_";
    }

    void ConfirmSave()
    {
        string title = typedName.Trim();
        if (title == "") return;   // Needs a name

        LevelData data = new LevelData();
        data.title = title;
        foreach (Vector3Int pos in tilemap.cellBounds.allPositionsWithin)
        {
            int type = allItems.IndexOf(tilemap.GetTile(pos));
            if (type >= 0) data.tiles.Add(new TileEntry { pos = pos, type = type });
        }

        string path = Path.Combine(LevelFolder(), SafeFileName(title) + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(data));
        currentTitle = title;
        Debug.Log("Saved \"" + title + "\" (" + data.tiles.Count + " tiles) to " + path);

        CloseMenus();
    }

    // ---------- Load window ----------

    void BuildLoadPanel()
    {
        RectTransform panel = MakePanel("LoadPanel", new Vector2(700f, 760f));

        TextMeshProUGUI title = MakeText(panel, "Load a level", 44f, TextAlignmentOptions.Center);
        Place((RectTransform)title.transform, new Vector2(0f, 320f), new Vector2(640f, 60f));

        // The scrolling area: anything outside it is hidden
        loadViewport = NewUI("Viewport", panel);
        Place(loadViewport, new Vector2(0f, 0f), new Vector2(620f, 520f));
        loadViewport.gameObject.AddComponent<RectMask2D>();

        loadList = NewUI("List", loadViewport);
        loadList.anchorMin = new Vector2(0f, 1f);
        loadList.anchorMax = new Vector2(1f, 1f);
        loadList.pivot = new Vector2(0.5f, 1f);

        Place(MakeButton(panel, "Cancel", CloseMenus), new Vector2(0f, -335f), new Vector2(220f, 60f));

        loadPanel = panel.gameObject;
        loadPanel.SetActive(false);
    }

    void OpenLoadPanel()
    {
        RefreshLoadList();
        loadScroll = 0f;
        loadList.anchoredPosition = Vector2.zero;
        loadPanel.SetActive(true);
        menuOpen = true;
        if (highlight != null) highlight.enabled = false;
    }

    void RefreshLoadList()
    {
        foreach (Transform child in loadList) Destroy(child.gameObject);
        ClearThumbnails();

        var jobs = new List<KeyValuePair<RawImage, string>>();
        int row = 0;

        // Premade levels first (gold), then everything saved on this computer
        foreach (TextAsset file in premadeLevels)
        {
            if (file == null) continue;
            jobs.Add(AddLoadEntry(row++, TitleOf(file.text, file.name), file.text, null));
        }
        foreach (string path in Directory.GetFiles(LevelFolder(), "*.json"))
        {
            string json = File.ReadAllText(path);
            jobs.Add(AddLoadEntry(row++, TitleOf(json, Path.GetFileNameWithoutExtension(path)), json, path));
        }

        if (row == 0)
        {
            TextMeshProUGUI empty = MakeText(loadList, "No levels yet. Press K to save one!", 28f, TextAlignmentOptions.Center);
            RectTransform r = (RectTransform)empty.transform;
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(0f, 80f);
            row = 1;
        }

        loadList.sizeDelta = new Vector2(0f, row * EntryStep);
        float maxScroll = Mathf.Max(0f, loadList.sizeDelta.y - loadViewport.rect.height);
        loadScroll = Mathf.Clamp(loadScroll, 0f, maxScroll);
        loadList.anchoredPosition = new Vector2(0f, loadScroll);

        if (thumbnailRoutine != null) StopCoroutine(thumbnailRoutine);
        thumbnailRoutine = StartCoroutine(RenderThumbnails(jobs));
    }

    const float EntryHeight = 140f;
    const float EntryStep = 150f;

    // filePath is null for premade levels (they can't be deleted)
    KeyValuePair<RawImage, string> AddLoadEntry(int row, string title, string json, string filePath)
    {
        RectTransform entry = MakeButton(loadList, "", () =>
        {
            LoadFromJson(json, title);
            CloseMenus();
        });
        entry.anchorMin = new Vector2(0f, 1f);
        entry.anchorMax = new Vector2(1f, 1f);
        entry.pivot = new Vector2(0.5f, 1f);
        entry.sizeDelta = new Vector2(-10f, EntryHeight);
        entry.anchoredPosition = new Vector2(0f, -row * EntryStep);
        if (filePath == null) entry.GetComponent<Image>().color = new Color(0.55f, 0.45f, 0.15f, 1f);

        // Thumbnail on the left
        RectTransform thumbRect = NewUI("Thumbnail", entry);
        thumbRect.anchorMin = thumbRect.anchorMax = new Vector2(0f, 0.5f);
        thumbRect.pivot = new Vector2(0f, 0.5f);
        thumbRect.anchoredPosition = new Vector2(8f, 0f);
        thumbRect.sizeDelta = new Vector2(224f, 126f);
        RawImage thumb = thumbRect.gameObject.AddComponent<RawImage>();
        thumb.color = Color.black;
        thumb.raycastTarget = false;

        // Title in the middle
        TextMeshProUGUI label = MakeText(entry, title, 30f, TextAlignmentOptions.Left);
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(248f, 0f);
        labelRect.offsetMax = new Vector2(filePath == null ? -12f : -130f, 0f);

        // Delete button on the right (click twice to be sure)
        if (filePath != null)
        {
            TextMeshProUGUI deleteLabel = null;
            bool armed = false;
            RectTransform del = MakeButton(entry, "Delete", () =>
            {
                if (!armed)
                {
                    armed = true;
                    deleteLabel.text = "Sure?";
                    return;
                }
                File.Delete(filePath);
                Debug.Log("Deleted \"" + title + "\"");
                RefreshLoadList();
            });
            del.anchorMin = del.anchorMax = new Vector2(1f, 0.5f);
            del.pivot = new Vector2(1f, 0.5f);
            del.anchoredPosition = new Vector2(-10f, 0f);
            del.sizeDelta = new Vector2(110f, 56f);
            del.GetComponent<Image>().color = new Color(0.6f, 0.15f, 0.15f, 1f);
            deleteLabel = del.GetComponentInChildren<TextMeshProUGUI>();
            deleteLabel.fontSize = 24f;
        }

        return new KeyValuePair<RawImage, string>(thumb, json);
    }

    // ---------- Thumbnails ----------

    IEnumerator RenderThumbnails(List<KeyValuePair<RawImage, string>> jobs)
    {
        EnsurePreview();

        foreach (var job in jobs)
        {
            LevelData data = JsonUtility.FromJson<LevelData>(job.Value);
            previewMap.ClearAllTiles();

            Vector3Int focus = Vector3Int.zero;
            bool foundStart = false;
            foreach (TileEntry e in data.tiles)
            {
                if (e.type < 0 || e.type >= allItems.Count) continue;
                previewMap.SetTile(e.pos, allItems[e.type]);
                if (allItems[e.type] == startTile) { focus = e.pos; foundStart = true; }
            }
            if (!foundStart && data.tiles.Count > 0) focus = data.tiles[0].pos;

            // Point the hidden camera at the start and draw into a small picture
            Vector3 center = previewMap.GetCellCenterWorld(focus);
            previewCam.transform.position = new Vector3(center.x, center.y + 1f, center.z - 10f);
            RenderTexture rt = new RenderTexture(320, 180, 16);
            thumbnails.Add(rt);
            previewCam.targetTexture = rt;
            previewCam.enabled = true;

            yield return null;   // Wait one frame so the camera draws it

            previewCam.enabled = false;
            if (job.Key != null)
            {
                job.Key.texture = rt;
                job.Key.color = Color.white;
            }
        }

        previewMap.ClearAllTiles();
        previewCam.targetTexture = null;
    }

    void EnsurePreview()
    {
        if (previewGrid != null) return;

        // Far away from the real level so the camera only sees the preview
        GameObject gridObject = new GameObject("PreviewGrid");
        gridObject.transform.position = new Vector3(10000f, 10000f, 0f);
        previewGrid = gridObject.AddComponent<Grid>();
        previewGrid.cellSize = tilemap.layoutGrid.cellSize;

        GameObject mapObject = new GameObject("PreviewTiles");
        mapObject.transform.SetParent(gridObject.transform, false);
        previewMap = mapObject.AddComponent<Tilemap>();
        mapObject.AddComponent<TilemapRenderer>();

        GameObject camObject = new GameObject("PreviewCamera");
        previewCam = camObject.AddComponent<Camera>();
        previewCam.orthographic = true;
        previewCam.orthographicSize = 4f;
        previewCam.clearFlags = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = Camera.main.backgroundColor;
        previewCam.enabled = false;
    }

    void ClearThumbnails()
    {
        if (thumbnailRoutine != null) { StopCoroutine(thumbnailRoutine); thumbnailRoutine = null; }
        if (previewCam != null) { previewCam.enabled = false; previewCam.targetTexture = null; }
        foreach (RenderTexture rt in thumbnails) if (rt != null) rt.Release();
        thumbnails.Clear();
    }

    void UpdateLoadScroll()
    {
        float maxScroll = Mathf.Max(0f, loadList.sizeDelta.y - loadViewport.rect.height);
        loadScroll = Mathf.Clamp(loadScroll - Input.mouseScrollDelta.y * 40f, 0f, maxScroll);
        loadList.anchoredPosition = new Vector2(0f, loadScroll);
    }

    void LoadFromJson(string json, string title)
    {
        LevelData data = JsonUtility.FromJson<LevelData>(json);
        tilemap.ClearAllTiles();
        foreach (TileEntry e in data.tiles)
            if (e.type >= 0 && e.type < allItems.Count) tilemap.SetTile(e.pos, allItems[e.type]);
        currentTitle = title;
        Debug.Log("Loaded \"" + title + "\"");
    }

    void CloseMenus()
    {
        ClearThumbnails();
        savePanel.SetActive(false);
        loadPanel.SetActive(false);
        menuOpen = false;
        waitForRelease = true;   // Don't place a block from the click that closed the window
    }

    // ---------- Start, checkpoints and finish ----------

    void UpdateLevelRules()
    {
        if (player == null || !player.activeSelf || levelDone) return;

        Vector3Int cell = tilemap.WorldToCell(player.transform.position);
        TileBase here = tilemap.GetTile(cell);
        if (here == null) return;

        if (here == checkpointTile && cell != lastCheckpoint)
        {
            lastCheckpoint = cell;
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null) health.SetSpawnPoint(tilemap.GetCellCenterWorld(cell));
            ShowMessage("Checkpoint!", 1.5f);
        }
        else if (here == finishTile)
        {
            levelDone = true;
            StartCoroutine(FinishLevel());
        }
    }

    IEnumerator FinishLevel()
    {
        ShowMessage("Level Complete!", 2.5f);
        player.SetActive(false);
        yield return new WaitForSeconds(2.5f);

        if (MainMenu.editorMode) SetBuildMode(true);   // Back to building
        else SceneManager.LoadScene("MainMenu");
    }

    void PlacePlayerAtStart()
    {
        if (player == null || startTile == null) return;

        foreach (Vector3Int pos in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.GetTile(pos) != startTile) continue;

            Vector3 spot = tilemap.GetCellCenterWorld(pos);
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null) health.TeleportTo(spot);
            else player.transform.position = spot;
            lastCheckpoint = pos;
            return;
        }
    }
    void SpawnEnemies()
    {
        if (enemyTile == null || enemyPrefab == null) return;
        foreach (Vector3Int pos in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.GetTile(pos) == enemyTile) enemySpots.Add(pos);
        }

        foreach (Vector3Int pos in enemySpots)
        {
            Vector3 spot = tilemap.GetCellCenterWorld(pos);
            spawnedEnemies.Add(Instantiate(enemyPrefab, spot, Quaternion.identity));
            tilemap.SetTile(pos, null);
        }
    }
    
    void ClearEnemies()
    {
        foreach (GameObject enemy in spawnedEnemies)
        {
            if (enemy != null) Destroy(enemy);
        }
        spawnedEnemies.Clear();

        foreach (Vector3Int pos in enemySpots)
        {
            tilemap.SetTile(pos, enemyTile);
        }
        enemySpots.Clear();
    }
   
    void CheckItems()
    {
        Vector3Int cell = tilemap.WorldToCell(player.transform.position);
        TileBase tile = tilemap.GetTile(cell);

        if (tile == coinTile && coinTile != null)
        {
            Take(cell);
            coins++;
            ShowMessage("Coins: " + coins, 1f);
        }
        else if (tile == heartTile && heartTile != null)
        {
            if (player.GetComponent<PlayerHealth>().Heal(1)) Take(cell);
        }
        else if (tile == keyTile && keyTile != null && !hasKey)
        {
            Take(cell);
            hasKey = true;
            if (heldKey != null) heldKey.SetActive(true);
        }

        if (hasKey && lockTile != null)
        {
            Vector3Int[] sides = { cell + Vector3Int.left, cell + Vector3Int.right, cell + Vector3Int.up, cell + Vector3Int.down };
            foreach (Vector3Int side in sides)
            {
                if (tilemap.GetTile(side) == lockTile)
                {
                    Take(side);
                    hasKey = false;
                    if (heldKey != null) heldKey.SetActive(false);
                    break;
                }
            }
        }
    }

    void Take(Vector3Int cell)
    {
        pickedUp[cell] = tilemap.GetTile(cell);
        tilemap.SetTile(cell, null);
    }

    void RestoreItems()
    {
        foreach (KeyValuePair<Vector3Int, TileBase> item in pickedUp)
            tilemap.SetTile(item.Key, item.Value);
        pickedUp.Clear();
        coins = 0;
        hasKey = false;
        if (heldKey != null) heldKey.SetActive(false);
    }

    void ReplaceAll(TileBase find, TileBase replaceWith)
    {
        foreach (Vector3Int pos in tilemap.cellBounds.allPositionsWithin)
            if (tilemap.GetTile(pos) == find) tilemap.SetTile(pos, replaceWith);
    }

    void BuildMessage()
    {
        messageText = MakeText(canvas.transform, "", 72f, TextAlignmentOptions.Center);
        Place((RectTransform)messageText.transform, new Vector2(0f, 250f), new Vector2(1200f, 120f));
        messageText.gameObject.SetActive(false);
    }

    void ShowMessage(string text, float seconds)
    {
        if (messageRoutine != null) StopCoroutine(messageRoutine);
        messageRoutine = StartCoroutine(MessageRoutine(text, seconds));
    }

    IEnumerator MessageRoutine(string text, float seconds)
    {
        messageText.text = text;
        messageText.gameObject.SetActive(true);
        yield return new WaitForSeconds(seconds);
        messageText.gameObject.SetActive(false);
    }

    // ---------- Files ----------

    string LevelFolder()
    {
        string folder = Path.Combine(Application.persistentDataPath, "Levels");
        Directory.CreateDirectory(folder);
        return folder;
    }

    string SafeFileName(string title)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
        return title;
    }

    string TitleOf(string json, string fallback)
    {
        LevelData d = JsonUtility.FromJson<LevelData>(json);
        return (d == null || string.IsNullOrEmpty(d.title)) ? fallback : d.title;
    }

    // ---------- UI helpers ----------

    RectTransform NewUI(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    RectTransform MakePanel(string name, Vector2 size)
    {
        RectTransform panel = NewUI(name, canvas.transform);
        Place(panel, Vector2.zero, size);
        panel.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 0.95f);
        return panel;
    }

    TextMeshProUGUI MakeText(Transform parent, string text, float size, TextAlignmentOptions align)
    {
        RectTransform rect = NewUI("Text", parent);
        TextMeshProUGUI t = rect.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    RectTransform MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = NewUI("Button", parent);
        Image bg = rect.gameObject.AddComponent<Image>();
        bg.color = normalColor;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(onClick);

        TextMeshProUGUI t = MakeText(rect, label, 30f, TextAlignmentOptions.Center);
        Stretch((RectTransform)t.transform, 6f);
        return rect;
    }
}
