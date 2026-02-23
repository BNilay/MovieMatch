using Microsoft.AspNetCore.Mvc;
using Hackathon25.Models;
using Hackathon25.Models.ViewModels;
using Hackathon25.Services;
using System.Text.Json;

namespace Hackathon25.Controllers
{
    public class SecimController : Controller
    {
        private readonly GeminiService _geminiService;
        private readonly TmdbService _tmdbService;

        public SecimController(GeminiService geminiService, TmdbService tmdbService)
        {
            _geminiService = geminiService;
            _tmdbService = tmdbService;
        }
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Kisi()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Kisi(int kisiSayisi)
        {
            if (kisiSayisi < 1 || kisiSayisi > 20)
            {
                ModelState.AddModelError("", "Lütfen 1 ile 20 arasında bir sayı giriniz.");
                return View();
            }

            return RedirectToAction("Filmler", new { kisiSayisi });
        }

        [HttpGet]
        public IActionResult Filmler(int kisiSayisi)
        {
            if (kisiSayisi < 1 || kisiSayisi > 20)
                return RedirectToAction("Kisi");

            var vm = new FilmSecimi
            {
                KisiSayisi = kisiSayisi,
                Filmler = new List<Film>()
            };

            for (int i = 0; i < kisiSayisi; i++)
                vm.Filmler.Add(new Film());

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Filmler(FilmSecimi vm)
        {
            if (vm.KisiSayisi < 1 || vm.KisiSayisi > 20)
                ModelState.AddModelError("", "Geçersiz kişi sayısı.");

            vm.Filmler ??= new List<Film>();

            while (vm.Filmler.Count < vm.KisiSayisi)
                vm.Filmler.Add(new Film());

            for (int i = 0; i < vm.KisiSayisi; i++)
            {
                if (string.IsNullOrWhiteSpace(vm.Filmler[i].filmAd))
                    ModelState.AddModelError($"Filmler[{i}].filmAd", $"Film adı zorunlu (Kişi {i + 1}).");
            }

            if (!ModelState.IsValid)
                return View(vm);

            TempData["KisiSayisi"] = vm.KisiSayisi;
            TempData["FilmListesi"] = string.Join(", ", vm.Filmler.Select(f => f.filmAd));

          
            TempData["AI_OrtakTurler"] = "Bulunamadı";
            TempData["AI_UyumluTurler"] = "Öneri üretilemedi";
            TempData["AI_Gerekce"] = "Gerekçe üretilemedi.";
            TempData["TMDB_Recs"] = "[]";
            TempData["DEBUG_GenreIds"] = "";
            TempData["TMDB_GenreIds"] = "";   
            TempData["TMDB_Page"] = 1;     

            try
            {
                
                var filmAdlari = vm.Filmler.Select(f => f.filmAd).ToList();
                var analiz = await _geminiService.AnalyzeGroupTasteAsync(filmAdlari);

                TempData["AI_OrtakTurler"] = (analiz.commonGenres != null && analiz.commonGenres.Any())
                    ? string.Join(", ", analiz.commonGenres)
                    : "Bulunamadı";

                TempData["AI_UyumluTurler"] = (analiz.compatibleGenres != null && analiz.compatibleGenres.Any())
                    ? string.Join(", ", analiz.compatibleGenres)
                    : "Öneri üretilemedi";

                TempData["AI_Gerekce"] = string.IsNullOrWhiteSpace(analiz.shortRationale)
                    ? "Gerekçe üretilemedi."
                    : analiz.shortRationale;

                
                var genreMap = await _tmdbService.GetGenreMapAsync();

                var genreNames = (analiz.commonGenres != null && analiz.commonGenres.Any())
                    ? analiz.commonGenres
                    : (analiz.compatibleGenres ?? new List<string>());

                var genreIds = new List<int>();
                foreach (var name in genreNames)
                {
                    if (genreMap.TryGetValue(name, out var id))
                        genreIds.Add(id);
                }

                genreIds = genreIds.Distinct().ToList();

                TempData["DEBUG_GenreIds"] = string.Join(", ", genreIds);

                
                var usedGenreIds = genreIds.Take(2).ToList();

                if (usedGenreIds.Any())
                {
                    
                    TempData["TMDB_GenreIds"] = string.Join(",", usedGenreIds);
                    TempData["TMDB_Page"] = 1;

                    var recs = await _tmdbService.DiscoverMoviesByGenresAsync(
                        usedGenreIds,
                        count: 10,
                        minYear: 1970,
                        minVoteAverage: 6.5,
                        page: 1
                    );

                  
                    if (recs.Count == 0)
                    {
                        TempData["TMDB_GenreIds"] = usedGenreIds[0].ToString(); 
                        TempData["TMDB_Page"] = 1;

                        recs = await _tmdbService.DiscoverMoviesByGenresAsync(
                            new[] { usedGenreIds[0] },
                            count: 10,
                            minYear: 1970,
                            minVoteAverage: 6.0,
                            page: 1
                        );
                    }

                    TempData["TMDB_Recs"] = JsonSerializer.Serialize(recs);
                }

               
                TempData.Keep("TMDB_GenreIds");
                TempData.Keep("TMDB_Page");
                TempData.Keep("TMDB_Recs");
            }
            catch (Exception ex)
            {
                TempData["AI_OrtakTurler"] = "Hata";
                TempData["AI_UyumluTurler"] = "Hata";
                TempData["AI_Gerekce"] = "AI/TMDB analizi şu anda çalışmadı: " + ex.Message;
                TempData["TMDB_Recs"] = "[]";
            }

            return RedirectToAction("Sonuc");
        }

        [HttpGet]
        public IActionResult Sonuc()
        {
            
            TempData.Keep("TMDB_GenreIds");
            TempData.Keep("TMDB_Page");
            TempData.Keep("TMDB_Recs");

            TempData.Keep("AI_OrtakTurler");
            TempData.Keep("AI_UyumluTurler");
            TempData.Keep("AI_Gerekce");
            TempData.Keep("FilmListesi");
            TempData.Keep("KisiSayisi");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BaskaOneriGetir(int page = 1)
        {
            TempData.Keep("AI_OrtakTurler");
            TempData.Keep("AI_UyumluTurler");
            TempData.Keep("AI_Gerekce");
            TempData.Keep("FilmListesi");
            TempData.Keep("KisiSayisi");

            TempData.Keep("TMDB_GenreIds");
            TempData.Keep("TMDB_Page");

            var idsStr = TempData["TMDB_GenreIds"] as string;

            if (string.IsNullOrWhiteSpace(idsStr))
            {
                TempData["TMDB_Recs"] = "[]";
                return RedirectToAction("Sonuc");
            }

            var genreIds = idsStr
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.Parse(x.Trim()))
                .ToList();

            page++; 
            TempData["TMDB_Page"] = page;

            var recs = await _tmdbService.DiscoverMoviesByGenresAsync(
                genreIds,
                count: 10,
                minYear: 1970,
                minVoteAverage: 6.5,
                page: page
            );

            TempData["TMDB_Recs"] = JsonSerializer.Serialize(recs);

            
            TempData.Keep("TMDB_Recs");
            TempData.Keep("TMDB_GenreIds");
            TempData.Keep("TMDB_Page");

            return RedirectToAction("Sonuc");
        }
    }
}
