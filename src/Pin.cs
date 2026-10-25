using System.Collections.Generic;

namespace Forgeplan {

/// <summary>
/// Закреплённое: то, что остаётся на экране, когда окно закрыто.
///
/// Закрепляется не копия, а сам план: подсказка на экране показывает ровно
/// то, что сейчас в окне, и меняется вместе с ним. Две части закрепляются
/// отдельно: «План» (что делаем) и «Нужно собрать» (из чего). Можно одну,
/// можно обе.
///
/// «Нужно собрать» закрепляется снимком: сколько не хватает в момент
/// закрепления, считая рюкзак и сундуки рядом. Дома всё лежит в сундуках, а
/// в поле их нет, и живой счёт там снова требовал бы всё целиком. Поэтому
/// недостача замораживается, а в поле считается только то, что добавилось в
/// рюкзак с момента снимка. Вернулся домой, пересчитал (Snapshot).
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
    /// <summary>Снимок недостачи: сколько не хватало (Need) и сколько этого
    /// было в рюкзаке в момент снимка (Bag).</summary>
    public class Target { public int Need, Bag; }
    public static readonly Dictionary<string, Target> Targets = new Dictionary<string, Target>();
    /// <summary>По какому плану снят снимок, см. Signature.</summary>
    public static int TargetsOf;

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
        if (Gather) Take(); else Targets.Clear();
        Fire();
    }

    /// <summary>Пересчитать недостачу: что нужно по плану минус рюкзак и
    /// сундуки рядом. Кнопка обновления в окне.</summary>
    public static void Snapshot() { Take(); Fire(); }

    static void Take() {
        Targets.Clear();
        var need = RawView ? Planner.Raw(Planner.Cart) : Planner.Direct(Planner.Cart);
        var have = Stock.Count();
        var bag = Stock.Count(false);
        foreach (var kv in need) {
            int got, inBag;
            have.TryGetValue(kv.Key, out got);
            bag.TryGetValue(kv.Key, out inBag);
            int shortBy = kv.Value - got;
            if (shortBy > 0) Targets[kv.Key] = new Target { Need = shortBy, Bag = inBag };
        }
        TargetsOf = Signature();
    }

    /// <summary>
    /// Отпечаток всего, от чего зависит недостача: позиции плана, вид и
    /// уровни уже сделанного. Не совпал со снимком: план правили после него, и подсказка
    /// просит пересчитать.
    /// </summary>
    public static int Signature() {
        unchecked {
            int h = RawView ? 17 : 31;
            foreach (var e in Planner.Cart) {
                h = h * 397 + (e.Id ?? "").GetStableHashCode();
                h = h * 397 + e.Qty;
                h = h * 397 + e.Quality;
                foreach (var m in e.Made) h = h * 397 + m;
            }
            return h;
        }
    }

    /// <summary>План правили после снимка.</summary>
    public static bool Stale { get { return Gather && TargetsOf != Signature(); } }

    public static void Set(bool plan, bool gather) {
        if (Plan == plan && Gather == gather) return;
        Plan = plan;
        Gather = gather;
        if (!gather) Targets.Clear();
        Fire();
    }

    public static void Clear() { Set(false, false); }

    /// <summary>Строка «Нужно собрать»: собрано с момента снимка из того,
    /// чего не хватало.</summary>
    public struct MatRow {
        public string Id;
        public int Have, Need;
        public int Left { get { return Need - Have; } }
        public bool Done { get { return Have >= Need; } }
    }

    /// <summary>
    /// Недостача из снимка и сколько из неё уже в рюкзаке: прибавка к тому,
    /// что лежало там при снимке. Сундуки здесь не считаются: в поле их нет,
    /// а дома для этого есть пересчёт. Собранное приходит с Have = Need, и
    /// подсказка гасит такие строки, а не прячет их.
    /// </summary>
    public static List<MatRow> Rows() {
        var rows = new List<MatRow>();
        if (!Gather || Targets.Count == 0) return rows;

        var bag = Stock.Count(false);
        foreach (var kv in Targets) {
            int now;
            bag.TryGetValue(kv.Key, out now);
            rows.Add(new MatRow {
                Id = kv.Key,
                Need = kv.Value.Need,
                Have = System.Math.Max(0, System.Math.Min(now - kv.Value.Bag, kv.Value.Need)),
            });
        }
        return rows;
    }
}

}
