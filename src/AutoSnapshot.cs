using HarmonyLib;
using UnityEngine;

namespace Forgeplan {

/// <summary>
/// Снимок хранилища сам, дома. Выключено по умолчанию.
///
/// «Дом» задаёт игрок кнопкой в настройках; не задал: точка возрождения у
/// кровати, её игра и так помнит. Снимок снимается, когда игрок входит в
/// радиус снимка вокруг дома (пешком или через портал), и потом раз в
/// несколько минут, пока он там. Вдали от дома не снимается никогда: там
/// снимок обнулил бы сундуки.
///
/// После входа ждём несколько секунд: после портала сундуки догружаются не
/// сразу, и снимок в первый же кадр увидел бы пустую базу.
///
/// Ещё снимок снимается, когда дома закрыли сундук: положили или взяли, и
/// числа сразу верные, не дожидаясь следующего раза по таймеру.
/// </summary>
public static class AutoSnapshot {
    public static bool Enabled;
    /// <summary>Как часто обновлять, пока игрок дома, в минутах.</summary>
    public static float Minutes = 2f;
    /// <summary>Дом, заданный кнопкой. null: точка возрождения у кровати.</summary>
    public static Vector3? Home;
    /// <summary>Последний снимок снят сам, а не кнопкой.</summary>
    public static bool LastWasAuto;

    const float Settle = 3f;
    /// <summary>После закрытия сундука: изменения успевают дойти.</summary>
    const float AfterChest = 1f;
    const float CheckEvery = 2f;

    static bool _inside;
    static float _check, _snapAt = -1f, _nextPeriodic;

    /// <summary>Где дом сейчас: заданный или кровать. false: ни того, ни другого.</summary>
    public static bool TryHome(out Vector3 home) {
        if (Home.HasValue) { home = Home.Value; return true; }
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        if (profile != null && profile.HaveCustomSpawnPoint()) {
            home = profile.GetCustomSpawnPoint();
            return true;
        }
        home = Vector3.zero;
        return false;
    }

    public static void SetHomeHere() {
        var me = Player.m_localPlayer;
        if (me == null) return;
        Home = me.transform.position;
        _inside = false;   // войти заново: снимок у нового дома
    }

    /// <summary>Снимок сняли (кнопкой или сам): следующий по таймеру через
    /// полный интервал, а не вдогонку.</summary>
    public static void Taken() {
        _snapAt = -1f;
        _nextPeriodic = Time.unscaledTime + Mathf.Max(0.5f, Minutes) * 60f;
    }

    /// <summary>Дома закрыли сундук: снять заново чуть погодя.</summary>
    public static void ChestClosed() {
        if (!Enabled || !Pin.Gather || !_inside) return;
        _snapAt = Time.unscaledTime + AfterChest;
    }

    public static void Reset() {
        _inside = false;
        _snapAt = -1f;
        _check = 0f;
    }

    public static void Tick() {
        if (!Enabled || !Pin.Gather) { Reset(); return; }
        var me = Player.m_localPlayer;
        if (me == null || me.IsDead()) { Reset(); return; }
        float now = Time.unscaledTime;

        // Портал: пока летим, ничего не решаем, а после прилёта ждём заново.
        if (me.IsTeleporting()) { _inside = false; _snapAt = -1f; return; }

        if (_snapAt >= 0f && now >= _snapAt) {
            _snapAt = -1f;
            Take(now);
            return;
        }

        if (now < _check) return;
        _check = now + CheckEvery;

        Vector3 home;
        if (!TryHome(out home)) return;
        bool inside = Vector3.Distance(me.transform.position, home) <= Stock.SnapshotRadius;
        if (inside && !_inside) _snapAt = now + Settle;
        else if (inside && _snapAt < 0f && now >= _nextPeriodic) Take(now);
        _inside = inside;
    }

    static void Take(float now) {
        Pin.Snapshot();
        LastWasAuto = true;
    }
}

[HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
static class SnapshotOnChestClose {
    static void Prefix(InventoryGui __instance) {
        // Закрывают не сундук, а пустое место: ничего не было открыто.
        if (__instance.m_currentContainer != null) AutoSnapshot.ChestClosed();
    }
}

}
