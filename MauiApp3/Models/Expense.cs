using Newtonsoft.Json;
using SQLite;

namespace MauiApp3.Models
{
    public class Expense
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [JsonProperty("Description")]
        public string Description { get; set; } // Harcama açıklaması
        [JsonProperty("Amount")]
        public double Amount { get; set; }      // Tutar
        [JsonProperty("Date")]
        public DateTime Date { get; set; }      // Tarih
        [JsonProperty("Category")]
        public string Category { get; set; }    // Yemek, Ulaşım, Donanım vb.
    }
}