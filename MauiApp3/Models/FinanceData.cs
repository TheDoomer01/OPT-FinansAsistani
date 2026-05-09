using SQLite;
using Newtonsoft.Json;

namespace MauiApp3.Models
{
    [Table("AnalizGecmisi")]
    public class FinanceData
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [JsonProperty("tavsiye")]
        public string tavsiye { get; set; }

        // Gemini'den gelen orijinal liste (Veritabanı bunu görmezden gelir)
        [Ignore]
        [JsonProperty("haberler")]
        public List<string> haberler { get; set; }

        // Veritabanına kaydedilecek olan birleştirilmiş metin
        public string HaberlerMetni { get; set; }

        public DateTime KayitTarihi { get; set; }
    }
}