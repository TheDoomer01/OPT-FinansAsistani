using MauiApp3.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MauiApp3
{
    public partial class App : Application
    {
        public static DatabaseService Database { get; private set; }

        // Sadece tek bir yapıcı metot (constructor) bıraktık
        public App(DatabaseService databaseService)
        {
            InitializeComponent();

            // Gelen servisi static değişkene aktaralım
            Database = databaseService;

            // DİKKAT: MainPage = new AppShell(); satırı buradan SİLİNDİ!
        }

        // Uygulamanın penceresini başlatan yegane doğru yer burası
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        // DİKKAT: En alttaki ikinci ve boş olan public App() metodu tamamen SİLİNDİ!
    }
}