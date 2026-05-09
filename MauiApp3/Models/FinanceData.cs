using System.Collections.Generic;

namespace MauiApp3.Models
{
    public class FinanceData
    {
        public string tavsiye { get; set; }  // Tamamen küçük harf
        public List<string> haberler { get; set; } // Tamamen küçük harf
        public System.DateTime KayitTarihi { get; set; }
    }
}