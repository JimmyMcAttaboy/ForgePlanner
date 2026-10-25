using System.Collections.Generic;
using UnityEngine;

namespace Forgeplan {

/// <summary>
/// Закреплённое: то, что остаётся на экране, когда окно закрыто.
///
/// Закрепляется не копия, а сам план: подсказка на экране показывает ровно
/// то, что сейчас в окне, и меняется вместе с ним. Две части закрепляются
/// отдельно: «План» (что делаем) и «Нужно собрать» (из чего). Можно одну,
/// можно обе.
///
/// «Нужно собрать» закрепляется снимком сундуков: что лежит в сундуках рядом
/// в момент закрепления, по предметам, плюс когда и где снимок снят. Рюкзак
/// считается живым. В поле сундуков нет, а снимок их помнит; подобрал,
/// выронил, скрафтил: рюкзак показывает как есть. Вернулся домой и разложил
/// по сундукам: пересчитал (Snapshot).
///
/// Что закреплено, хранится в файле плана, см. PlanStore.
/// </summary>
public static class Pin {
    /// <summary>Закреплён список позиций плана.</summary>
    public static bool Plan;
    /// <summary>Закреплено «Нужно собрать».</summary>
    public static bool Gather;

    /// <summary>Считать ли до сырья. Зеркалит переключатель в окне.</summary>
    public static bool RawView = true;

    /* Снимок сундуков. */
    /// <summary>Сколько каждого материала лежало в сундуках рядом.</summary>
    public static readonly Dictionary<string, int> Chests = new Dictionary<string, int>();
    /// <summary>Сколько сундуков попало в счёт. Ноль: пересчитали в поле.</summary>
    public static int Boxes;
    /// <summary>Когда снят, местное время.</summary>
    public static System.DateTime At;
    /// <summary>Где стоял игрок.</summary>
    public static Vector3 Where;
    /// <summary>По какому плану снят снимок, см. Signature. Ноль: снимка нет.</summary>
    public static int TargetsOf;

    public static bool HasSnapshot { get { return TargetsOf != 0; } }

    /// <summary>Закреплённое поменялось: подсказке на экране пора пересобраться.</summary>
    public static System.Action Changed;

    static void Fire() { if (Changed != null) Changed(); }

    public static bool Any { get { return Plan || Gather; } }

    static readonly List<Entry> None = new List<Entry>();

    /// <summary>Позиции, по которым считается закреплённое: сам план, пока
    /// что-нибудь закреплено.</summary>
    public static List<Entry> Items { get { return Any ? Planner.Cart : None; } }

    public static void TogglePlan() { Plan = !Plan; Fire(); }
    public static void ToggleGather() {
        Gather = !Gather;
        if (Gather) Take(); else Forget();
        Fire();
    }

    /// <summary>Пересчитать сундуки рядом. Кнопка обновления в окне и на панели.</summary>
    public static void Snapshot() {
        AutoSnapshot.LastWasAuto = false;
        Take();
        Fire();
    }

    static void Forget() {
        Chests.Clear();
        Boxes = 0;
        TargetsOf = 0;
    }

    /// <summary>Единственное место, где снимается снимок: кнопкой, при
    /// закреплении и сам. Отсюда же перезапуск таймера автоснимка, чтобы ни
    /// один путь его не миновал.</summary>
    static void Take() {
        AutoSnapshot.Taken();
        Chests.Clear();
        int boxes;
        foreach (var kv in Stock.ChestsOnly(out boxes)) Chests[kv.Key] = kv.Value;
        Boxes = boxes;
        At = System.DateTime.Now;
        var me = Player.m_localPlayer;
        Where = me != null ? me.transform.position : Vector3.zero;
        TargetsOf = Signature();
    }

    /// <summary>Сколько метров от места снимка до игрока. -1: игрока нет.</summary>
    public static int DistanceFromSnapshot() {
        var me = Player.m_localPlayer;
        if (me == null) return -1;
        return Mathf.RoundToInt(Vector3.Distance(me.transform.position, Where));
    }

    /// <summary>Отпечаток плана на момент снимка. Сейчас служит только
    /// отметкой «снимок есть»: ноль значит снимка нет.</summary>
    public static int Signature() {
        unchecked {
            int h = RawView ? 17 : 31;
            h = h * 397 + (Planner.UseOwned ? 1 : 0);
            foreach (var e in Planner.Cart) {
                h = h * 397 + (e.Id ?? "").GetStableHashCode();
                h = h * 397 + e.Qty;
                h = h * 397 + e.Quality;
                foreach (var m in e.Made) h = h * 397 + m;
            }
            return h == 0 ? 1 : h;
        }
    }

    /// <summary>Снимка нет (закрепили без него или файл из старой версии).
    /// Правка плана снимок не портит: в нём всё содержимое сундуков, а не
    /// только то, что было в плане. Сам мод снимок не обновляет: в поле это
    /// обнулило бы сундуки, поэтому только просит.</summary>
    public static bool Stale { get { return Gather && !HasSnapshot; } }

    public static void Set(bool plan, bool gather) {
        if (Plan == plan && Gather == gather) return;
        Plan = plan;
        Gather = gather;
        if (!gather) Forget();
        Fire();
    }

    public static void Clear() { Set(false, false); }

    /// <summary>
    /// Строка «Нужно собрать»: сколько нужно всего, сколько из этого в
    /// сундуках, сколько в рюкзаке и сколько ещё добыть. Части не больше
    /// целого: лишнее в сундуке не делает полосу длиннее.
    /// </summary>
    public struct MatRow {
        public string Id;
        public int Total, Chests, Bag;
        public int Left { get { return Total - Chests - Bag; } }
        public bool Done { get { return Left <= 0; } }
    }

    /// <summary>
    /// Разбивка по материалам для окна и для панели. snapshot: сундуки из
    /// снимка, иначе сундуки рядом сейчас. Рюкзак всегда живой.
    /// </summary>
    public static List<MatRow> Breakdown(bool snapshot) {
        var rows = new List<MatRow>();
        var need = RawView ? Planner.Raw(Planner.Cart) : Planner.Direct(Planner.Cart);
        if (need.Count == 0) return rows;
        int boxes;
        var chests = snapshot ? Chests : Stock.ChestsOnly(out boxes);
        var bag = Stock.Count(false);
        foreach (var kv in need) {
            if (kv.Value <= 0) continue;
            int c, b;
            chests.TryGetValue(kv.Key, out c);
            bag.TryGetValue(kv.Key, out b);
            c = System.Math.Min(c, kv.Value);
            b = System.Math.Min(b, kv.Value - c);
            rows.Add(new MatRow { Id = kv.Key, Total = kv.Value, Chests = c, Bag = b });
        }
        return rows;
    }

    /// <summary>
    /// Что показывает панель: только то, за чем идти, то есть чего не
    /// хватило в сундуках при снимке. Добытое гаснет, а не пропадает.
    /// </summary>
    public static List<MatRow> Rows() {
        var rows = new List<MatRow>();
        if (!Gather || !HasSnapshot) return rows;
        foreach (var r in Breakdown(true))
            if (r.Total - r.Chests > 0) rows.Add(r);
        return rows;
    }
}

}
