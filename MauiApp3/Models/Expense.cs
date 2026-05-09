using SQLite;

namespace MauiApp3.Models
{
    public class Expense
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Description { get; set; } // Harcama açıklaması
        public double Amount { get; set; }      // Tutar
        public DateTime Date { get; set; }      // Tarih
        public string Category { get; set; }    // Yemek, Ulaşım, Donanım vb.
    }
}