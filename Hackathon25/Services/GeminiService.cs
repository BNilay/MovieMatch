using System.Text;
using System.Text.Json;

namespace Hackathon25.Services;

public class GeminiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiService(HttpClient http, IConfiguration config)
    {
        _http = http;

        _apiKey = config["Gemini:ApiKey"]
                 ?? throw new InvalidOperationException("Gemini:ApiKey bulunamadı (User Secrets).");

        _model = config["Gemini:Model"]
                 ?? throw new InvalidOperationException("Gemini:Model bulunamadı (User Secrets).");
    }

    public async Task<GroupTasteResult> AnalyzeGroupTasteAsync(List<string> filmAdlari)
    {
        var modelId = _model.StartsWith("models/") ? _model["models/".Length..] : _model;

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={_apiKey}";

        // ✅ EN KRİTİK DÜZELTME:
        // - description alanını zorunlu kılıyoruz
        // - compatibleGenres her zaman 3-5 olacak şekilde zorunlu kılıyoruz
        var prompt = $@"
Analyze the following movie titles and infer the group's COMMON WATCHABLE GENRES.

Movie titles: {string.Join(", ", filmAdlari)}

Task:
1) Map each title to its commonly accepted genres.
2) Find intersections: put genres that appear in at least 2 movies into 'commonGenres'.
3) ALWAYS output 3-5 'compatibleGenres' that unify the group (even if commonGenres is non-empty).
   - compatibleGenres must NOT be empty.

Rules:
- Do NOT describe plot/themes. Only genre logic.
- You MUST choose genres ONLY from this exact list (TMDB movie genres):
  [""Action"", ""Adventure"", ""Animation"", ""Comedy"", ""Crime"", ""Documentary"", ""Drama"",
   ""Family"", ""Fantasy"", ""History"", ""Horror"", ""Music"", ""Mystery"", ""Romance"",
   ""Science Fiction"", ""Thriller"", ""War"", ""Western""]
- Output MUST be valid JSON and NOTHING else.
- If you are unsure about a title, infer the most likely mainstream genres.

JSON format:
{{
  ""description"": ""1 sentence summarizing group taste using ONLY genres."",
  ""commonGenres"": [""..."", ""...""],
  ""compatibleGenres"": [""..."", ""...""],
  ""shortRationale"": ""1 sentence, genre logic only.""
}}
";

        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 350,
                responseMimeType = "application/json"
            }
        };

        var json = JsonSerializer.Serialize(body);
        var resp = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new Exception($"Gemini hata: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {err}");
        }

        var respText = await resp.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(respText);

        // ✅ Daha güvenli okuma
        var candidates = doc.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() == 0)
            throw new Exception("Gemini candidates boş döndü.");

        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        if (parts.GetArrayLength() == 0)
            throw new Exception("Gemini parts boş döndü.");

        var text = parts[0].TryGetProperty("text", out var textEl) ? textEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(text))
            throw new Exception("Gemini boş cevap döndü (text null/empty).");

        text = CleanJson(text);

        var result = JsonSerializer.Deserialize<GroupTasteResult>(
            text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (result == null)
            throw new Exception("Gemini JSON parse edilemedi.");

        // ✅ Son garanti: compatibleGenres boşsa doldur (edge case)
        result.commonGenres ??= new List<string>();
        result.compatibleGenres ??= new List<string>();

        if (result.compatibleGenres.Count == 0)
        {
            // commonGenres varsa onu baz al, yoksa default mainstream
            var fallback = result.commonGenres.Count > 0
                ? result.commonGenres.Take(3).ToList()
                : new List<string> { "Drama", "Adventure", "Comedy" };

            result.compatibleGenres = fallback;
        }

        if (string.IsNullOrWhiteSpace(result.description))
        {
            // description yine de boş gelirse rationale’dan üret
            result.description = !string.IsNullOrWhiteSpace(result.shortRationale)
                ? result.shortRationale
                : "Group taste inferred from common/compatible genres.";
        }

        return result;
    }

    private static string CleanJson(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
            s = s.Replace("```json", "").Replace("```", "").Trim();

        var first = s.IndexOf('{');
        var last = s.LastIndexOf('}');
        if (first >= 0 && last > first)
            s = s.Substring(first, last - first + 1);

        return s;
    }
}

public class GroupTasteResult
{
    // ✅ EKLENDİ: Açıklama alanın bunu kullanacak
    public string description { get; set; } = "";

    public List<string> commonGenres { get; set; } = new();

    public List<string> compatibleGenres { get; set; } = new();

    public string shortRationale { get; set; } = "";
}

public class GeminiFilter
{
    public int minYear { get; set; }
    public int maxYear { get; set; }
    public double minRating { get; set; }
}
