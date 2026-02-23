using System.Text.Json;

namespace Hackathon25.Services;

public class TmdbService
{
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _baseUrl;

	public TmdbService(HttpClient http, IConfiguration config)
	{
		_http = http;
		_apiKey = config["TMDB:ApiKey"] ?? throw new InvalidOperationException("TMDB:ApiKey yok.");
		_baseUrl = config["TMDB:BaseUrl"] ?? "https://api.themoviedb.org/3";
	}

	// Genre isimlerini TMDB genre_id'ye çevir (örn: Drama -> 18)
	public async Task<Dictionary<string, int>> GetGenreMapAsync()
	{
        var url = $"{_baseUrl}/genre/movie/list?api_key={_apiKey}&language=en-US";
        var json = await _http.GetStringAsync(url);

		using var doc = JsonDocument.Parse(json);
		var arr = doc.RootElement.GetProperty("genres").EnumerateArray();

		// name -> id
		var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var g in arr)
		{
			var name = g.GetProperty("name").GetString() ?? "";
			var id = g.GetProperty("id").GetInt32();
			if (!string.IsNullOrWhiteSpace(name))
				map[name] = id;
		}
		return map;
	}

    // Tür id’lerine göre film önerisi çek (discover)
    public async Task<List<TmdbMovie>> DiscoverMoviesByGenresAsync(
    IEnumerable<int> genreIds,
    int count = 10,
    int minYear = 1980,
    double minVoteAverage = 7.0,
    int page = 1)
    {
        var withGenres = string.Join(",", genreIds.Distinct());

        var url =
            $"{_baseUrl}/discover/movie?api_key={_apiKey}" +
            $"&with_genres={withGenres}" +
            $"&primary_release_date.gte={minYear}-01-01" +
            $"&vote_average.gte={minVoteAverage}" +
            $"&sort_by=popularity.desc" +
            $"&language=tr-TR" +
            $"&page={page}";

        var json = await _http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results").EnumerateArray();

        var list = new List<TmdbMovie>();
        foreach (var r in results)
        {
            list.Add(new TmdbMovie
            {
                Id = r.GetProperty("id").GetInt32(),
                Title = r.GetProperty("title").GetString() ?? "",
                Overview = r.TryGetProperty("overview", out var o) ? (o.GetString() ?? "") : "",
                PosterPath = r.TryGetProperty("poster_path", out var p) ? (p.GetString() ?? "") : "",
                VoteAverage = r.TryGetProperty("vote_average", out var v) ? v.GetDouble() : 0,
                ReleaseDate = r.TryGetProperty("release_date", out var d) ? (d.GetString() ?? "") : ""
            });

            if (list.Count >= count) break;
        }
        return list;
    }

}

public class TmdbMovie
{
	public int Id { get; set; }
	public string Title { get; set; } = "";
	public string Overview { get; set; } = "";
	public string PosterPath { get; set; } = "";
	public double VoteAverage { get; set; }
	public string ReleaseDate { get; set; } = "";
}
