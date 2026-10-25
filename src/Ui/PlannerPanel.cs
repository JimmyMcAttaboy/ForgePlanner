using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Forgeplan.Ui {

/// <summary>
/// Двойной щелчок по строке. Одиночный намеренно не считается: строка занимает
/// всю ширину списка, и случайный клик мимо кнопки добавлял бы вещь в план
/// незаметно для игрока.
/// </summary>
public class DoubleClick : MonoBehaviour, IPointerClickHandler {
    public System.Action Action;
    public void OnPointerClick(PointerEventData e) {
        if (e.clickCount == 2 && Action != null) Action();
    }
}

/// <summary>Таскает окно за заголовок.</summary>
public class DragMove : MonoBehaviour, IBeginDragHandler, IDragHandler {
    public RectTransform Target;
    Vector2 _grab;

    public void OnBeginDrag(PointerEventData e) {
        if (Target == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)Target.parent, e.position, e.pressEventCamera, out _grab);
        _grab = Target.anchoredPosition - _grab;
    }

    public void OnDrag(PointerEventData e) {
        if (Target == null) return;
        Vector2 p;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)Target.parent, e.position, e.pressEventCamera, out p))
            Target.anchoredPosition = p + _grab;
    }
}

/// <summary>
/// Окно планировщика.
///
/// Раскладка — три колонки: разделы, каталог, план. Так устроены большие окна
/// самой игры, и так же расставлен калькулятор на сайте: человек, пришедший с
/// сайта, не должен заново искать, где что.
///
/// Дерево объектов строится один раз, а обновляются только подписи — поэтому
/// каталог держит все пятьсот рецептов без ограничения, а строки
/// переиспользуются вместо пересоздания при каждой букве в поиске.
///
/// Planner и GameData про этот файл ничего не знают: они считают, он рисует.
/// </summary>
public class PlannerPanel {
    /* Фон. Угольный, а не чёрный: чистый чёрный выглядит дырой в экране, а
     * деревянная панель игры в своём цвете слишком светлая — подписи на ней
     * теряются. Поэтому спрайт панели умножается на угольный тон, а сверху
     * ложится ровный слой той же гаммы. Суммарная непрозрачность около 0,88:
     * сцена за окном угадывается, но читать не мешает. */
    static readonly Color Charcoal = new Color(0.30f, 0.28f, 0.25f, 0.80f);
    static readonly Color Veil = new Color(0.10f, 0.10f, 0.09f, 0.40f);

    GameObject _root;
    RectTransform _window;
    TMP_InputField _search;
    RectTransform _catalog, _cart, _totals;
    Button _viewBtn, _snapshotBtn, _coalBtn, _woodCoalBtn;
    TextMeshProUGUI _snapshotLine, _snapshotWarn;
    GameObject _snapshotWarnRow;
    readonly List<MaterialRow> _materialRows = new List<MaterialRow>();
    Image _planEdge, _gatherEdge;
    RectTransform _strip;
    GameObject _catalogPop, _catalogCatcher;
    readonly List<StationCard> _cards = new List<StationCard>();
    Image _pinPlan, _pinGather;

    readonly List<CatalogRow> _catalogRows = new List<CatalogRow>();
    readonly List<CartRow> _cartRows = new List<CartRow>();
    readonly List<TotemPieceRow> _pieceRows = new List<TotemPieceRow>();
    readonly List<UnitRow> _unitRows = new List<UnitRow>();
    /// <summary>Какие строки плана раскрыты на экземпляры. Состояние окна, а
    /// не плана: в файл не пишется, после перезапуска всё свёрнуто.</summary>
    readonly HashSet<Entry> _open = new HashSet<Entry>();
    readonly List<TotalPair> _pairs = new List<TotalPair>();
    readonly List<TotalRow> _notes = new List<TotalRow>();

    readonly List<KeyValuePair<Kind?, Button>> _tabs = new List<KeyValuePair<Kind?, Button>>();
    Kind? _group;                  // null — показывать всё

    readonly List<KeyValuePair<int, Button>> _biomes = new List<KeyValuePair<int, Button>>();
    int _biome = -1;               // -1 — все биомы

    GameObject _settings;          // оверлей настроек; null — ещё не собран
    GameObject _about;             // оверлей «Инфо»
    Button _numbersBtn, _barsBtn, _langEn, _langRu;
    UiKit.Switch _chestSw, _cardSw, _compareSw, _autoSw;
    /// <summary>Сменили вид закреплённых материалов: сохранить в конфиг.</summary>
    public System.Action<bool> OnPinBarsChanged;
    /// <summary>Сменили радиус снимка хранилища: сохранить в конфиг.</summary>
    public System.Action<float> OnSnapshotRadiusChanged;
    TextMeshProUGUI _radiusValue, _homeLine;
    /// <summary>Включили или выключили автоснимок: сохранить в конфиг.</summary>
    public System.Action<bool> OnAutoSnapshotChanged;
    /// <summary>Кнопки режима каталога — по одной на Progress.Mode, в том же
    /// порядке.</summary>
    readonly List<Button> _modeBtns = new List<Button>();
    TextMeshProUGUI _modeHint;

    /// <summary>Карточка предмета при наведении на строку.</summary>
    readonly ItemCard _card = new ItemCard();
    /// <summary>Переключатели карточки поменялись — сохранить в конфиг.</summary>
    public System.Action<bool, bool> OnCardChanged;
    TextMeshProUGUI _openLine;

    /// <summary>Настройки поменялись — плагину нужно сохранить их в конфиг.</summary>
    public System.Action<Progress.Mode> OnSpoilersChanged;
    public System.Action<bool> OnChestsChanged;
    /// <summary>Считать ли сундуки. Живёт в конфиге, сюда кладёт плагин.</summary>
    public bool Chests = true;
    /// <summary>Переключили «Своё»: сохранить в конфиг.</summary>
    public System.Action<bool> OnOwnedChanged;
    /// <summary>Язык переключили: плагину нужно сохранить это в конфиг.</summary>
    public System.Action<bool> OnLanguageChanged;
    Button _ownedBtn;

    List<ItemDef> _matches = new List<ItemDef>();
    string _queryShown;
    bool _rawView = true;
    bool _planDirty = true;
    /// <summary>Когда окно в последний раз перечитывало тотемы PlanBuild.</summary>
    float _totemsAt;

    public bool Visible {
        get { return _root != null && _root.activeSelf; }
    }

    /// <summary>Курсор стоит в строке поиска. Пока так — буквы принадлежат
    /// строке, а не игре: иначе «i» на середине слова откроет инвентарь.</summary>
    public bool Typing {
        get { return Visible && _search != null && _search.isFocused; }
    }

    /* ————————————————————————— открыть и закрыть ————————————————————————— */

    public void Toggle() {
        if (Visible) { Hide(); return; }
        if (!GameData.Build()) {
            ForgeplanPlugin.Log.LogWarning("Каталог не собран: нужен загруженный мир.");
            return;
        }
        if (!UiKit.Init()) {
            ForgeplanPlugin.Log.LogWarning("Интерфейс игры ещё не поднялся.");
            return;
        }
        // Время открытия пишем в лог намеренно. «Ничего не происходит по F7»
        // и «окно строится восемь секунд» снаружи выглядят одинаково, а
        // чинятся по-разному; одна строка в логе снимает вопрос.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Progress.Refresh();
        bool first = _root == null;
        if (first) Build();
        // Вид хранится в файле плана: перезашёл в игру, вид тот же.
        _rawView = Pin.RawView;
        _root.SetActive(true);
        Search(force: true);
        _planDirty = true;
        if (first)
            ForgeplanPlugin.Log.LogInfo(string.Format(
                "окно собрано за {0} мс, в каталоге {1} позиций",
                clock.ElapsedMilliseconds, _matches.Count));
    }

    public void Hide() {
        CloseCatalog();
        _card.Hide();
        if (_root != null) _root.SetActive(false);
    }

    /// <summary>
    /// Пересобрать окно. Нужно при смене языка: подписи расставлены по всему
    /// дереву объектов, и обойти их, подменяя текст, дороже и хрупче, чем
    /// собрать заново — тем более что сборка занимает три десятка миллисекунд.
    /// Выбранный раздел, биом и содержимое списка пересборку переживают: они
    /// живут в полях, а не в интерфейсе.
    /// </summary>
    public void Rebuild() {
        bool wasVisible = Visible;
        Drop();
        if (!wasVisible) return;
        Build();
        _root.SetActive(true);
        Search(force: true);
        _planDirty = true;
    }

    /// <summary>Каталог держит ссылки на префабы мира — при выходе из мира
    /// окно нужно выбросить целиком, иначе иконки станут пустыми.</summary>
    /// <summary>
    /// Забыть выбранный раздел и биом.
    ///
    /// Зовётся при выходе из мира, а не из Drop: пересборку при смене языка
    /// фильтры переживать обязаны — там ничего не изменилось, кроме подписей.
    /// А вот фильтр, выставленный прошлым персонажем в прошлом мире, в новом
    /// не помнит никто, и окно открывается почти пустым.
    /// </summary>
    public void ResetFilters() {
        _group = null;
        _biome = -1;
        _queryShown = null;
    }

    public void Drop() {
        _card.Drop();
        _settings = null;
        _about = null;
        _ask = null;
        _askWhere = null;
        _numbersBtn = _barsBtn = _langEn = _langRu = null;
        _chestSw = _cardSw = _compareSw = _autoSw = null;
        _radiusValue = _homeLine = null;
        _modeBtns.Clear();
        _modeHint = null;
        _openLine = null;
        // Гасим до Destroy: тот откладывает удаление до конца кадра, и при
        // пересборке на экране на кадр оказались бы два окна внахлёст.
        if (_root != null) _root.SetActive(false);
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _catalogRows.Clear();
        _cartRows.Clear();
        _cards.Clear();
        _catalogPop = _catalogCatcher = null;
        _pieceRows.Clear();
        _unitRows.Clear();
        _pairs.Clear();
        _materialRows.Clear();
        _notes.Clear();
        _matches.Clear();
        _tabs.Clear();
        _biomes.Clear();
        _queryShown = null;
    }

    public void Tick() {
        if (!Visible) return;
        if (_search != null && _search.text != _queryShown) Search();
        if (_catalogRows.Count < _matches.Count) Fill(false);
        // Тотем меняется без нашего участия: в него носят материалы, по нему
        // достраиваются планы. Пока он в плане, окно перечитывает его само.
        if (Time.unscaledTime - _totemsAt > 1f && PlanTotems.AnyIn(Planner.Cart)) {
            _totemsAt = Time.unscaledTime;
            _planDirty = true;
        }
        if (_planDirty) { RefreshPlan(); _planDirty = false; }
        _card.Tick();
    }

    /// <summary>Качество для карточки строки каталога: то, что выбрано в
    /// плане, если вещь там есть, иначе первое.</summary>
    int QualityOf(string id) {
        var e = Planner.Cart.FirstOrDefault(x => x.Id == id);
        return e != null ? e.Quality : 1;
    }

    /* ————————————————————————— сборка дерева ————————————————————————— */

    void Build() {
        _root = new GameObject("Forgeplan", typeof(RectTransform));
        _root.transform.SetParent(UiKit.CanvasRoot, false);
        var rootRt = (RectTransform)_root.transform;
        UiKit.Stretch(rootRt);

        _window = UiKit.Rect(rootRt, "window");
        _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
        _window.pivot = new Vector2(0.5f, 0.5f);
        _window.sizeDelta = WindowSize(rootRt);

        UiKit.Box(_window, "bg", UiKit.PanelSprite, UiKit.PanelSprite != null
            ? Charcoal : new Color(0.13f, 0.12f, 0.11f, 0.92f));
        UiKit.Box(_window, "veil", null, Veil);

        var col = _window.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(16, 16, 12, 16);
        col.spacing = 12;
        col.childForceExpandHeight = false;
        col.childForceExpandWidth = true;
        col.childControlHeight = true;
        col.childControlWidth = true;

        // Окно отдано плану: поиск, план, что собрать. Каталог не стоит
        // колонкой, а выпадает поверх плана, пока ищут.
        BuildHeader();
        BuildPlanSection();
        BuildGatherSection();
        BuildCatalog();
    }

    /// <summary>Окно во весь экран не нужно, но и жёсткий размер не годится:
    /// у игры канва масштабируется настройкой «размер интерфейса», и окно в
    /// 1180 точек у кого-то вылезет за край. Отсюда потолок по канве.</summary>
    static Vector2 WindowSize(RectTransform root) {
        var r = root.rect;
        float w = r.width > 1f ? Mathf.Min(1240f, r.width * 0.94f) : 1240f;
        float h = r.height > 1f ? Mathf.Min(806f, r.height * 0.94f) : 806f;
        return new Vector2(w, h);
    }

    /// <summary>Нижний край полосы поиска от верха окна: под ним выпадает
    /// каталог.</summary>
    const float HeaderBottom = 12f + 40f + 4f;

    /// <summary>
    /// Одна полоса: название, поиск, настройки и закрыть. Язык и «Инфо» ушли
    /// в настройки, а название встало в ряд с поиском и полосы не забирает.
    /// </summary>
    void BuildHeader() {
        var bar = UiKit.Row(_window, "header", 40);
        UiKit.Horizontal(bar, 8);

        // Тащим окно за полосу. Прозрачная картинка нужна, чтобы полоса
        // ловила курсор: без raycastTarget тянуть будет не за что.
        var grip = bar.gameObject.AddComponent<Image>();
        grip.color = new Color(0, 0, 0, 0);
        grip.raycastTarget = true;
        bar.gameObject.AddComponent<DragMove>().Target = _window;

        var title = UiKit.Label(bar, "title", L.Title, 24, UiKit.Gold);
        title.raycastTarget = false;   // тащить окно за название тоже можно

        // Поле и крестик: один ряд с общей обводкой, крестик читается как
        // часть поля, а не как ещё одна кнопка.
        var searchRow = UiKit.Rect(bar, "search-row");
        var sle = searchRow.gameObject.AddComponent<LayoutElement>();
        sle.flexibleWidth = 1;
        sle.minHeight = sle.preferredHeight = 38;
        UiKit.Horizontal(searchRow, 0, 1, 4);
        UiKit.Frame(searchRow, new Color(0.93f, 0.77f, 0.38f, 0.32f), 1f);

        _search = UiKit.Input(searchRow, L.SearchHint, 36);
        _search.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1;
        _search.onSelect.AddListener(_ => OpenCatalog());
        _search.onValueChanged.AddListener(_ => OpenCatalog());
        UiKit.Button(searchRow, "×", 32, 28, () => {
            _search.text = "";
            CloseCatalog();
        });

        var gear = UiKit.IconBtn(bar, Art.Get("gear"), 38, ShowSettings, 24);
        UiKit.Tip(gear, L.Settings);
        var close = UiKit.Button(bar, "×", 38, 38, Hide, 22);
        UiKit.Tip(close, L.Close);
    }

    /// <summary>Часть окна: золотая черта сверху (горит, пока часть
    /// закреплена), заголовок и содержимое.</summary>
    static RectTransform Section(Transform parent, string name, float flex, out Image edge) {
        var sec = UiKit.Rect(parent, name);
        var le = sec.gameObject.AddComponent<LayoutElement>();
        le.flexibleHeight = flex;
        le.minHeight = 150;
        var col = sec.gameObject.AddComponent<VerticalLayoutGroup>();
        col.spacing = 6;
        col.childForceExpandHeight = false;
        col.childForceExpandWidth = true;
        col.childControlHeight = true;
        col.childControlWidth = true;
        edge = UiKit.Row(sec, "edge", 2).gameObject.AddComponent<Image>();
        edge.color = Color.clear;
        edge.raycastTarget = false;
        return sec;
    }

    static void Spacer(Transform parent) {
        UiKit.Rect(parent, "spacer").gameObject
             .AddComponent<LayoutElement>().flexibleWidth = 1;
    }

    void BuildPlanSection() {
        var sec = Section(_window, "plan", 1f, out _planEdge);
        var head = UiKit.Row(sec, "head", 32);
        UiKit.Horizontal(head, 6, 4, 4);
        UiKit.Label(head, "title", L.CraftPlan, 20, UiKit.Gold);
        Spacer(head);
        // Тотем PlanBuild: только если PlanBuild стоит, кнопка, которая всегда
        // отвечает «тотемов нет», только шумит.
        if (PlanTotems.Available)
            UiKit.Button(head, L.AddTotem, 104, 28, AddTotem);
        var clear = UiKit.IconBtn(head, Art.Get("clear"), 30, () => {
            Planner.Cart.Clear();
            _planDirty = true;
        }, 18);
        UiKit.Tip(clear, L.TipClear);
        _pinPlan = PinToggle(head, () => { Pin.TogglePlan(); _planDirty = true; });
        UiKit.Tip(_pinPlan, L.TipPinPlan, L.TipPinPlanText);

        var frame = UiKit.Rect(sec, "frame");
        var fle = frame.gameObject.AddComponent<LayoutElement>();
        fle.flexibleHeight = 1;
        fle.minHeight = 110;
        _cart = UiKit.ScrollBox(frame, 4);
    }

    /// <summary>
    /// «Нужно собрать»: вид (сырьё или рецепты) рядом с заголовком, счёт
    /// значками справа, станки плашками над списком материалов. Что делает
    /// каждый значок, говорит подсказка при наведении.
    /// </summary>
    void BuildGatherSection() {
        var sec = Section(_window, "gather", 1.3f, out _gatherEdge);
        var head = UiKit.Row(sec, "head", 32);
        UiKit.Horizontal(head, 6, 4, 4);
        UiKit.Label(head, "title", L.ToGather, 20, UiKit.Gold);
        Gap(head, 10);
        // Сырьё и рецепты: одна кнопка, которая их переключает. Выбранный вид
        // подписан золотом, второй виден рядом серым.
        _viewBtn = UiKit.Button(head, "", 176, 30, () => {
            _rawView = !_rawView; _planDirty = true;
        }, 15);
        UiKit.Tip(_viewBtn, L.Raw + " / " + L.Recipes, L.HintRaw + "\n" + L.HintRecipes);
        Spacer(head);
        _coalBtn = IconToggle(head, FirstIcon("coal"), L.Coal, L.TipCoalText,
            () => Planner.CountCoal = !Planner.CountCoal);
        _woodCoalBtn = IconToggle(head, FirstIcon("wood"), L.Firewood, L.TipFirewoodText,
            () => Planner.BurnCoalFromWood = !Planner.BurnCoalFromWood);
        _ownedBtn = IconToggle(head, FirstIcon("armorbronzechest", "armorleatherchest"), L.Owned,
            L.TipOwnedText, () => {
                Planner.UseOwned = !Planner.UseOwned;
                if (OnOwnedChanged != null) OnOwnedChanged(Planner.UseOwned);
                Ui.PinHud.Invalidate();
            });
        Gap(head, 10);
        // Снимок хранилища: что лежит в сундуках рядом. Кнопка видна, пока
        // материалы закреплены; золотом, когда снимка нет или он снят вдали
        // от сундуков.
        _snapshotBtn = UiKit.Button(head, L.UpdateSnapshot, 230, 30, TakeSnapshot, 14);
        UiKit.Tip(_snapshotBtn, L.UpdateSnapshot, L.UpdateSnapshotText);
        _pinGather = PinToggle(head, () => { Pin.ToggleGather(); _planDirty = true; });
        UiKit.Tip(_pinGather, L.TipPinGather, L.TipPinGatherText);

        // Откуда числа: снимок (сколько сундуков, когда, как далеко отсюда)
        // или, пока не закреплено, сколько сундуков рядом сейчас.
        _snapshotLine = UiKit.Label(UiKit.Row(sec, "snapshot", 18), "text", "", 13, UiKit.Dim);
        UiKit.Stretch((RectTransform)_snapshotLine.transform);
        // Снимок без единого сундука: сняли в поле. Красной полосой с кнопкой,
        // чтобы ошибку было видно и чинилась она в одно нажатие.
        var warn = UiKit.Row(sec, "snapshot-warn", 30);
        UiKit.Horizontal(warn, 8, 8, 4);
        var wbg = warn.gameObject.AddComponent<Image>();
        wbg.color = new Color(0.94f, 0.48f, 0.37f, 0.14f);
        wbg.raycastTarget = false;
        UiKit.Frame(warn, new Color(0.94f, 0.48f, 0.37f, 0.55f), 1f);
        _snapshotWarn = UiKit.Label(warn, "text", "", 13, new Color(0.96f, 0.52f, 0.40f));
        _snapshotWarn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        var fix = UiKit.Button(warn, L.UpdateSnapshot, 220, 24, TakeSnapshot, 13);
        Mark(fix, true);
        _snapshotWarnRow = warn.gameObject;
        _snapshotWarnRow.SetActive(false);
        Bar.Legend(sec, 13f);

        // Станки и переработка плашками: сетка сама переносит их на новую
        // строку, когда окно узкое.
        _strip = UiKit.Rect(sec, "stations");
        var grid = _strip.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(248f, 44f);
        grid.spacing = new Vector2(8f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.Flexible;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;

        var frame = UiKit.Rect(sec, "frame");
        var fle = frame.gameObject.AddComponent<LayoutElement>();
        fle.flexibleHeight = 1;
        fle.minHeight = 110;
        _totals = UiKit.ScrollBox(frame, 3);
    }

    void TakeSnapshot() { Pin.Snapshot(); _planDirty = true; }

    /// <summary>Значок-переключатель счёта: название и смысл в подсказке.</summary>
    Button IconToggle(Transform parent, Sprite icon, string name, string text, System.Action flip) {
        var btn = UiKit.IconBtn(parent, icon, 40, () => { flip(); _planDirty = true; }, 32);
        if (icon == null) {
            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = name.Substring(0, 1);
        }
        UiKit.Tip(btn, name, text);
        return btn;
    }

    /// <summary>Значок первого предмета из списка, что есть в игре: сборки и
    /// моды переименовывают префабы, поэтому запасных несколько.</summary>
    static Sprite FirstIcon(params string[] ids) {
        foreach (var id in ids) {
            var it = GameData.Get(id);
            if (it != null && it.Icon != null) return it.Icon;
        }
        return null;
    }

    static Sprite PieceIcon(string prefab) {
        var it = GameData.Get(GameData.PieceId(prefab));
        return it != null ? it.Icon : null;
    }

    /* ————————————————————————— каталог поверх плана ————————————————————————— */

    /// <summary>
    /// Каталог выпадает из поиска поверх плана и закрывается щелчком мимо,
    /// крестиком в поле или Esc. Колонкой во всё окно он занимал место, пока
    /// в нём ничего не искали.
    /// </summary>
    void BuildCatalog() {
        var catcher = UiKit.Rect(_window, "catalog-catcher");
        catcher.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UiKit.Stretch(catcher);
        catcher.offsetMax = new Vector2(0f, -HeaderBottom);
        var shade = catcher.gameObject.AddComponent<Image>();
        shade.color = new Color(0f, 0f, 0f, 0.35f);
        shade.raycastTarget = true;
        var miss = catcher.gameObject.AddComponent<Button>();
        miss.transition = Selectable.Transition.None;
        miss.onClick.AddListener(CloseCatalog);
        _catalogCatcher = catcher.gameObject;

        var pop = UiKit.Rect(_window, "catalog");
        pop.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        pop.anchorMin = new Vector2(0f, 0.16f);
        pop.anchorMax = new Vector2(1f, 1f);
        pop.offsetMin = new Vector2(16f, 0f);
        pop.offsetMax = new Vector2(-16f, -HeaderBottom);
        UiKit.Box(pop, "bg", UiKit.PanelSprite, UiKit.PanelSprite != null
            ? Charcoal : new Color(0.13f, 0.12f, 0.11f, 0.97f));
        UiKit.Box(pop, "veil", null, new Color(0.07f, 0.06f, 0.05f, 0.88f));
        UiKit.Frame(pop, new Color(0.93f, 0.77f, 0.38f, 0.55f), 2f);

        var col = pop.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(12, 12, 10, 12);
        col.spacing = 8;
        col.childForceExpandHeight = false;
        col.childForceExpandWidth = true;
        col.childControlHeight = true;
        col.childControlWidth = true;

        var body = UiKit.Rect(pop, "body");
        body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var h = UiKit.Horizontal(body, 12);
        h.childForceExpandHeight = true;

        var frame = UiKit.Rect(body, "frame");
        frame.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        _catalog = UiKit.ScrollBox(frame);
        BuildFilters(body);
        _catalogPop = pop.gameObject;

        _catalogPop.SetActive(false);
        _catalogCatcher.SetActive(false);
    }

    void OpenCatalog() {
        if (_catalogPop == null || _catalogPop.activeSelf) return;
        _catalogCatcher.SetActive(true);
        _catalogPop.SetActive(true);
    }

    void CloseCatalog() {
        if (_catalogPop == null || !_catalogPop.activeSelf) return;
        _catalogPop.SetActive(false);
        _catalogCatcher.SetActive(false);
        _card.Hide();
        var es = EventSystem.current;
        if (es != null && _search != null && es.currentSelectedGameObject == _search.gameObject)
            es.SetSelectedGameObject(null);
    }

    /// <summary>Esc закрывает верхний слой окна, а не всё окно сразу.
    /// false: закрывать нечего, пусть закрывается окно.</summary>
    public bool Escape() {
        if (_ask != null && _ask.activeSelf) { _ask.SetActive(false); return true; }
        if (_about != null && _about.activeSelf) { _about.SetActive(false); return true; }
        if (_settings != null && _settings.activeSelf) { HideSettings(); return true; }
        if (_catalogPop != null && _catalogPop.activeSelf) { CloseCatalog(); return true; }
        return false;
    }

    /// <summary>
    /// Фильтры справа от списка, подписанными кнопками в две колонки: разделы
    /// и биомы. Значки предметов на их месте каждый читал по-своему, а слово
    /// читается одинаково.
    /// </summary>
    void BuildFilters(Transform parent) {
        var side = UiKit.Rect(parent, "filters");
        var le = side.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 2 * FilterWidth + 10;
        le.flexibleWidth = 0;
        var h = UiKit.Horizontal(side, 10);
        h.childAlignment = TextAnchor.UpperLeft;
        h.childForceExpandHeight = true;

        var kinds = FilterColumn(side);
        AddTab(kinds, L.All, null);
        AddTab(kinds, L.Weapons, Kind.Weapon);
        AddTab(kinds, L.Armour, Kind.Armor);
        AddTab(kinds, L.Tools, Kind.Tool);
        AddTab(kinds, L.Food, Kind.Food);
        AddTab(kinds, L.Materials, Kind.Material);
        AddTab(kinds, L.Buildings, Kind.Piece);
        AddTab(kinds, L.Other, Kind.Other);

        var biomes = FilterColumn(side);
        AddBiome(biomes, L.AllBiomes, -1);
        for (int i = 0; i < Tiers.Ids.Length; i++) AddBiome(biomes, BiomeLabel(i), i);
    }

    const float FilterWidth = 150f;

    static RectTransform FilterColumn(Transform parent) {
        var col = UiKit.Rect(parent, "column");
        var le = col.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = FilterWidth;
        var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childAlignment = TextAnchor.UpperLeft;
        return col;
    }

    /// <summary>Кнопка раздела. null: всё.</summary>
    void AddTab(Transform parent, string label, Kind? kind) {
        var btn = UiKit.Button(parent, label, FilterWidth, 30, () => {
            _group = kind;
            Search(force: true);
        }, 15, TextAlignmentOptions.Left);
        _tabs.Add(new KeyValuePair<Kind?, Button>(kind, btn));
    }

    static string BiomeLabel(int tier) {
        var name = L.Ru ? Tiers.Ru[tier] : Tiers.En[tier];
        return "<color=" + Tiers.Color[tier] + ">\u25CF</color> " + name;
    }

    void AddBiome(Transform parent, string label, int tier) {
        var btn = UiKit.Button(parent, label, FilterWidth, 30, () => {
            if (!Progress.Unlocked(tier)) return;
            _biome = tier;
            Search(force: true);
        }, 15, TextAlignmentOptions.Left);
        _biomes.Add(new KeyValuePair<int, Button>(tier, btn));
    }

    /// <summary>
    /// Закрытый биом гасится, а не прячется: исчезающая кнопка читается как
    /// поломка, а погасшая как «сюда пока нельзя».
    /// </summary>
    void MarkBiomes() {
        foreach (var b in _biomes) {
            bool open = b.Key < 0 || Progress.Unlocked(b.Key);
            var btn = b.Value;
            btn.interactable = open;
            foreach (var t in btn.GetComponentsInChildren<TMP_Text>(true))
                t.color = !open ? UiKit.Faint
                        : b.Key == _biome ? UiKit.Gold : UiKit.Dim;
        }
    }

    /// <summary>Подпись переключателя вида: «Сырьё / Рецепты», выбранное золотом.</summary>
    void ShowView() {
        if (_viewBtn == null) return;
        string on = "#" + ColorUtility.ToHtmlStringRGB(UiKit.Gold);
        string off = "#" + ColorUtility.ToHtmlStringRGB(UiKit.Dim);
        string text = "<color=" + (_rawView ? on : off) + ">" + L.Raw + "</color>"
                    + "<color=" + off + "> / </color>"
                    + "<color=" + (_rawView ? off : on) + ">" + L.Recipes + "</color>";
        foreach (var t in _viewBtn.GetComponentsInChildren<TMP_Text>(true)) t.text = text;
    }

    /// <summary>Булавка-переключатель у заголовка части. Без рамки кнопки:
    /// это значок, а не ещё одна кнопка в ряду.</summary>
    static Image PinToggle(Transform parent, System.Action onClick, string art = "pin") {
        var icon = UiKit.Icon(parent, Art.Get(art), 22);
        icon.color = UiKit.Dim;
        icon.raycastTarget = true;
        var btn = icon.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick());
        var hover = icon.gameObject.AddComponent<Hover>();
        hover.Enter = () => icon.transform.localScale = Vector3.one * 1.15f;
        hover.Exit = () => icon.transform.localScale = Vector3.one;
        return icon;
    }

    static void MarkPin(Image icon, bool on) {
        if (icon != null) icon.color = on ? UiKit.Gold : UiKit.Dim;
    }

    /* Размеры блоков строки плана. */
    const float LevelsWidth = 4 * 28 + 3 * 4;    // четыре кнопки уровня
    const float QtyWidth = 32 + 36 + 32 + 2 * 4; // «−», число, «+»
    const float DelWidth = 28;
    const float FoldWidth = 18;                  // стрелка «раскрыть»
    /// <summary>Где в строке плана начинается название: отступ, стрелка,
    /// иконка и зазоры между ними. По нему выравниваются вложенные строки.</summary>
    const int NameIndent = 4 + (int)FoldWidth + 6 + 26 + 6;
    const float BlockGap = 14;                   // зазор между блоками

    /// <summary>Пустое место заданной ширины: зазор между блоками строки.</summary>
    static void Gap(Transform parent, float w) {
        var le = UiKit.Rect(parent, "gap").gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w;
        le.flexibleWidth = 0;
    }

    /// <summary>Блок фиксированной ширины с детьми по центру.</summary>
    static RectTransform Block(Transform parent, string name, float w) {
        var rt = UiKit.Rect(parent, name);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w;
        le.flexibleWidth = 0;
        UiKit.Horizontal(rt, 4).childAlignment = TextAnchor.MiddleCenter;
        return rt;
    }

    static void Mark(Button b, bool on) {
        if (b == null) return;
        foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
            t.color = on ? UiKit.Gold : UiKit.Dim;
    }

    /* ————————————————————————— каталог ————————————————————————— */

    void Search(bool force = false) {
        var q = _search != null ? _search.text : "";
        if (!force && q == _queryShown) return;
        _queryShown = q;
        Progress.Refresh();
        // Выбранный биом мог закрыться: фильтр включили в настройках, или
        // окно осталось открытым с прошлого мира. Тогда возвращаемся ко «всем».
        if (_biome >= 0 && !Progress.Unlocked(_biome)) _biome = -1;

        foreach (var t in _tabs) Mark(t.Value, t.Key.Equals(_group));
        MarkBiomes();

        var needle = (q ?? "").Trim().ToLowerInvariant();
        _matches = GameData.Items.Values
            .Where(i => i.HasRecipe)
            .Where(i => !_group.HasValue || i.Group == _group.Value)
            .Where(i => _biome < 0 || i.Tier == _biome)
            .Where(Progress.Shows)
            .Where(i => needle.Length == 0 || i.Haystack.Contains(needle))
            .OrderBy(i => i.Shown)
            .ToList();

        int total = GameData.Items.Values.Count(i => i.HasRecipe);
        // Ноль найденных при полном каталоге — либо фильтры, либо ошибка в них.
        // Снаружи это выглядит одинаково, поэтому состояние уходит в лог.
        if (_matches.Count == 0 && total > 0)
            ForgeplanPlugin.Log.LogInfo(string.Format(
                "поиск ничего не дал: раздел={0}, биом={1}, запрос=\"{2}\", "
                + "прогрессия={3}, всего с рецептом={4}",
                _group.HasValue ? _group.Value.ToString() : "всё",
                _biome < 0 ? "все" : Tiers.Ids[_biome],
                needle, Progress.Current, total));
        Fill(true);

        // Смена раздела при прокрутке вниз оставляла пустоту: список стал
        // короче, а положение осталось прежним.
        _catalog.anchoredPosition = Vector2.zero;
    }

    /// <summary>Сколько строк каталога создавать за кадр.</summary>
    const int RowsPerFrame = 48;

    /// <summary>
    /// Достроить и заполнить строки каталога.
    ///
    /// Строки переиспользуются: лишние прячем, недостающие создаём — полный
    /// пересбор на каждую букву в поиске дёргал бы кадры. Но создаём их не все
    /// разом, а горстью за кадр, и вот почему: с приходом построек в каталоге
    /// стало 1126 позиций вместо 490, а строка — это шесть объектов Unity.
    /// Семь тысяч объектов в одном кадре подвешивали игру на несколько секунд,
    /// и первое нажатие F7 выглядело так, будто мод не работает.
    ///
    /// Прокрутке это не мешает: пока строки достраиваются, список уже виден и
    /// растёт сверху вниз, а полный он через десяток кадров.
    /// </summary>
    void Fill(bool refill) {
        int have = _catalogRows.Count;
        int want = Mathf.Min(_matches.Count, have + RowsPerFrame);
        for (int i = have; i < want; i++) _catalogRows.Add(new CatalogRow(_catalog, this));

        // При дозаполнении трогаем только новые: у прежних текст уже верный.
        for (int i = refill ? 0 : have; i < _catalogRows.Count; i++) {
            if (i < _matches.Count) _catalogRows[i].Show(_matches[i]);
            else _catalogRows[i].Hide();
        }
    }

    /// <summary>Добавить в план ближайший тотем PlanBuild.</summary>
    void AddTotem() {
        var me = Player.m_localPlayer;
        var id = me != null ? PlanTotems.Nearest(me.transform.position) : null;
        if (id == null) {
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, L.NoTotemNearby);
            return;
        }
        if (Planner.Cart.Any(e => e.Id == id)) return;
        PlanTotems.Track(id);
        Planner.Cart.Add(new Entry { Id = id, Qty = 1, Quality = 1 });
        _planDirty = true;
    }

    public void Add(string id) {
        var e = Planner.Cart.FirstOrDefault(x => x.Id == id);
        if (e != null) e.Qty++;
        else Planner.Cart.Add(new Entry { Id = id, Qty = 1, Quality = 1 });
        _planDirty = true;
    }

    /* ————————————————————————— список и итог ————————————————————————— */

    void RefreshPlan() {
        ShowView();
        UiKit.MarkIcon(_coalBtn, Planner.CountCoal);
        UiKit.MarkIcon(_woodCoalBtn, Planner.BurnCoalFromWood);
        UiKit.MarkIcon(_ownedBtn, Planner.UseOwned);

        MarkPin(_pinPlan, Pin.Plan);
        MarkPin(_pinGather, Pin.Gather);
        if (_planEdge != null) _planEdge.color = Pin.Plan ? UiKit.Gold : Color.clear;
        if (_gatherEdge != null) _gatherEdge.color = Pin.Gather ? UiKit.Gold : Color.clear;
        if (_snapshotBtn != null) {
            if (_snapshotBtn.gameObject.activeSelf != Pin.Gather) _snapshotBtn.gameObject.SetActive(Pin.Gather);
            Mark(_snapshotBtn, Pin.Stale || (Pin.HasSnapshot && Pin.Boxes == 0));
        }
        ShowSnapshotLine();
        // Закреплённый план и есть план: любая правка здесь меняет подсказку.
        Ui.PinHud.Invalidate();
        // Подсказка на экране считает по тому же переключателю, что и окно:
        // два разных разложения одного заказа сбивали бы с толку сильнее,
        // чем отсутствие у неё собственной настройки.
        if (Pin.RawView != _rawView) { Pin.RawView = _rawView; Ui.PinHud.Invalidate(); }

        // Под строкой тотема — его постройки по видам. Строки из двух пулов,
        // поэтому порядок в списке ставим руками.
        int slot = 0, sub = 0, units = 0;
        var owned = Planner.UseOwned ? Stock.Owned() : null;
        _open.RemoveWhere(x => !Planner.Cart.Contains(x));
        for (int i = 0; i < Planner.Cart.Count; i++) {
            if (i >= _cartRows.Count) _cartRows.Add(new CartRow(_cart, this));
            var e = Planner.Cart[i];
            _cartRows[i].Show(e, _open.Contains(e),
                              Planner.MadeOf(e, owned).Count(h => h > 0));
            _cartRows[i].Go.transform.SetSiblingIndex(slot++);
            // Раскрытая строка — по ряду на каждый экземпляр: какой уже есть
            // и на каком уровне.
            if (_open.Contains(e) && Unfolds(GameData.Get(e.Id)))
                for (int k = 0; k < e.Qty; k++) {
                    if (units >= _unitRows.Count) _unitRows.Add(new UnitRow(_cart, this));
                    _unitRows[units].Show(e, k, Planner.MadeOf(e, owned));
                    _unitRows[units++].Go.transform.SetSiblingIndex(slot++);
                }
            if (!PlanTotems.IsTotem(e.Id)) continue;
            var groups = PlanTotems.Groups(e.Id).Where(g => g.Need.Count > 0).ToList();
            int shown = groups.Count > TotemPieceRow.Max ? TotemPieceRow.Max - 1 : groups.Count;
            int lines = shown < groups.Count ? shown + 1 : shown;   // + «и ещё N»
            for (int k = 0; k < lines; k++) {
                if (sub >= _pieceRows.Count) _pieceRows.Add(new TotemPieceRow(_cart));
                if (k < shown) _pieceRows[sub].Show(groups[k]);
                else _pieceRows[sub].ShowMore(groups.Count - shown);
                _pieceRows[sub++].Go.transform.SetSiblingIndex(slot++);
            }
        }
        for (int i = Planner.Cart.Count; i < _cartRows.Count; i++) _cartRows[i].Hide();
        for (int i = sub; i < _pieceRows.Count; i++) _pieceRows[i].Hide();
        for (int i = units; i < _unitRows.Count; i++) _unitRows[i].Hide();

        // Материалы полосами: сундуки (из снимка, а пока не закреплено, сундуки
        // рядом), рюкзак и что ещё добыть. Больше всего добывать сверху,
        // добытое внизу и тусклое.
        var mats = Pin.Breakdown(Pin.Gather && Pin.HasSnapshot)
            .OrderBy(r => r.Done)
            .ThenByDescending(r => r.Left)
            .ThenBy(r => GameData.NameOf(r.Id))
            .ToList();
        for (int i = 0; i < mats.Count; i++) {
            if (i >= _materialRows.Count) _materialRows.Add(new MaterialRow(_totals));
            _materialRows[i].Show(mats[i], BuyNote(mats[i].Id));
            _materialRows[i].Go.transform.SetSiblingIndex(i);
        }
        for (int i = mats.Count; i < _materialRows.Count; i++) _materialRows[i].Hide();

        var plan = Planner.Process(Planner.Refine());
        var notes = new List<TotalRow.Data>();
        AddStations(notes);
        if (plan.Count > 0) {
            foreach (var row in plan) {
                notes.Add(new TotalRow.Data {
                    Icon = GameData.StationIcon(row.Station),
                    Name = GameData.StationName(row.Station),
                    Value = row.Units + L.Pieces + Planner.Time(row.Seconds)
                          + (row.Loads > 0 ? L.Loads + row.Loads : ""),
                });
            }
        }
        // Станки и переработка: плашками над списком. Заголовки разделов не
        // нужны, плашку узнают по значку станка.
        var cards = notes.Where(d => !d.Header).ToList();
        for (int i = 0; i < cards.Count; i++) {
            if (i >= _cards.Count) _cards.Add(new StationCard(_strip));
            _cards[i].Show(cards[i]);
        }
        for (int i = cards.Count; i < _cards.Count; i++) _cards[i].Hide();
    }

    /// <summary>
    /// Раздел «Станки»: у чего именно придётся стоять и где оно.
    ///
    /// Раньше окно называло станок в строке каталога и на этом умолкало. Для
    /// арбалета это «Чёрная кузница» — и дальше игрок сам вспоминает, есть ли
    /// она у него, какого уровня и в какой стороне. Всё это мод знает.
    ///
    /// Нужный уровень считается по самой дорогой позиции плана: улучшение до
    /// второго качества требует станка на ступень выше, и узнать об этом,
    /// собрав материалы, — обидно.
    /// </summary>
    /// <summary>
    /// Приписка «у кого и почём» к названию покупного сырья.
    ///
    /// Разметкой в самой подписи, а не отдельным полем строки: половинка итогов
    /// и так тесная, а приписка нужна редко — у двух десятков позиций из
    /// четырёхсот. Своя колонка ради них отняла бы место у всех остальных.
    ///
    /// Звёздочка у запертых товаров значит «появится на прилавке не сразу».
    /// Переводить ключ в имя босса мод не берётся: половина ключей у торговцев
    /// не боссовые.
    /// </summary>
    static string BuyNote(string id) {
        Trade.Offer o;
        if (!Trade.TryGet(id, out o)) return "";
        return "  <size=72%><color=#8A8172>" + o.Trader + " · " + o.Price
             + (string.IsNullOrEmpty(o.Key) ? "" : " *") + "</color></size>";
    }

    /// <summary>До скольких метров показывать стрелку направления.</summary>
    const float ArrowRange = 150f;

    void AddStations(List<TotalRow.Data> notes) {
        // Что за станки нужны и какого уровня.
        var need = new Dictionary<string, int>();
        foreach (var e in Planner.Cart) {
            var it = GameData.Get(e.Id);
            if (it == null || string.IsNullOrEmpty(it.Station)) continue;
            int lvl = it.MinStationLevel + Mathf.Max(1, e.Quality) - 1;
            int had;
            if (!need.TryGetValue(it.Station, out had) || lvl > had)
                need[it.Station] = lvl;
            // Под тотемом — постройки разных станков, и нужны все.
            if (it.MoreStations != null)
                foreach (var s in it.MoreStations)
                    if (!need.ContainsKey(s)) need[s] = 1;
        }
        if (need.Count == 0) return;

        var found = Stations.Distances();
        notes.Add(new TotalRow.Data { Name = L.StationsNeeded, Header = true });

        foreach (var kv in need.OrderBy(k => GameData.StationName(k.Key))) {
            var row = new TotalRow.Data {
                Icon = GameData.StationIcon(kv.Key),
                Name = GameData.StationName(kv.Key),
            };
            Stations.Found at;
            if (found.TryGetValue(kv.Key, out at)) {
                row.Value = Mathf.RoundToInt(at.Distance) + L.Metres
                          + "  " + L.LevelShort + at.Level;
                // Дальше полутораста метров стрелка бесполезна: на таком
                // расстоянии между «туда» и «туда же, но правее» — минута ходу
                // не в ту сторону.
                if (at.Distance <= ArrowRange) row.Bearing = at.Bearing;
                // Стоит, но не дорос: собирать материалы под улучшение, которое
                // некому сделать, — то же самое, что не иметь станка вовсе.
                if (at.Level < kv.Value) {
                    row.Value += "  (" + L.NeedLevel + kv.Value + ")";
                    row.Muted = true;
                }
            } else {
                row.Value = L.NotNearby;
                row.Muted = true;
                var pieceId = GameData.PieceId(kv.Key);
                if (GameData.Get(pieceId) != null) {
                    row.Action = L.AddToPlan;
                    row.OnAction = () => Add(pieceId);
                }
            }
            notes.Add(row);
        }
    }

    public void MarkDirty() { _planDirty = true; }

    /// <summary>Строка над материалами: откуда взяты сундуки.</summary>
    void ShowSnapshotLine() {
        if (_snapshotLine == null) return;
        bool field = Pin.Gather && Pin.HasSnapshot && Pin.Boxes == 0;
        if (_snapshotWarnRow.activeSelf != field) _snapshotWarnRow.SetActive(field);
        if (field) {
            _snapshotWarn.text = L.SnapshotEmpty(Pin.At, Pin.DistanceFromSnapshot());
            _snapshotLine.text = "";
            return;
        }
        if (Pin.Gather && Pin.HasSnapshot) {
            _snapshotLine.text = L.SnapshotLine(Pin.Boxes, Pin.At, Pin.DistanceFromSnapshot(),
                                                AutoSnapshot.LastWasAuto);
        } else if (Pin.Gather) {
            _snapshotLine.text = L.PinStale;
        } else {
            int boxes;
            Stock.ChestsOnly(out boxes);
            _snapshotLine.text = L.ChestsNearby(boxes);
        }
    }

    /// <summary>
    /// Раскрывается ли строка на экземпляры. Только у того, что улучшают:
    /// «уже есть на первом уровне» у еды или стрел ничего не значит — их
    /// запас и так считает рюкзак с сундуками. И не у того, что крафтится
    /// пачкой: у пачки нет отдельных экземпляров.
    /// </summary>
    static bool Unfolds(ItemDef it) {
        return it != null && !it.Totem && it.HasRecipe && it.MaxQuality > 1 && it.Out <= 1;
    }

    void ToggleOpen(Entry e) {
        if (!_open.Remove(e)) _open.Add(e);
        MarkDirty();
    }

    /* ————————————————————————— настройки ————————————————————————— */

    /// <summary>
    /// Настройки поверх окна, а не отдельным окном: своё окно пришлось бы
    /// таскать, закрывать и держать поверх планировщика, а показать тут нужно
    /// два переключателя.
    /// </summary>
    void ShowSettings() {
        if (_settings == null) BuildSettings();
        Progress.Refresh();
        RefreshSettings();
        _settings.SetActive(true);
    }

    void HideSettings() {
        if (_settings != null) _settings.SetActive(false);
        Search(force: true);
    }

    /// <summary>
    /// Настройки одной колонкой, разделами по смыслу: каталог, хранилище,
    /// закреплённая панель, общее. Слева что это, справа чем менять; пояснения
    /// в подсказках, а не строками под кнопками. Закрывается крестиком.
    /// </summary>
    void BuildSettings() {
        var box = Overlay("settings", 520f, 640f);
        _settings = box.parent.gameObject;

        var head = UiKit.Row(box, "title", 32);
        UiKit.Horizontal(head, 8);
        var title = UiKit.Label(head, "title", L.Settings, 22, UiKit.Gold);
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        UiKit.Button(head, L.Info, 96, 30, ShowAbout, 13);
        var close = UiKit.Button(head, "×", 30, 30, HideSettings, 18);
        UiKit.Tip(close, L.Close);

        /* Каталог. */
        SettingsSection(box, L.SectionCatalogue);
        // Три режима: три кнопки в ряд, а не переключатель по кругу: по
        // кругу не видно, что вариантов больше двух и какой будет следующим.
        var modes = SettingsRow(box, L.SpoilersTitle, null, null);
        foreach (Progress.Mode mode in System.Enum.GetValues(typeof(Progress.Mode))) {
            var m = mode;
            var mb = UiKit.Button(modes, L.ModeName(m), 104, 26, () => {
                Progress.Current = m;
                if (OnSpoilersChanged != null) OnSpoilersChanged(m);
                Progress.Refresh();
                RefreshSettings();
            }, 13);
            UiKit.Tip(mb, L.ModeName(m), L.ModeHint(m));
            _modeBtns.Add(mb);
        }
        _openLine = UiKit.Label(UiKit.Row(box, "open", 18), "text", "", 13, UiKit.Dim);
        UiKit.Stretch((RectTransform)_openLine.transform);
        _cardSw = new UiKit.Switch(SettingsRow(box, L.CardShow, L.CardShow, L.CardHint), () => {
            ItemCard.Enabled = !ItemCard.Enabled;
            if (OnCardChanged != null) OnCardChanged(ItemCard.Enabled, ItemCard.Compare);
            RefreshSettings();
        });
        _compareSw = new UiKit.Switch(SettingsRow(box, L.CardCompare, L.CardCompare, L.CardHint), () => {
            ItemCard.Compare = !ItemCard.Compare;
            if (OnCardChanged != null) OnCardChanged(ItemCard.Enabled, ItemCard.Compare);
            RefreshSettings();
        });

        /* Хранилище. */
        SettingsSection(box, L.SectionStorage);
        _chestSw = new UiKit.Switch(SettingsRow(box, L.UseChests, L.UseChests, L.UseChestsHint), () => {
            Chests = !Chests;
            if (OnChestsChanged != null) OnChestsChanged(Chests);
            RefreshSettings();
        });
        // Радиус снимка хранилища: шагами по десять метров.
        var radius = SettingsRow(box, L.SnapshotRange, L.SnapshotRange, L.UpdateSnapshotText);
        UiKit.Button(radius, "−", 28, 26, () => StepRadius(-10f), 15);
        _radiusValue = UiKit.Label(radius, "value", "", 15, UiKit.Gold, TextAlignmentOptions.Center);
        _radiusValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
        UiKit.Button(radius, "+", 28, 26, () => StepRadius(10f), 15);
        // Автоснимок дома: выключен, пока игрок сам не включит.
        _autoSw = new UiKit.Switch(SettingsRow(box, L.AutoSnapshot, L.AutoSnapshot, L.AutoSnapshotText), () => {
            AutoSnapshot.Enabled = !AutoSnapshot.Enabled;
            AutoSnapshot.Reset();
            if (OnAutoSnapshotChanged != null) OnAutoSnapshotChanged(AutoSnapshot.Enabled);
            RefreshSettings();
        });
        var home = SettingsRow(box, "", L.SetHome, L.SetHomeText);
        _homeLine = home.Find("label").GetComponent<TextMeshProUGUI>();
        _homeLine.color = UiKit.Dim;
        _homeLine.fontSize = 13;
        var setHome = UiKit.Button(home, L.SetHome, 190, 26, () => {
            AutoSnapshot.SetHomeHere();
            RefreshSettings();
        }, 13);
        UiKit.Tip(setHome, L.SetHome, L.SetHomeText);

        /* Закреплённая панель. */
        SettingsSection(box, L.SectionPinned);
        var style = SettingsRow(box, L.PinnedStyle, null, null);
        _numbersBtn = UiKit.Button(style, L.StyleNumbers, 96, 26, () => SetPinBars(false), 13);
        _barsBtn = UiKit.Button(style, L.StyleBars, 96, 26, () => SetPinBars(true), 13);

        /* Общее. */
        SettingsSection(box, L.SectionGeneral);
        var lang = SettingsRow(box, L.Language, null, null);
        _langEn = UiKit.Button(lang, "English", 96, 26, () => SetLanguage(false), 13);
        _langRu = UiKit.Button(lang, "Русский", 96, 26, () => SetLanguage(true), 13);
    }

    /// <summary>Заголовок раздела: мелко, заглавными, с чертой под ним.</summary>
    static void SettingsSection(RectTransform box, string text) {
        UiKit.Row(box, "gap", 4);
        var t = UiKit.Label(UiKit.Row(box, "section", 18), "text", text.ToUpperInvariant(), 12, UiKit.Dim);
        UiKit.Stretch((RectTransform)t.transform);
        t.characterSpacing = 6;
        var line = UiKit.Row(box, "rule", 1).gameObject.AddComponent<Image>();
        line.color = UiKit.Line;
        line.raycastTarget = false;
    }

    /// <summary>Строка настройки: подпись слева (с подсказкой, если есть),
    /// элементы справа добавляет вызывающий.</summary>
    static RectTransform SettingsRow(RectTransform box, string label, string tipTitle, string tipText) {
        var row = UiKit.Row(box, "row", 30);
        UiKit.Horizontal(row, 6, 2, 0);
        var t = UiKit.Label(row, "label", label, 15, UiKit.Ink);
        t.overflowMode = TextOverflowModes.Ellipsis;
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.minWidth = 0;
        if (!string.IsNullOrEmpty(tipTitle)) {
            t.raycastTarget = true;
            UiKit.Tip(t, tipTitle, tipText);
        }
        return row;
    }

    void SetLanguage(bool ru) {
        if (L.Ru == ru) return;
        L.Ru = ru;
        if (OnLanguageChanged != null) OnLanguageChanged(L.Ru);
        Rebuild();
    }

    /// <summary>
    /// Полоса под один элемент, прижатый влево.
    ///
    /// Нужна потому, что колонка настроек растягивает детей на всю ширину, и
    /// переключатель выходил во всю коробку — кнопка в шестьсот точек читается
    /// как заголовок, а не как то, что нажимают.
    /// </summary>
    static RectTransform Line(RectTransform parent) {
        var row = UiKit.Row(parent, "line", 30);
        UiKit.Horizontal(row, 8);
        return row;
    }

    static TextMeshProUGUI Hint(RectTransform parent, string text) {
        var t = UiKit.Label(parent, "hint", text, 13, UiKit.Dim);
        t.textWrappingMode = TextWrappingModes.Normal;
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 34;
        le.flexibleHeight = 0;
        return t;
    }

    const float MinRadius = 20f, MaxRadius = 150f;

    void StepRadius(float by) {
        float next = Mathf.Clamp(Mathf.Round((Stock.SnapshotRadius + by) / 10f) * 10f, MinRadius, MaxRadius);
        if (Mathf.Approximately(next, Stock.SnapshotRadius)) return;
        Stock.SnapshotRadius = next;
        if (OnSnapshotRadiusChanged != null) OnSnapshotRadiusChanged(next);
        RefreshSettings();
        _planDirty = true;
    }

    void SetPinBars(bool bars) {
        Ui.PinHud.Bars = bars;
        if (OnPinBarsChanged != null) OnPinBarsChanged(bars);
        Ui.PinHud.Invalidate();
        RefreshSettings();
    }

    void RefreshSettings() {
        for (int i = 0; i < _modeBtns.Count; i++)
            Mark(_modeBtns[i], (int)Progress.Current == i);
        if (_modeHint != null) _modeHint.text = L.ModeHint(Progress.Current);
        if (_chestSw != null) _chestSw.Set(Chests);
        Mark(_numbersBtn, !Ui.PinHud.Bars);
        Mark(_barsBtn, Ui.PinHud.Bars);
        if (_radiusValue != null) _radiusValue.text = Mathf.RoundToInt(Stock.SnapshotRadius) + L.Metres;
        if (_autoSw != null) _autoSw.Set(AutoSnapshot.Enabled);
        Mark(_langEn, !L.Ru);
        Mark(_langRu, L.Ru);
        if (_homeLine != null) {
            Vector3 h;
            var me = Player.m_localPlayer;
            if (!AutoSnapshot.TryHome(out h)) _homeLine.text = L.HomeNone;
            else {
                int m = me != null ? Mathf.RoundToInt(Vector3.Distance(me.transform.position, h)) : -1;
                _homeLine.text = (AutoSnapshot.Home.HasValue ? L.HomeSet : L.HomeBed)
                               + (m >= 0 ? " · " + m + L.Metres : "");
            }
        }
        if (_cardSw != null) _cardSw.Set(ItemCard.Enabled);
        if (_compareSw != null) _compareSw.Set(ItemCard.Enabled && ItemCard.Compare);
        if (_openLine != null) {
            switch (Progress.Current) {
                case Progress.Mode.Discovered:
                    _openLine.text = L.RecipesKnown + Progress.KnownCount
                                   + L.OfNine + Progress.KnownTotal;
                    break;
                case Progress.Mode.Bosses:
                    _openLine.text = L.BiomesOpen + Progress.OpenCount
                                   + L.OfNine + Tiers.Ids.Length;
                    break;
                default:
                    _openLine.text = "";
                    break;
            }
        }
    }

    /* ————————————————————————— инфо ————————————————————————— */

    void ShowAbout() {
        if (_about == null) BuildAbout();
        _about.SetActive(true);
    }

    void BuildAbout() {
        var box = Overlay("about", 520f, 260f);
        _about = box.parent.gameObject;

        UiKit.Label(box, "title", L.Title + "  " + Forgeplan.Build.Version, 22, UiKit.Gold)
             .gameObject.AddComponent<LayoutElement>().minHeight = 28;

        // Чей это мод: форк ForgePlanner от Biozip. Ссылки словами, без
        // значков чужих сайтов: оригинал, его сайт и исходники форка.
        UiKit.Label(UiKit.Row(box, "author", 26), "name", L.ForkOf, 16, UiKit.Ink);

        Hint(box, L.DataLine);

        var row = UiKit.Row(box, "links", 34);
        UiKit.Horizontal(row, 10);
        TextLink(row, L.LinkOriginal, Links.Original);
        TextLink(row, L.SiteOriginal, Links.Site);
        TextLink(row, L.LinkSource, Links.Source);
        UiKit.Rect(row, "spacer").gameObject
             .AddComponent<LayoutElement>().flexibleWidth = 1;

        var foot = UiKit.Row(box, "foot", 32);
        UiKit.Horizontal(foot, 8);
        var ver = UiKit.Label(foot, "game", L.GameVersion + GameVersionString(),
                              13, UiKit.Dim);
        ver.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        UiKit.Button(foot, L.Close, 120, 30, () => _about.SetActive(false));
    }


    /// <summary>Ссылка словом: спросить и открыть.</summary>
    void TextLink(RectTransform parent, string title, string url) {
        UiKit.ButtonAuto(parent, title, 30, () => AskLink(title, url), 14);
    }

    /// <summary>
    /// Версия самой игры. Спрашивают её не из любопытства: расчёт мода держится
    /// на данных конкретной сборки Valheim, и в жалобе «числа не сходятся»
    /// первым делом нужно знать, о какой игре речь.
    /// </summary>
    static string GameVersionString() {
        try {
            return global::Version.GetVersionString(false);
        } catch {
            return "?";
        }
    }

    /* —————————————————————— подтверждение перехода —————————————————————— */

    GameObject _ask;
    TextMeshProUGUI _askWhere;
    string _askUrl;

    /// <summary>
    /// Спросить, прежде чем уводить игрока из игры.
    ///
    /// Показываем сам адрес: «перейти на сайт» без адреса — это то, чему
    /// доверять не за что, а мод уже стоит с полными правами. Пусть видно,
    /// куда именно.
    /// </summary>
    void AskLink(string title, string url) {
        if (_ask == null) BuildAsk();
        _askUrl = url;
        _askWhere.text = title + "\n<color=#B9AE99>" + url + "</color>";
        _ask.SetActive(true);
    }

    void BuildAsk() {
        var box = Overlay("ask", 520f, 216f);
        _ask = box.parent.gameObject;

        UiKit.Label(box, "title", L.OpenLink, 20, UiKit.Gold)
             .gameObject.AddComponent<LayoutElement>().minHeight = 28;

        _askWhere = UiKit.Label(box, "where", "", 15, UiKit.Ink);
        _askWhere.textWrappingMode = TextWrappingModes.Normal;
        _askWhere.gameObject.AddComponent<LayoutElement>().minHeight = 46;

        Hint(box, L.LinkLeaves);

        var foot = UiKit.Row(box, "foot", 34);
        UiKit.Horizontal(foot, 8);
        UiKit.Rect(foot, "spacer").gameObject
             .AddComponent<LayoutElement>().flexibleWidth = 1;
        UiKit.Button(foot, L.Cancel, 120, 30, () => _ask.SetActive(false));
        UiKit.Button(foot, L.Open, 120, 30, () => {
            _ask.SetActive(false);
            if (string.IsNullOrEmpty(_askUrl)) return;
            ForgeplanPlugin.Log.LogInfo("открываю ссылку: " + _askUrl);
            Application.OpenURL(_askUrl);
        });
    }

    /// <summary>
    /// Завеса с коробкой посередине. Возвращает коробку — в неё складывают
    /// содержимое; завеса лежит на её родителе.
    /// </summary>
    RectTransform Overlay(string name, float w, float h) {
        var veil = UiKit.Rect(_window, name);
        UiKit.Stretch(veil);
        veil.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // Непрозрачная для лучей завеса: без неё кнопки под оверлеем остаются
        // нажимаемыми, а окно выглядит сломанным.
        var block = veil.gameObject.AddComponent<Image>();
        block.color = new Color(0.06f, 0.06f, 0.05f, 0.72f);
        block.raycastTarget = true;

        var box = UiKit.Rect(veil, "box");
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(w, h);
        UiKit.Box(box, "bg", UiKit.PanelSprite, UiKit.PanelSprite != null
            ? Charcoal : new Color(0.13f, 0.12f, 0.11f, 0.96f));
        UiKit.Box(box, "veil", null, Veil);
        UiKit.Frame(box, UiKit.Line);

        var col = box.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(22, 22, 16, 16);
        col.spacing = 8;
        col.childForceExpandHeight = false;
        col.childForceExpandWidth = true;
        col.childControlHeight = true;
        col.childControlWidth = true;
        return box;
    }

    /* ————————————————————————— строки ————————————————————————— */

    class CatalogRow {
        readonly GameObject _go;
        readonly Image _icon;
        readonly TextMeshProUGUI _name, _station;
        string _id;

        public CatalogRow(Transform parent, PlannerPanel owner) {
            var rt = UiKit.Row(parent, "row", 30);
            _go = rt.gameObject;
            UiKit.Horizontal(rt, 8, 4, 4);
            _icon = UiKit.Icon(rt, null, 26);
            _name = UiKit.Label(rt, "name", "", 16, UiKit.Ink);
            _name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            _station = UiKit.Label(rt, "station", "", 12, UiKit.Dim,
                                   TextAlignmentOptions.Right);
            var sle = _station.gameObject.AddComponent<LayoutElement>();
            sle.preferredWidth = 84;
            sle.minWidth = 0;
            UiKit.Button(rt, "+", 34, 26, () => { if (_id != null) owner.Add(_id); });

            // Ловушка для щелчка. Подписи и иконки лучи не ловят, поэтому без
            // прозрачной картинки на самой строке двойной щелчок попадал бы в
            // пустоту. Прокрутке она не мешает: колесо система событий отдаёт
            // ближайшему предку, который его обрабатывает, — это ScrollRect.
            var catcher = rt.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            rt.gameObject.AddComponent<DoubleClick>().Action =
                () => { if (_id != null) owner.Add(_id); };

            var hover = rt.gameObject.AddComponent<Hover>();
            hover.Enter = () => owner._card.Point(_go, () => _id, () => owner.QualityOf(_id));
            hover.Exit = () => owner._card.Leave(_go);
        }

        public void Show(ItemDef it) {
            _id = it.Id;
            _icon.sprite = it.Icon;
            _icon.color = it.Icon != null ? Color.white : Color.clear;
            _name.text = it.Shown;
            _station.text = string.IsNullOrEmpty(it.Station)
                ? L.ByHand : GameData.StationName(it.Station);
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }
    }

    class CartRow {
        readonly GameObject _go;
        readonly Image _icon;
        readonly TextMeshProUGUI _name, _qty, _noLevels;
        readonly List<Button> _quality = new List<Button>();
        readonly Button _minus, _plus;
        readonly Image _fold;
        Entry _entry;

        static readonly Color DelIdle = new Color(0.66f, 0.32f, 0.27f);
        static readonly Color DelHot = new Color(0.96f, 0.38f, 0.31f);
        static readonly Color DelBack = new Color(0.62f, 0.60f, 0.58f, 0.55f);

        /// <summary>
        /// Крестик удаления: приглушённый и красноватый, под курсором — красный.
        ///
        /// Раньше он был такой же кнопкой, как «+» рядом, и промах мимо «+»
        /// стирал строку вместе с выставленными уровнем и количеством. Теперь он
        /// отделён зазором и не спорит яркостью с кнопками, которые жмут часто;
        /// в полную силу загорается, только когда на него навели.
        /// </summary>
        static void DeleteButton(Transform parent, UnityEngine.Events.UnityAction onClick) {
            var btn = UiKit.Button(parent, "\u00D7", DelWidth, 26, onClick, 18);
            var img = btn.GetComponent<Image>();
            var label = btn.GetComponentInChildren<TMP_Text>(true);
            img.color = DelBack;
            label.color = DelIdle;
            var hover = btn.gameObject.AddComponent<Hover>();
            hover.Enter = () => { img.color = Color.white; label.color = DelHot; };
            hover.Exit = () => { img.color = DelBack; label.color = DelIdle; };
        }

        public CartRow(Transform parent, PlannerPanel owner) {
            var rt = UiKit.Row(parent, "row", 32);
            _go = rt.gameObject;
            UiKit.Horizontal(rt, 6, 4, 4);

            // Стрелка «раскрыть на экземпляры». Место под неё есть у каждой
            // строки, даже где раскрывать нечего: иначе иконки и названия
            // соседних строк стояли бы вразнобой.
            var fold = Block(rt, "fold", FoldWidth);
            _fold = UiKit.Icon(fold, Art.Get("arrow"), 12);
            _fold.color = UiKit.Dim;
            _fold.raycastTarget = true;
            var foldBtn = _fold.gameObject.AddComponent<Button>();
            foldBtn.transition = Selectable.Transition.None;
            foldBtn.onClick.AddListener(() => { if (_entry != null) owner.ToggleOpen(_entry); });
            var foldHover = _fold.gameObject.AddComponent<Hover>();
            foldHover.Enter = () => _fold.color = UiKit.Gold;
            foldHover.Exit = () => _fold.color = UiKit.Dim;

            _icon = UiKit.Icon(rt, null, 26);
            _name = UiKit.Label(rt, "name", "", 16, UiKit.Ink);
            var nle = _name.gameObject.AddComponent<LayoutElement>();
            nle.flexibleWidth = 1;
            nle.minWidth = 0;

            // Три блока фиксированной ширины с зазорами: уровень, количество,
            // удаление. Ширина держится, даже когда блок пуст, — иначе у вещи
            // без уровней счётчик уезжал влево, и столбцы разных строк стояли
            // вразнобой.
            Gap(rt, BlockGap);
            var levels = Block(rt, "levels", LevelsWidth);
            // Кнопки качества создаём на максимум сразу: у самой «глубокой»
            // вещи в игре четыре уровня, а прятать лишние дешевле, чем
            // пересобирать строку при смене предмета.
            for (int q = 1; q <= 4; q++) {
                int level = q;
                _quality.Add(UiKit.Button(levels, q.ToString(), 28, 26, () => {
                    if (_entry != null) { _entry.Quality = level; owner.MarkDirty(); }
                }, 14));
                UiKit.Tip(_quality[_quality.Count - 1], L.TipLevel(level));
            }
            // У вещи без уровней вместо пустоты — прочерк: пустое место под
            // подписью «Уровень» выглядело как недорисованная строка.
            _noLevels = UiKit.Label(levels, "none", "\u2014", 16, UiKit.Faint,
                                    TextAlignmentOptions.Center);
            _noLevels.gameObject.AddComponent<LayoutElement>().preferredWidth = LevelsWidth;

            Gap(rt, BlockGap);
            var qty = Block(rt, "qty", QtyWidth);
            _minus = UiKit.Button(qty, "\u2212", 32, 26, () => {
                if (_entry != null && _entry.Qty > 1) {
                    _entry.Qty--;
                    _entry.Fit();
                    owner.MarkDirty();
                }
            });
            _qty = UiKit.Label(qty, "qty", "", 17, UiKit.Ink, TextAlignmentOptions.Center);
            _qty.gameObject.AddComponent<LayoutElement>().preferredWidth = 36;
            _plus = UiKit.Button(qty, "+", 32, 26, () => {
                if (_entry != null) { _entry.Qty++; owner.MarkDirty(); }
            });

            Gap(rt, BlockGap);
            DeleteButton(rt, () => {
                if (_entry != null) { Planner.Cart.Remove(_entry); owner.MarkDirty(); }
            });

            // Прозрачная подложка ловит курсор между кнопками: без неё наведение
            // срабатывало бы только над ними, а карточка мигала бы на пробелах.
            var catcher = rt.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            var hover = rt.gameObject.AddComponent<Hover>();
            hover.Enter = () => owner._card.Point(_go,
                () => _entry != null ? _entry.Id : null,
                () => _entry != null ? _entry.Quality : 1);
            hover.Exit = () => owner._card.Leave(_go);
        }

        public void Show(Entry e, bool open, int made) {
            _entry = e;
            var it = GameData.Get(e.Id);
            _icon.sprite = it != null ? it.Icon : null;
            _icon.color = _icon.sprite != null ? Color.white : Color.clear;
            _name.text = it != null ? it.Shown : e.Id;
            if (made > 0)
                _name.text += "  <size=80%><color=#8A8172>" + L.MadeSummary(made) + "</color></size>";

            bool folds = Unfolds(it);
            if (_fold.gameObject.activeSelf != folds) _fold.gameObject.SetActive(folds);
            // Стрелка смотрит вверх; свёрнуто — вправо, раскрыто — вниз.
            if (folds) _fold.transform.localRotation = Quaternion.Euler(0f, 0f, open ? 180f : -90f);
            _qty.text = e.Qty.ToString();

            int max = it != null ? it.MaxQuality : 1;
            for (int q = 0; q < _quality.Count; q++) {
                bool on = q < max && max > 1;
                _quality[q].gameObject.SetActive(on);
                if (on) Mark(_quality[q], e.Quality == q + 1);
            }
            _noLevels.gameObject.SetActive(max <= 1);
            // Тотем в двух экземплярах — не заказ, а ошибка счёта: его состав
            // и так всё, что ему нужно.
            bool counted = it == null || !it.Totem;
            _minus.gameObject.SetActive(counted);
            _plus.gameObject.SetActive(counted);
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }

        public GameObject Go { get { return _go; } }
    }

    /// <summary>
    /// Один экземпляр раскрытой строки: «№2 · есть, ур. 1» и кнопки уровней.
    ///
    /// Кнопки стоят ровно под кнопками уровня у самой строки, и их столько,
    /// сколько нужно по плану: у мечей второго уровня — 1 и 2. Нажатая —
    /// «этот уже есть на таком уровне», нажатая на целевом — «готов». Нажать
    /// отмеченную ещё раз — снять отметку: экземпляра ещё нет.
    /// </summary>
    class UnitRow {
        readonly GameObject _go;
        readonly TextMeshProUGUI _name;
        readonly List<Button> _levels = new List<Button>();
        Entry _entry;
        int _index;

        public GameObject Go { get { return _go; } }

        public UnitRow(Transform parent, PlannerPanel owner) {
            var rt = UiKit.Row(parent, "unit", 26);
            _go = rt.gameObject;
            UiKit.Horizontal(rt, 6, 4, 4);
            Gap(rt, FoldWidth);
            Gap(rt, 26);
            _name = UiKit.Label(rt, "name", "", 14, UiKit.Dim);
            var nle = _name.gameObject.AddComponent<LayoutElement>();
            nle.flexibleWidth = 1;
            nle.minWidth = 0;

            Gap(rt, BlockGap);
            var levels = Block(rt, "levels", LevelsWidth);
            for (int q = 1; q <= 4; q++) {
                int level = q;
                _levels.Add(UiKit.Button(levels, q.ToString(), 28, 22, () => {
                    // Со «Своим» уровни приходят из сундуков, отметки руками
                    // ничего бы не поменяли, поэтому молча ждём, пока его выключат.
                    if (_entry == null || Planner.UseOwned) return;
                    while (_entry.Made.Count <= _index) _entry.Made.Add(0);
                    _entry.Made[_index] = Mathf.Min(_entry.Made[_index], _entry.Quality) == level
                        ? 0 : level;
                    owner.MarkDirty();
                }, 13));
            }
            Gap(rt, BlockGap);
            Gap(rt, QtyWidth);
            Gap(rt, BlockGap);
            Gap(rt, DelWidth);
        }

        /// <param name="made">Уровни, по которым считает план: Entry.Made или
        /// найденное со «Своим», см. Planner.MadeOf.</param>
        public void Show(Entry e, int index, List<int> made) {
            _entry = e;
            _index = index;
            int target = Mathf.Max(1, e.Quality);
            int had = index < made.Count ? Mathf.Min(made[index], target) : 0;
            _name.text = "\u2116" + (index + 1) + "  \u00B7  "
                + (had <= 0 ? L.UnitNew : had >= target ? L.UnitDone : L.UnitHas(had))
                + (Planner.UseOwned && had > 0 ? "  <size=80%>" + L.UnitFound + "</size>" : "");
            for (int q = 0; q < _levels.Count; q++) {
                bool on = q < target;
                _levels[q].gameObject.SetActive(on);
                if (on) Mark(_levels[q], had == q + 1);
            }
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }
    }

    /// <summary>
    /// Вид построек под строкой тотема: «Костёр ×2 · 4 дерева, 10 камня».
    /// Только для чтения — кнопок нет, количеством правит сам тотем.
    /// Отступ слева ставит иконку под название тотема, чтобы было видно,
    /// чьи это строки.
    /// </summary>
    class TotemPieceRow {
        /// <summary>Больше строк на один тотем не показываем: у чертежа базы
        /// бывает три десятка видов, и они вытеснили бы весь план.</summary>
        public const int Max = 8;

        readonly GameObject _go;
        readonly Image _icon;
        readonly TextMeshProUGUI _name, _mats;

        public GameObject Go { get { return _go; } }

        public TotemPieceRow(Transform parent) {
            var rt = UiKit.Row(parent, "totem-piece", 22);
            _go = rt.gameObject;
            UiKit.Horizontal(rt, 6, NameIndent, 4 + (int)(DelWidth + BlockGap));
            _icon = UiKit.Icon(rt, null, 18);
            _name = UiKit.Label(rt, "name", "", 14, UiKit.Dim);
            var nle = _name.gameObject.AddComponent<LayoutElement>();
            nle.flexibleWidth = 1;
            nle.minWidth = 0;
            _mats = UiKit.Label(rt, "mats", "", 13, UiKit.Dim, TextAlignmentOptions.Right);
            var mle = _mats.gameObject.AddComponent<LayoutElement>();
            mle.preferredWidth = LevelsWidth + QtyWidth + BlockGap;
            mle.minWidth = 60;
        }

        public void Show(PlanTotems.Group g) {
            _icon.sprite = g.Icon;
            _icon.color = g.Icon != null ? Color.white : Color.clear;
            _name.text = g.Count > 1 ? g.Name + " ×" + g.Count : g.Name;
            // Сначала то, чего нужно больше: оно и определяет вылазку.
            _mats.text = string.Join(", ", g.Need
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => GameData.NameOf(kv.Key))
                .Select(kv => kv.Value + " " + GameData.NameOf(kv.Key)));
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void ShowMore(int rest) {
            _icon.sprite = null;
            _icon.color = Color.clear;
            _name.text = L.TotemMore(rest);
            _mats.text = "";
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }
    }

    /// <summary>Строка во всю ширину: заголовок «Переработка» и строки
    /// станков — у них длинное значение, в половину ширины оно не влезает.</summary>
    class TotalRow {
        public struct Data {
            public Sprite Icon;
            public string Name, Value;
            public bool Header;
            /// <summary>Есть — в строке появляется кнопка с этой подписью.</summary>
            public string Action;
            public System.Action OnAction;
            /// <summary>Приглушить строку: станка поблизости нет.</summary>
            public bool Muted;
            /// <summary>Угол до цели от направления взгляда. null — не показывать.</summary>
            public float? Bearing;
        }

        readonly GameObject _go;
        readonly Image _icon;
        readonly TextMeshProUGUI _name, _value;

        public GameObject Go { get { return _go; } }

        public TotalRow(Transform parent) {
            var rt = UiKit.Row(parent, "note", 26);
            _go = rt.gameObject;
            UiKit.Horizontal(rt, 8, 4, 6);
            _icon = UiKit.Icon(rt, null, 22);
            _name = UiKit.Label(rt, "name", "", 16, UiKit.Ink);
            _name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            // Стрелка направления. Стоит слева от числа, потому что читается
            // вместе с ним: «119 м вон туда».
            _arrow = UiKit.Icon(rt, Art.Get("arrow"), 15);
            _arrow.gameObject.SetActive(false);

            _value = UiKit.Label(rt, "value", "", 15, UiKit.Ink, TextAlignmentOptions.Right);
            var vle = _value.gameObject.AddComponent<LayoutElement>();
            vle.preferredWidth = 230;
            vle.minWidth = 60;

            // Кнопка есть всегда, но обычно спрятана: создавать её на лету
            // значит пересобирать разметку строки посреди перерисовки.
            _btn = UiKit.Button(rt, "", 150, 22, () => {
                if (_action != null) _action();
            }, 13f);
            _btn.gameObject.SetActive(false);
            _btnLabel = _btn.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        readonly Image _arrow;
        readonly Button _btn;
        readonly TextMeshProUGUI _btnLabel;
        System.Action _action;

        public void Show(Data d) {
            _icon.sprite = d.Icon;
            _icon.color = d.Icon != null ? Color.white : Color.clear;
            _name.text = d.Name;
            _name.color = d.Header ? UiKit.Gold : d.Muted ? UiKit.Dim : UiKit.Ink;
            _value.text = d.Value ?? "";
            _value.color = d.Muted ? UiKit.Dim : UiKit.Ink;

            // Без картинки стрелку не показываем вовсе: Image с пустым спрайтом
            // рисует не ничто, а сплошной прямоугольник своего цвета.
            bool hasArrow = d.Bearing.HasValue && _arrow.sprite != null;
            if (_arrow.gameObject.activeSelf != hasArrow)
                _arrow.gameObject.SetActive(hasArrow);
            if (hasArrow) {
                // Экранный поворот против часовой: у Unity положительный Z
                // крутит влево, а угол мы считали по часовой.
                _arrow.transform.localRotation =
                    Quaternion.Euler(0f, 0f, -d.Bearing.Value);
                _arrow.color = d.Muted ? UiKit.Dim : UiKit.Gold;
            }

            _action = d.OnAction;
            bool hasBtn = !string.IsNullOrEmpty(d.Action);
            if (hasBtn && _btnLabel != null) _btnLabel.text = d.Action;
            if (_btn.gameObject.activeSelf != hasBtn) _btn.gameObject.SetActive(hasBtn);
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }
    }

    /// <summary>
    /// Строка материала в окне: значок, название, полоса «сундуки, рюкзак,
    /// ещё добыть» с числами внутри и сколько нужно всего.
    /// </summary>
    class MaterialRow {
        readonly GameObject _go;
        readonly CanvasGroup _fade;
        readonly Image _icon;
        readonly TextMeshProUGUI _name, _total;
        readonly Bar _bar;

        public GameObject Go { get { return _go; } }

        public MaterialRow(Transform parent) {
            var rt = UiKit.Row(parent, "material", 26);
            _go = rt.gameObject;
            _fade = _go.AddComponent<CanvasGroup>();
            UiKit.Horizontal(rt, 8, 4, 6);
            _icon = UiKit.Icon(rt, null, 22);
            _name = UiKit.Label(rt, "name", "", 15, UiKit.Ink);
            _name.overflowMode = TextOverflowModes.Ellipsis;
            var nle = _name.gameObject.AddComponent<LayoutElement>();
            nle.preferredWidth = 230;
            nle.minWidth = 100;
            _bar = new Bar(rt, 20f, 12f);
            _total = UiKit.Label(rt, "total", "", 15, UiKit.Ink, TextAlignmentOptions.Right);
            var tle = _total.gameObject.AddComponent<LayoutElement>();
            tle.preferredWidth = 48;
            tle.minWidth = 36;
        }

        public void Show(Pin.MatRow r, string note) {
            var it = GameData.Get(r.Id);
            _icon.sprite = it != null ? it.Icon : null;
            _icon.color = _icon.sprite != null ? Color.white : Color.clear;
            _name.text = GameData.NameOf(r.Id) + note;
            _bar.Set(r.Chests, r.Bag, Mathf.Max(0, r.Left));
            _total.text = r.Total.ToString();
            _fade.alpha = r.Done ? 0.45f : 1f;
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }
    }

    /// <summary>
    /// Плашка станка или переработки: значок, название, под ним где он и
    /// какого уровня (или сколько и как долго). Нет станка или он мал:
    /// вторая строка красная, и рядом «+», чтобы добавить его в план.
    /// </summary>
    class StationCard {
        static readonly Color Warn = new Color(0.96f, 0.45f, 0.36f);

        readonly GameObject _go;
        readonly Image _icon, _arrow;
        readonly TextMeshProUGUI _name, _value;
        readonly Button _add;
        System.Action _action;

        public StationCard(Transform parent) {
            var rt = UiKit.Rect(parent, "card");
            _go = rt.gameObject;
            UiKit.Box(rt, "bg", UiKit.FieldSprite, new Color(0f, 0f, 0f, 0.45f));
            UiKit.Horizontal(rt, 8, 8, 6);
            _icon = UiKit.Icon(rt, null, 26);

            var text = UiKit.Rect(rt, "text");
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var v = text.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childAlignment = TextAnchor.MiddleLeft;
            _name = UiKit.Label(text, "name", "", 14, UiKit.Ink);
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _value = UiKit.Label(text, "value", "", 12, UiKit.Dim);
            _value.overflowMode = TextOverflowModes.Ellipsis;

            _arrow = UiKit.Icon(rt, Art.Get("arrow"), 14);
            _arrow.gameObject.SetActive(false);
            _add = UiKit.Button(rt, "+", 26, 24, () => { if (_action != null) _action(); }, 16);
            _add.gameObject.SetActive(false);
        }

        public void Show(TotalRow.Data d) {
            _icon.sprite = d.Icon;
            _icon.color = d.Icon != null ? Color.white : Color.clear;
            _name.text = d.Name;
            _value.text = d.Value ?? "";
            _value.color = d.Muted ? Warn : UiKit.Dim;

            // Без картинки стрелку не показываем: Image с пустым спрайтом
            // рисует сплошной прямоугольник.
            bool hasArrow = d.Bearing.HasValue && _arrow.sprite != null;
            if (_arrow.gameObject.activeSelf != hasArrow) _arrow.gameObject.SetActive(hasArrow);
            if (hasArrow) {
                _arrow.transform.localRotation = Quaternion.Euler(0f, 0f, -d.Bearing.Value);
                _arrow.color = d.Muted ? UiKit.Dim : UiKit.Gold;
            }

            _action = d.OnAction;
            bool hasAdd = d.OnAction != null;
            if (_add.gameObject.activeSelf != hasAdd) _add.gameObject.SetActive(hasAdd);
            if (hasAdd) UiKit.Tip(_add, d.Action ?? L.AddToPlan);
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }
    }

    /// <summary>Половинка строки итогов: иконка, название, число.</summary>
    class TotalCell {
        readonly GameObject _go;
        readonly Image _icon;
        readonly TextMeshProUGUI _name, _value;

        public TotalCell(Transform parent) {
            var rt = UiKit.Rect(parent, "cell");
            _go = rt.gameObject;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            // preferredWidth = 0 намеренно: без него половинка с длинным
            // названием требует больше места, чем соседняя, и колонки чисел
            // перестают стоять друг под другом. Так вся ширина раздаётся
            // через flexibleWidth поровну.
            le.preferredWidth = 0;
            le.flexibleWidth = 1;
            le.minWidth = 80;
            le.minHeight = le.preferredHeight = 26;
            UiKit.Horizontal(rt, 8, 4, 8);
            _icon = UiKit.Icon(rt, null, 22);
            _name = UiKit.Label(rt, "name", "", 16, UiKit.Ink);
            _name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            _value = UiKit.Label(rt, "value", "", 17, UiKit.Gold, TextAlignmentOptions.Right);
            var vle = _value.gameObject.AddComponent<LayoutElement>();
            vle.preferredWidth = 64;
            vle.minWidth = 40;
        }

        public void Show(TotalRow.Data d) {
            _icon.sprite = d.Icon;
            _icon.color = d.Icon != null ? Color.white : Color.clear;
            _name.text = d.Name;
            _value.text = d.Value ?? "";
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }
    }

    class TotalPair {
        readonly GameObject _go;
        readonly TotalCell _a, _b;

        public GameObject Go { get { return _go; } }

        public TotalPair(Transform parent) {
            var rt = UiKit.Row(parent, "pair", 26);
            _go = rt.gameObject;
            UiKit.Horizontal(rt, 10);
            _a = new TotalCell(rt);
            _b = new TotalCell(rt);
        }

        public void Show(TotalRow.Data left, TotalRow.Data? right) {
            _a.Show(left);
            if (right.HasValue) _b.Show(right.Value); else _b.Hide();
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide() { if (_go.activeSelf) _go.SetActive(false); }
    }
}

}
