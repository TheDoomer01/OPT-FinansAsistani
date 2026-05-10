using MauiApp3.Services; // Servislerin olduğu klasörü kontrol et
using MauiApp3.Views;

namespace MauiApp3
{
    public partial class App : Application
    {
        // Global erişim için database'i static tutabilirsin
        public static DatabaseService Database { get; private set; }

        // MAUI bu metodu çalıştırırken ihtiyacı olan servisleri otomatik getirir
        public App(DatabaseService databaseService, GeminiService geminiService)
        {
            InitializeComponent();

            Database = databaseService;

            // KRİTİK NOKTA: 
            // 1. MainPage'i bir NavigationPage içine sarıyoruz (Üst barın görünmesi için).
            // 2. MainPage'e ihtiyacı olan iki servisi (Gemini ve Database) gönderiyoruz.
            MainPage = new NavigationPage(new MainPage(geminiService, databaseService));
        }
    }
}