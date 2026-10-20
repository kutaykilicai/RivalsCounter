using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace RivalsCounter
{
    public class TeamUp { public long id { get; set; } public string name { get; set; } public List<int> heroes { get; set; } public string text { get; set; } }
    public class Hero
    {
        public int id { get; set; } public string name { get; set; } public string role { get; set; } public string tier { get; set; }
        public double? winRate { get; set; } public double? pickRate { get; set; } public long matches { get; set; } public string icon { get; set; }
        public List<TeamUp> teamUps { get; set; } = new();
    }
    public class Counter { public int hero { get; set; } public double wrVs { get; set; } public double delta { get; set; } public long matches { get; set; } public double siteDiff { get; set; } public double lift { get; set; } public string source { get; set; } }
    public class Data
    {
        public string generated { get; set; } public string patch { get; set; } public string tierDate { get; set; } public string rivalsmetaStatsDate { get; set; }
        public object rivalsmetaSeasonId { get; set; } public Dictionary<string, string> sources { get; set; } public string notes { get; set; }
        public List<Hero> heroes { get; set; } public Dictionary<string, List<Counter>> counters { get; set; }
    }

    public class Rec { public Hero Hero; public double Score; public string Reason; }

    public static class Engine
    {
        public static Data D;
        public static Dictionary<int, Hero> ById;
        static Dictionary<(int c, int e), Counter> pair;
        public static string BaseDir = AppContext.BaseDirectory;
        public static string UserIcons = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RivalsCounter", "Icons");
        public static string UserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RivalsCounter", "heroes.json");

        public static void Load()
        {
            var p = File.Exists(UserData) ? UserData : Path.Combine(BaseDir, "Data", "heroes.json");
            D = JsonSerializer.Deserialize<Data>(File.ReadAllBytes(p));
            Index();
        }
        static void Index()
        {
            ById = D.heroes.ToDictionary(h => h.id);
            pair = new();
            foreach (var kv in D.counters) { int e = int.Parse(kv.Key); foreach (var c in kv.Value) pair[(c.hero, e)] = c; }
        }
        static double TierBonus(string t) => t switch { "S" => 1.5, "A" => 0.8, "B" => 0, "C" => -0.8, _ => 0 };
        public static string RoleTr(string r) => r switch { "Vanguard" => "Öncü (Tank)", "Duelist" => "Düellocu (DPS)", _ => "Stratejist (Destek)" };

        public static Dictionary<string, List<Rec>> Recommend(IList<int> enemies, int? struggling, IList<int> allies, int? me, int perRole = 4)
        {
            var res = new Dictionary<string, List<Rec>>();
            var team = allies.ToList(); if (me.HasValue) team.Add(me.Value);
            foreach (var h in D.heroes)
            {
                if (allies.Contains(h.id)) continue;
                double s = 0; var parts = new List<string>(); int n = 0;
                Counter best = null; int bestE = 0; double bestW = double.MinValue;
                foreach (var e in enemies)
                {
                    if (!pair.TryGetValue((h.id, e), out var c)) continue;
                    double w = (struggling == e) ? 3.0 : 1.0;
                    s += c.delta * w; n++;
                    if (c.delta * w > bestW) { bestW = c.delta * w; best = c; bestE = e; }
                }
                if (struggling.HasValue && pair.TryGetValue((h.id, struggling.Value), out var sc))
                    parts.Add($"{ById[struggling.Value].name}'e karşı %{sc.wrVs:0.0} kazanma ({sc.matches:N0} maç)");
                else if (best != null && best.delta > 0)
                    parts.Add($"{ById[bestE].name}'e karşı %{best.wrVs:0.0} kazanma ({best.matches:N0} maç)");
                double tb = TierBonus(h.tier); s += tb;
                if (h.tier != null) parts.Add($"{h.tier} tier");
                foreach (var tu in h.teamUps)
                {
                    var mates = tu.heroes.Where(x => x != h.id && team.Contains(x)).ToList();
                    if (mates.Count > 0) { s += 1.0; parts.Add($"Team-up: {string.Join(", ", mates.Select(m => ById[m].name))} ({tu.name})"); break; }
                }
                if (!res.TryGetValue(h.role, out var l)) res[h.role] = l = new();
                l.Add(new Rec { Hero = h, Score = s, Reason = string.Join(" · ", parts) });
            }
            foreach (var k in res.Keys.ToList()) res[k] = res[k].OrderByDescending(r => r.Score).Take(perRole).ToList();
            return res;
        }

        static string Slug(string n) { var s = new string(n.ToLowerInvariant().Replace("&", "").Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()); while (s.Contains("--")) s = s.Replace("--", "-"); s = s.Trim('-'); return s == "hulk" ? "bruce-banner" : s; }
        static async Task<string> RefreshTiersAsync(HttpClient http)
        {
            var html = await http.GetStringAsync("https://marvelrivals.gg/best-heroes-meta-tier-list/");
            var parts = System.Text.RegularExpressions.Regex.Split(html, "<h2[^>]*>(.*?)</h2>");
            var map = new Dictionary<string, string>(); var dp = new List<string>();
            for (int k = 1; k + 1 < parts.Length; k += 2)
            {
                var title = System.Text.RegularExpressions.Regex.Replace(parts[k], "<[^>]+>", "").Trim();
                if (title.Length < 6 || !title.EndsWith("-Tier")) continue;
                string t = title.Substring(0, 1);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(parts[k + 1], "data-grid=\"([^\"]*)\""))
                {
                    var list = m.Groups[1].Value.ToLowerInvariant().Split(',');
                    for (int i = 0; i < list.Length; i++)
                    {
                        if (list[i] == "deadpool") { // form = role of previous hero in list
                            var prev = i > 0 ? D.heroes.FirstOrDefault(h => Slug(h.name) == list[i - 1])?.role : null;
                            var key = "deadpool-" + (prev ?? "x").ToLowerInvariant();
                            if (map.ContainsKey(key) || prev == null) key = new[] { "deadpool-vanguard", "deadpool-duelist", "deadpool-strategist" }.FirstOrDefault(x => !map.ContainsKey(x)) ?? key;
                            map[key] = t; }
                        else if (!map.ContainsKey(list[i])) map[list[i]] = t;
                    }
                }
            }
            if (map.Count < 30) throw new Exception("tier listesi okunamadı");
            int n = 0;
            foreach (var h in D.heroes)
            {
                string key = h.name.StartsWith("Deadpool") ? "deadpool-" + h.role.ToLowerInvariant() : Slug(h.name);
                if (map.TryGetValue(key, out var t)) { h.tier = t; n++; }
            }
            var dm = System.Text.RegularExpressions.Regex.Match(html, "Updated:\\s*([A-Za-z]{3} \\d{1,2}, \\d{4})");
            if (dm.Success) D.tierDate = dm.Groups[1].Value;
            var sm = System.Text.RegularExpressions.Regex.Match(html, "Season (\\d+(\\.\\d+)?) Ranked");
            if (sm.Success) D.patch = "Season " + sm.Groups[1].Value;
            return $"{n} tier güncellendi";
        }

        // ---- online refresh: rivalsmeta Nuxt payloads ----
        static JsonNode Decode(JsonArray d)
        {
            var memo = new Dictionary<int, JsonNode>();
            JsonNode R(int i)
            {
                if (i < 0) return null;
                if (memo.TryGetValue(i, out var m)) return m?.DeepClone();
                var v = d[i]; JsonNode o;
                if (v is JsonArray a)
                {
                    if (a.Count > 0 && a[0] is JsonValue sv && sv.TryGetValue<string>(out var tag) && (tag is "ShallowReactive" or "Reactive" or "Ref" or "ShallowRef"))
                        o = R((int)a[1]);
                    else { var arr = new JsonArray(); foreach (var x in a) arr.Add(R((int)x)); o = arr; }
                }
                else if (v is JsonObject ob) { var no = new JsonObject(); foreach (var kv in ob) no[kv.Key] = kv.Value is JsonValue jv && jv.TryGetValue<int>(out var ix) ? R(ix) : null; o = no; }
                else o = v?.DeepClone();
                memo[i] = o; return o?.DeepClone();
            }
            return R(0);
        }
        public static async Task<string> RefreshAsync()
        {
            using var http = new HttpClient(); http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 RivalsCounter");
            http.Timeout = TimeSpan.FromSeconds(30);
            var mmRaw = JsonNode.Parse(await http.GetStringAsync("https://rivalsmeta.com/characters/magik/matchups/_payload.json")).AsArray();
            var mm = Decode(mmRaw)["data"]["hero-matchups"].AsObject();
            var baseWr = new Dictionary<int, double>();
            foreach (var row in mm)
            {
                long M = 0, W = 0;
                foreach (var kv in row.Value.AsObject()) { if (kv.Key == row.Key) continue; M += (long)kv.Value["matches"]; W += (long)kv.Value["wins"]; }
                if (M > 0) baseWr[int.Parse(row.Key)] = 100 - W * 100.0 / M; // row = opponents' wins vs hero
            }
            var counters = new Dictionary<string, List<Counter>>();
            foreach (var row in mm)
            {
                int e = int.Parse(row.Key); if (!ById.ContainsKey(e) || !baseWr.ContainsKey(e)) continue;
                var l = new List<Counter>();
                foreach (var kv in row.Value.AsObject())
                {
                    int c = int.Parse(kv.Key); long m = (long)kv.Value["matches"]; long w = (long)kv.Value["wins"];
                    if (c == e || m < 1500 || !ById.ContainsKey(c) || !baseWr.ContainsKey(c)) continue;
                    double wr = w * 100.0 / m; // mm[E][C].wins = C's wins vs E
                    l.Add(new Counter { hero = c, wrVs = Math.Round(wr, 2), delta = Math.Round(wr - 50, 2), lift = Math.Round(wr - baseWr[c], 2), siteDiff = Math.Round(2 * (wr - 50), 2), matches = m, source = $"rivalsmeta.com/characters/{ById[e].name}/matchups" });
                }
                counters[row.Key] = l.OrderByDescending(x => x.delta).ToList();
            }
            // tiers + win/pick rate
            try
            {
                var tl = Decode(JsonNode.Parse(await http.GetStringAsync("https://rivalsmeta.com/tier-list/_payload.json")).AsArray())["data"].AsObject().First().Value;
                var agg = new Dictionary<int, long[]>();
                foreach (var g in tl["heroes"].AsArray()) foreach (var h in g["heroes"].AsArray())
                { int id = (int)h["hero_id"]; if (!agg.TryGetValue(id, out var a)) agg[id] = a = new long[3]; a[0] += (long)h["wr_matches"]; a[1] += (long)h["wr_wins"]; a[2] += (long)h["matches"]; }
                double total = agg.Values.Sum(a => a[2]) / 12.0;
                foreach (var h in D.heroes) if (agg.TryGetValue(h.id, out var a) && a[0] > 0) { h.winRate = Math.Round(a[1] * 100.0 / a[0], 2); h.pickRate = Math.Round(a[2] * 100 / total, 2); h.matches = a[2]; }
            } catch { }
            string tierMsg = "";
            try { tierMsg = await RefreshTiersAsync(http); } catch (Exception ex) { tierMsg = "tier güncellenemedi: " + ex.Message; }
            if (counters.Count < 30) throw new Exception("Beklenmeyen veri formatı");
            D.counters = counters; D.generated = DateTime.Now.ToString("yyyy-MM-dd");
            D.rivalsmetaStatsDate = DateTime.Now.ToString("yyyy-MM-dd");
            Directory.CreateDirectory(Path.GetDirectoryName(UserData));
            File.WriteAllText(UserData, JsonSerializer.Serialize(D));
            Index();
            return $"{counters.Count} kahraman eşleşmesi güncellendi · {tierMsg}";
        }
    }
}
