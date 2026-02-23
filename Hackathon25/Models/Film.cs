using System.ComponentModel.DataAnnotations;

namespace Hackathon25.Models
{
    public class Film
    {
        public int filmId { get; set; }

        [Required(ErrorMessage = "Film adı boş geçilemez")]
        [MaxLength(100, ErrorMessage = "Film adı en fazla 100 karakter olabilir")]
        public string filmAd { get; set; } =string.Empty;

        public string? filmTur { get; set; }
    }
}
