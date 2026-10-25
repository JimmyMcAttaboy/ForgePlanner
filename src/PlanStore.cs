using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Forgeplan {

/// <summary>
/// План и закреплённое — между запусками игры.
///
/// До 1.6.0 мод не хранил ничего: план жил, пока открыта игра. Пока в плане
/// было только «что сделать», это было терпимо — набрать заново пять строк
/// недолго. С отметками «этот меч уже есть на первом уровне» иначе: это
/// записанный прогресс группы, и терять его на выходе из игры нельзя.
///
/// Чьё хранилище. Отдельный файл на мир и персонажа: у человека бывает
/// несколько миров, и заказ брони на сервере друзей не должен всплывать в
/// одиночной игре. Мир узнаётся по имени и сиду — их знает и клиент на
/// выделенном сервере; персонаж — по файлу профиля.
///
/// Только у себя на диске, в BepInEx/config/forgeplanner. В мир и в
/// сохранения мод по-прежнему не пишет ничего (README, «Client-side only»):
/// удалили мод — остался файл, который никто не читает.
///
/// Пишем не по каждому щелчку, а раз в пару секунд и только если что-то
/// поменялось — и обязательно при выходе из мира.
/// </summary>
public static class PlanStore {
    const float Every = 2f;
    const string Header = "# Provisioner";

    static string _path;
    static string _saved;
    static float _checkAt;

    /// <summary>
    /// Мы только что вышли из мира, а персонаж ещё жив. Выход идёт в
    /// несколько кадров: ZNetScene уже закрыт, план дописан и стёрт, а
    /// Player.m_localPlayer ещё не уничтожен. Без этой отметки Tick увидел бы
    /// «персонаж есть, плана нет» и загрузил его снова — в уходящий мир.
    /// Закреплённая панель строилась на миг и пропадала со сценой, а путь к
    /// файлу оставался старым, и следующий мир писал бы в чужой файл.
    /// </summary>
    static bool _leaving;

    /// <summary>Зовётся каждый кадр. Первый кадр с персонажем в мире —
    /// загрузка, дальше — запись, если план поменялся.</summary>
    public static void Tick() {
        if (Player.m_localPlayer == null) { _leaving = false; return; }
        if (_leaving) return;
        if (_path == null) {
            _path = PathFor();
            if (_path == null) return;
            Load();
            _saved = Snapshot();
            return;
        }
        if (Time.unscaledTime < _checkAt) return;
        _checkAt = Time.unscaledTime + Every;
        // Мир или персонаж сменились, а выход мы почему-то пропустили: не
        // писать чужой план в этот файл.
        var now = PathFor();
        if (now != null && now != _path) {
            ForgeplanPlugin.Log.LogWarning("план: сменился мир или персонаж без выхода — "
                                           + Path.GetFileName(_path) + " → " + Path.GetFileName(now));
            Flush();
            _path = now;
            Load();
            _saved = Snapshot();
            return;
        }
        Flush();
    }

    /// <summary>Выход из мира: дописать и забыть, чей это был план.</summary>
    public static void Leave() {
        Flush();
        _path = null;
        _saved = null;
        _leaving = true;
    }

    static void Flush() {
        if (_path == null) return;
        var snap = Snapshot();
        if (snap == _saved) return;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, snap, new UTF8Encoding(false));
            _saved = snap;
        } catch (Exception e) {
            // Не записалось — попробуем через пару секунд снова; окну это не
            // мешает, а строка в логе скажет, почему план не пережил выход.
            ForgeplanPlugin.Log.LogWarning("план не записан в " + _path + " — " + e.Message);
            _saved = snap;
        }
    }

    static string PathFor() {
        var world = WorldGenerator.instance != null ? WorldGenerator.instance.m_world : null;
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        if (world == null || profile == null) return null;
        string name = Safe(world.m_name) + "-" + Safe(world.m_seedName) + "--" + Safe(profile.GetFilename());
        var dir = Path.Combine(BepInEx.Paths.ConfigPath, "provisioner");
        var path = Path.Combine(dir, name + ".plan");
        // Переходят с ForgePlanner: план этого мира берём оттуда один раз.
        // Копия, а не перенос: оригинал остаётся как был.
        var old = Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "forgeplanner"), name + ".plan");
        try {
            if (!File.Exists(path) && File.Exists(old)) {
                Directory.CreateDirectory(dir);
                File.Copy(old, path);
            }
        } catch (Exception ex) {
            ForgeplanPlugin.Log.LogWarning("план ForgePlanner не скопирован: " + ex.Message);
        }
        return path;
    }

    static string Safe(string s) {
        if (string.IsNullOrEmpty(s)) return "_";
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(bad.Contains(c) || c == ' ' ? '_' : c);
        return sb.ToString();
    }

    /* Формат — строки через табуляцию, чтобы файл читался и правился руками:

           # ForgePlanner
           [plan]
           swordiron	4	2	1,1,0,0
           [pinned]
           plan
           gather
           [chests]
           sig	-1234567
           boxes	14
           at	638655123450000000
           pos	12.5,30.0,-88.1
           wolfpelt	26

       id, сколько, уровень, уровни уже сделанных экземпляров. В [pinned]:
       какие части плана закреплены. В [chests]: снимок сундуков, см.
       Pin.Snapshot: отпечаток плана, сколько сундуков, когда (ticks) и где,
       потом сколько каждого материала в них лежало. Старый раздел [pin]
       (копия плана до того, как закреплённое стало самим планом) читается
       как «gather». */

    static string Snapshot() {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        Write(sb, "[plan]", Planner.Cart);
        sb.Append("[pinned]").Append('\n');
        if (Pin.Plan) sb.Append("plan").Append('\n');
        if (Pin.Gather) sb.Append("gather").Append('\n');
        // Вид запоминается: без него после перезахода окно открывалось в
        // «Сырьё», отпечаток плана не совпадал со снимком, и подсказка зря
        // просила пересчитать.
        if (!Pin.RawView) sb.Append("recipes").Append('\n');
        // Дом для автоснимка, если игрок задал его кнопкой.
        if (AutoSnapshot.Home.HasValue) {
            var h = AutoSnapshot.Home.Value;
            var hi = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("[home]").Append('\n');
            sb.Append("pos").Append('\t').Append(h.x.ToString("0.0", hi)).Append(',')
              .Append(h.y.ToString("0.0", hi)).Append(',').Append(h.z.ToString("0.0", hi)).Append('\n');
        }
        if (Pin.Gather && Pin.HasSnapshot) {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("[chests]").Append('\n');
            sb.Append("sig").Append('\t').Append(Pin.TargetsOf).Append('\n');
            sb.Append("boxes").Append('\t').Append(Pin.Boxes).Append('\n');
            sb.Append("at").Append('\t').Append(Pin.At.Ticks).Append('\n');
            sb.Append("pos").Append('\t').Append(Pin.Where.x.ToString("0.0", inv)).Append(',')
              .Append(Pin.Where.y.ToString("0.0", inv)).Append(',')
              .Append(Pin.Where.z.ToString("0.0", inv)).Append('\n');
            foreach (var kv in Pin.Chests)
                sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
        }
        return sb.ToString();
    }

    static void Write(StringBuilder sb, string title, List<Entry> list) {
        sb.Append(title).Append('\n');
        foreach (var e in list)
            sb.Append(e.Id).Append('\t').Append(e.Qty).Append('\t').Append(e.Quality)
              .Append('\t').Append(string.Join(",", e.Made.Select(h => h.ToString()).ToArray()))
              .Append('\n');
    }

    static void Load() {
        if (!File.Exists(_path)) {
            // У этого мира и персонажа плана ещё нет — начинаем с пустого,
            // а не с того, что осталось от прошлого мира.
            Planner.Cart.Clear();
            Pin.Clear();
            return;
        }
        var plan = new List<Entry>();
        var pin = new List<Entry>();
        List<Entry> into = null;
        bool flags = false, gather = false, pinPlan = false, pinGather = false, recipes = false;
        var chests = new Dictionary<string, int>();
        int targetsOf = 0, boxes = 0;
        long at = 0;
        var where = Vector3.zero;
        Vector3? home = null;
        bool inHome = false;
        try {
            foreach (var raw in File.ReadAllLines(_path)) {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line == "[plan]") { into = plan; flags = gather = false; continue; }
                if (line == "[pin]") { into = pin; flags = gather = false; continue; }
                if (line == "[pinned]") { into = null; flags = true; gather = false; continue; }
                // [gather], [gathered] и [short] писали прошлые сборки, снимок
                // там значил другое. Не читаем: подсказка попросит пересчитать.
                if (line == "[gather]" || line == "[gathered]" || line == "[short]") {
                    into = null; flags = gather = false; continue;
                }
                if (line == "[chests]") { into = null; flags = false; gather = true; inHome = false; continue; }
                if (line == "[home]") { into = null; flags = gather = false; inHome = true; continue; }
                if (inHome) {
                    var hf = raw.Split('\t');
                    Vector3 hp;
                    if (hf.Length > 1 && hf[0] == "pos" && TryVector(hf[1], out hp)) home = hp;
                    continue;
                }
                if (flags) {
                    if (line == "plan") pinPlan = true;
                    else if (line == "gather") pinGather = true;
                    else if (line == "recipes") recipes = true;
                    continue;
                }
                if (gather) {
                    var f = raw.Split('\t');
                    if (f.Length < 2) continue;
                    if (f[0] == "at") { long.TryParse(f[1], out at); continue; }
                    if (f[0] == "pos") {
                        Vector3 p;
                        if (TryVector(f[1], out p)) where = p;
                        continue;
                    }
                    int a;
                    if (!int.TryParse(f[1], out a)) continue;
                    if (f[0] == "sig") targetsOf = a;
                    else if (f[0] == "boxes") boxes = a;
                    else if (a > 0) chests[f[0]] = a;
                    continue;
                }
                var e = Parse(raw);
                if (e != null && into != null) into.Add(e);
            }
        } catch (Exception ex) {
            ForgeplanPlugin.Log.LogWarning("план не прочитан из " + _path + " — " + ex.Message);
            return;
        }
        // Без PlanBuild строки тотемов показать нечем: убираем, а не держим
        // строку, которая никогда не наполнится.
        if (!PlanTotems.Available) {
            plan.RemoveAll(e => PlanTotems.IsTotem(e.Id));
        }
        Planner.Cart.Clear();
        Planner.Cart.AddRange(plan);
        if (pin.Count > 0) pinGather = true;
        Pin.RawView = !recipes;
        Pin.Set(pinPlan, pinGather);
        // Снимок из файла. Нет его (файл из старой версии): TargetsOf
        // остаётся 0, и подсказка просит пересчитать.
        Pin.Chests.Clear();
        if (pinGather) foreach (var kv in chests) Pin.Chests[kv.Key] = kv.Value;
        Pin.TargetsOf = pinGather ? targetsOf : 0;
        Pin.Boxes = boxes;
        Pin.At = at > 0 ? new DateTime(at) : DateTime.MinValue;
        Pin.Where = where;
        AutoSnapshot.Home = home;
        AutoSnapshot.Reset();
        ForgeplanPlugin.Log.LogInfo("план загружен: позиций " + plan.Count + ", закреплено: план "
                                    + pinPlan + ", собрать " + pinGather + " — " + Path.GetFileName(_path));
    }

    static bool TryVector(string text, out Vector3 v) {
        v = Vector3.zero;
        var p = text.Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var st = System.Globalization.NumberStyles.Float;
        float x, y, z;
        if (p.Length != 3 || !float.TryParse(p[0], st, inv, out x)
            || !float.TryParse(p[1], st, inv, out y) || !float.TryParse(p[2], st, inv, out z))
            return false;
        v = new Vector3(x, y, z);
        return true;
    }

    static Entry Parse(string line) {
        var f = line.Split('\t');
        if (f.Length < 3 || f[0].Length == 0) return null;
        int qty, quality;
        if (!int.TryParse(f[1], out qty) || !int.TryParse(f[2], out quality)) return null;
        var e = new Entry { Id = f[0], Qty = Mathf.Max(1, qty), Quality = Mathf.Clamp(quality, 1, 4) };
        if (f.Length > 3 && f[3].Length > 0)
            foreach (var part in f[3].Split(',')) {
                int h;
                e.Made.Add(int.TryParse(part, out h) ? Mathf.Clamp(h, 0, 4) : 0);
            }
        e.Fit();
        return e;
    }
}

}
