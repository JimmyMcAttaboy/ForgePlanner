using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Forgeplan.Ui {

/// <summary>
/// Полоса «сколько где»: сундуки, рюкзак и что ещё добыть, у каждой части
/// своё число внутри. Ширина частей по количеству, пустая часть без заливки.
/// Одна на окно и на закреплённую панель, чтобы читались одинаково.
/// </summary>
public class Bar {
    public static readonly Color ChestFill = new Color(0.72f, 0.54f, 0.25f);
    public static readonly Color BagFill = new Color(0.44f, 0.60f, 0.32f);
    public static readonly Color ChestText = new Color(0.85f, 0.66f, 0.36f);
    public static readonly Color BagText = new Color(0.56f, 0.75f, 0.42f);
    static readonly Color Track = new Color(1f, 1f, 1f, 0.10f);
    static readonly Color OnFill = new Color(0.08f, 0.07f, 0.06f);

    class Seg {
        public GameObject Go;
        public LayoutElement Le;
        public TextMeshProUGUI Text;
    }

    readonly RectTransform _rt;
    readonly Seg _chests, _bag, _left;

    public RectTransform Rect { get { return _rt; } }

    public Bar(Transform parent, float height, float font) {
        _rt = UiKit.Row(parent, "bar", height);
        _rt.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1;
        var bg = _rt.gameObject.AddComponent<Image>();
        bg.color = Track;
        bg.raycastTarget = false;
        var h = _rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 0;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        _chests = Make(ChestFill, OnFill, font);
        _bag = Make(BagFill, OnFill, font);
        _left = Make(Color.clear, UiKit.Ink, font);
    }

    Seg Make(Color fill, Color ink, float font) {
        var rt = UiKit.Rect(_rt, "part");
        var img = rt.gameObject.AddComponent<Image>();
        img.color = fill;
        img.raycastTarget = false;
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minWidth = 0;
        le.preferredWidth = 0;
        var t = UiKit.Label(rt, "n", "", font, ink, TextAlignmentOptions.Center);
        UiKit.Stretch((RectTransform)t.transform);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Truncate;
        t.fontStyle = FontStyles.Bold;
        t.raycastTarget = false;
        return new Seg { Go = rt.gameObject, Le = le, Text = t };
    }

    public void Set(int chests, int bag, int left) {
        Apply(_chests, chests);
        Apply(_bag, bag);
        Apply(_left, left);
    }

    static void Apply(Seg s, int n) {
        bool on = n > 0;
        if (s.Go.activeSelf != on) s.Go.SetActive(on);
        s.Le.flexibleWidth = Mathf.Max(0, n);
        s.Text.text = n.ToString();
    }

    /// <summary>Строка-легенда: цвет и подпись того же цвета.</summary>
    public static RectTransform Legend(Transform parent, float font) {
        var row = UiKit.Row(parent, "legend", font + 6f);
        UiKit.Horizontal(row, 12);
        Key(row, ChestFill, ChestText, L.Chests, font);
        Key(row, BagFill, BagText, L.Backpack, font);
        Key(row, Track, UiKit.Dim, L.StillToGet, font);
        UiKit.Rect(row, "spacer").gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        return row;
    }

    static void Key(Transform parent, Color swatch, Color ink, string text, float font) {
        var sw = UiKit.Rect(parent, "swatch");
        var le = sw.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = font;
        le.minHeight = le.preferredHeight = font;
        var img = sw.gameObject.AddComponent<Image>();
        img.color = swatch;
        img.raycastTarget = false;
        var t = UiKit.Label(parent, "key", text, font, ink);
        t.raycastTarget = false;
        var tle = t.gameObject.AddComponent<LayoutElement>();
        tle.preferredWidth = t.GetPreferredValues(text).x + 2f;
    }
}

}
