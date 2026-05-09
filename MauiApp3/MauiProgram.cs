using CommunityToolkit.Maui;
using MauiApp3.Services;
using MauiApp3.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace MauiApp3
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            // Secrets.json dosyasını yapılandırmaya ekliyoruz
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            builder.Configuration.AddUserSecrets(assembly, optional: true);
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // BURAYI EKLE: DatabaseService'i Singleton (tekil örnek) olarak tanımlıyoruz
            builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
            builder.Services.AddSingleton<GeminiService>();
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddTransient<MainPage>(); // MainPage'i de kayıt et

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
