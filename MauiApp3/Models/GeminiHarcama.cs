using Newtonsoft.Json;

namespace MauiApp3.Models // Kendi namespace'ini yazmayı unutma
{
    public class GeminiHarcama
    {
        [JsonProperty("Tarih")]
        public string Tarih { get; set; }

        // AI bazen string ("100 TL") bazen sayı (100) gönderiyor. Hepsini yakalamak için object yaptık.
        [JsonProperty("Tutar")]
        public object Tutar { get; set; }

        [JsonProperty("Kategori")]
        public string Kategori { get; set; }

        [JsonProperty("Aciklama")]
        public string Aciklama { get; set; }
    }
}