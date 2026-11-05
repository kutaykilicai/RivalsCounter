using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RivalsCounter
{
    public class VisionResult
    {
        public List<string> enemies = new();
        public List<string> allies = new();
        public string me;
        public string map;
        public string screenType;
        public string note;
        public double confidence;
    }

    public static class Vision
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

        public static async Task<VisionResult> AnalyzeAsync(byte[] pngBytes)
        {
            if (!Config.HasVisionKey) throw new Exception("API anahtarı yok. %LOCALAPPDATA%\\RivalsCounter\\config.json içine openRouterApiKey veya geminiApiKey yaz.");

            var names = Engine.D.heroes.Select(h => h.name).Distinct().OrderBy(x => x).ToList();
            var roster = string.Join(", ", names);
            var prompt =
                "Marvel Rivals screenshot analysis. Return ONLY JSON keys: enemies (string[]<=6), allies (string[]<=5 excluding me), me (string|null), map (string|null), screenType (scoreboard|hero_select|loading|hud|unknown), confidence (0-1), note (short).\n" +
                "Rules: Prefer printed hero NAME TEXT beside portraits over guessing from art. YOUR TEAM/blue = allies+me; ENEMY TEAM/red = enemies. Highlighted/yellow local player row is me (do not also put that hero in allies). Skip '?'/locked slots. Use EXACT roster names: " + roster + ". Map only if clearly visible.";

            string b64 = Convert.ToBase64String(pngBytes);
            if (!string.IsNullOrWhiteSpace(Config.Current.openRouterApiKey))
                return await CallOpenRouter(prompt, b64);
            return await CallGemini(prompt, b64);
        }

        static async Task<VisionResult> CallOpenRouter(string prompt, string b64)
        {
            var body = new
            {
                model = Config.Current.visionModel ?? "google/gemini-2.5-pro",
                temperature = 0.1,
                messages = new object[]
                {
                    new {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = "data:image/png;base64," + b64 } }
                        }
                    }
                }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.Current.openRouterApiKey);
            req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/kutaykilicai/RivalsCounter");
            req.Headers.TryAddWithoutValidation("X-Title", "Rivals Counter");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req);
            var txt = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new Exception("OpenRouter: " + (int)res.StatusCode + " " + Trunc(txt));
            using var doc = JsonDocument.Parse(txt);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return Parse(content);
        }

        static async Task<VisionResult> CallGemini(string prompt, string b64)
        {
            var model = "gemini-2.0-flash";
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Config.Current.geminiApiKey}";
            var body = new
            {
                contents = new[]
                {
                    new {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { inline_data = new { mime_type = "image/png", data = b64 } }
                        }
                    }
                },
                generationConfig = new { temperature = 0.1 }
            };
            using var res = await Http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            var txt = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new Exception("Gemini: " + (int)res.StatusCode + " " + Trunc(txt));
            using var doc = JsonDocument.Parse(txt);
            var content = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            return Parse(content);
        }

        static string Trunc(string s) => string.IsNullOrEmpty(s) ? "" : s.Length <= 240 ? s : s.Substring(0, 240);

        public static VisionResult Parse(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) throw new Exception("Boş model yanıtı");
            var m = Regex.Match(content, "\\{[\\s\\S]*\\}");
            if (!m.Success) throw new Exception("JSON bulunamadı: " + Trunc(content));
            using var doc = JsonDocument.Parse(m.Value);
            var root = doc.RootElement;
            var vr = new VisionResult();
            vr.enemies = ReadNames(root, "enemies");
            vr.allies = ReadNames(root, "allies");
            if (root.TryGetProperty("me", out var me) && me.ValueKind == JsonValueKind.String) vr.me = Canon(me.GetString());
            if (root.TryGetProperty("map", out var map) && map.ValueKind == JsonValueKind.String) vr.map = map.GetString();
            if (root.TryGetProperty("screenType", out var st) && st.ValueKind == JsonValueKind.String) vr.screenType = st.GetString();
            if (root.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.String) vr.note = note.GetString();
            if (root.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var cd)) vr.confidence = cd;
            return vr;
        }

        static List<string> ReadNames(JsonElement root, string key)
        {
            var list = new List<string>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var x in arr.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.String) continue;
                var n = Canon(x.GetString());
                if (n != null && !list.Contains(n)) list.Add(n);
            }
            return list;
        }

        public static string Canon(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var n = Norm(raw);
            Hero best = null; int bestScore = int.MaxValue;
            foreach (var h in Engine.D.heroes)
            {
                var hn = Norm(h.name);
                if (hn == n) return h.name;
                if (hn.Contains(n) || n.Contains(hn))
                {
                    int score = Math.Abs(hn.Length - n.Length);
                    if (score < bestScore) { bestScore = score; best = h; }
                }
            }
            // aliases
            var aliases = new Dictionary<string, string>
            {
                ["cloakanddagger"] = "Cloak & Dagger",
                ["cd"] = "Cloak & Dagger",
                ["jeff"] = "Jeff The Land Shark",
                ["jeffthelandshark"] = "Jeff The Land Shark",
                ["spiderman"] = "Spider-man",
                ["starlord"] = "Star-lord",
                ["mrfantastic"] = "Mister Fantastic",
                ["misterfantastic"] = "Mister Fantastic",
                ["inviswoman"] = "Invisible Woman",
                ["invisiblewoman"] = "Invisible Woman",
                ["drstrange"] = "Doctor Strange",
                ["doctorstrange"] = "Doctor Strange",
                ["peni"] = "Peni Parker",
                ["peniparker"] = "Peni Parker",
                ["rocket"] = "Rocket Raccoon",
                ["rocketraccoon"] = "Rocket Raccoon",
                ["brucebanner"] = "Hulk",
                ["hulk"] = "Hulk",
                ["gorr"] = "Gorr The God Butcher",
                ["gorrthegodbutcher"] = "Gorr The God Butcher",
                ["devildino"] = "Devil Dinosaur",
                ["devildinosaur"] = "Devil Dinosaur",
                ["wintersoldier"] = "Winter Soldier",
                ["bucky"] = "Winter Soldier",
            };
            if (aliases.TryGetValue(n, out var a)) return a;
            return best?.name;
        }

        static string Norm(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
