using System.ComponentModel.DataAnnotations;

namespace Hackathon25.Models.ViewModels
{
    public class FilmSecimi
    {
        public int KisiSayisi { get; set; }

        public List<Film> Filmler { get; set; } = new();

    }
}
