using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using MauiApp3.Helpers;
using MauiApp3.Models;
using MauiApp3.Services; // Servisi tanıtmak için
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace MauiApp3.Views;

public partial class MainPage : ContentPage
{
    // 1. GeminiService nesnesini burada tanımla
    private readonly GeminiService _geminiService = new GeminiService();
    private bool isExpanded = false;

    public MainPage()
    {
        InitializeComponent();
        // Eğer hafıza boşsa kutucuk boş kalsın (Placeholder görünecek)
        var savedUni = UserSession.University;
        UniEntry.Text = string.IsNullOrEmpty(savedUni) ? "" : savedUni;
    }

    // Kullanıcı yazdığı an hafızaya kaydet
    void OnUniEntryUnfocused(object sender, FocusEventArgs e)
    {
        // Artık doğrudan yardımcı sınıfımızı kullanıyoruz
        UserSession.University = UniEntry.Text;

        // Okul değiştiği için eski bütçe tavsiyelerini temizliyoruz
        Preferences.Default.Remove("LastFinanceData");
    }

    // 2. Yeni metodları buraya ekle

    private async void OnAddExpenseClicked(object sender, EventArgs e)
    {
        var dbService = Handler.MauiContext.Services.GetService<DatabaseService>();
        var popup = new AddExpensePopup(dbService);

        // Sonucu 'object' olarak alıyoruz, böylece IPopupResult kavgası bitiyor
        object result = await this.ShowPopupAsync(popup);

        // Mermi geçirmez kontrol: Sonuç null değilse ve "true" değerine eşitse
        if (result != null && result.Equals(true))
        {
            await DisplayAlert("Tamam", "Harcama kaydedildi!", "OK");
        }
    }
    void OnSliderValueChanged(object sender, ValueChangedEventArgs e)
    {
        BudgetValueLabel.Text = $"{Math.Round(e.NewValue)} TL";
    }

    async void OnBoardTapped(object sender, EventArgs e)
    {
        isExpanded = !isExpanded;

        if (isExpanded)
        {
            // 'await' kaldırıldı çünkü Animate void döner.
            // Animasyon bitince (finished) detayları görünür yapıyoruz.
            MiniBoard.Animate("Expand", x => MiniBoard.HeightRequest = x, 100, 350, length: 250,
                finished: (v, c) => MainThread.BeginInvokeOnMainThread(() => DetailsLayout.IsVisible = true));
        }
        else
        {
            // Küçülürken önce detayları gizlemek daha şık durur
            DetailsLayout.IsVisible = false;
            MiniBoard.Animate("Collapse", x => MiniBoard.HeightRequest = x, 350, 100, length: 250);
        }
    }

    async void OnAnalyzeClicked(object sender, EventArgs e)
    {
        var selectedBudget = Math.Round(BudgetSlider.Value);

        // 1. Önce hafızayı kontrol et (Kota dostu sistem)
        string cachedJson = Preferences.Default.Get("LastFinanceData", string.Empty);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            var cachedData = JsonConvert.DeserializeObject<FinanceData>(cachedJson);
            if ((DateTime.Now - cachedData.KayitTarihi).TotalHours < 1)
            {
                UpdateUI(cachedData);
                return;
            }
        }

        // AMAÇ: Eğer hafızada veri yoksa veya 1 saatten eskiyse, Gemini'den yeni veri iste.
        try
        {
            NewsLabel.Text = "Gemini 2.5 Flash analiz ediyor...";

            // Gemini'ye "Bana sadece JSON ver" diye kesin bir komut veriyoruz.
            string userUni = Preferences.Default.Get("UserUniversity", "herhangi bir üniversite");
            var budget = Math.Round(BudgetSlider.Value);

            string prompt = $"Sen bir üniversite finansal asistanısın. Kullanıcının bütçesi {selectedBudget} TL " +
                $"ve okuduğu okul: {UserSession.University}. " +
                $"Tavsiyelerini verirken bu üniversitenin bulunduğu şehrin yaşam maliyetlerini, " +
                $"varsa o bölgedeki popüler öğrenci semtlerini ve öğrenci harcamalarını (ulaşım, yemekhane) dikkate al. " +
                $"Ayrıca kullanıcının bölümüne uygun kariyer odaklı küçük yatırım veya tasarruf ipuçları ver. " +
                "Sadece ve sadece şu JSON formatında cevap ver, başka açıklama ekleme: " +
                "{ \"tavsiye\": \"...\", \"haberler\": [\"...\", \"...\", \"...\"] }";

            var response = await _geminiService.GetResponseAsync(prompt);

            // GEÇİCİ OLARAK ŞUNU EKLE:
            //await DisplayAlert("Gemini Ne Dedi?", response, "Tamam");

            // JSON dışındaki gereksiz karakterleri (```json gibi) temizliyoruz
            int start = response.IndexOf("{");
            int end = response.LastIndexOf("}");

            if (start >= 0 && end > start)
            {
                response = response.Substring(start, (end - start) + 1);
            }

            // Deserialization (Metni nesneye çevirme) ayarlarını yapıyoruz
            var settings = new JsonSerializerSettings

            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            // Metni FinanceData modeline dönüştürüyoruz
            var result = JsonConvert.DeserializeObject<FinanceData>(response, settings);

            // Şu küçük kontrol hayat kurtarır:
            if (result == null)
            {
                NewsLabel.Text = "Veri ayrıştırılamadı, lütfen tekrar deneyin.";
                return;
            }

            // Şu anki saati damgala
            result.KayitTarihi = DateTime.Now;

            // AMAÇ: Yeni gelen ve işlenen bu veriyi bir sonraki açılışta kullanmak için Preferences'a kaydet.
            string jsonToSave = JsonConvert.SerializeObject(result);
            Preferences.Default.Set("LastFinanceData", jsonToSave);

            // Ekrandaki kutucukları doldur
            UpdateUI(result);
        }
        catch (Exception ex)
        {
            NewsLabel.Text = "Hata oluştu: " + ex.Message;
        }
    }

    async void OnSettingsClicked(object sender, EventArgs e)
    {
        // Kullanıcıya seçenek sunuyoruz
        string action = await DisplayActionSheet("Ayarlar", "Vazgeç", null, "Verileri Sıfırla", "Hakkında");

        switch (action)
        {
            case "Verileri Sıfırla":
                await HandleClearSession();
                break;
            case "Hakkında":
                await DisplayAlert("Bilgi", "Bu uygulama bir öğrenci projesi olarak geliştirilmiştir.", "Tamam");
                break;
        }
    }

    // Silme işlemini ayrı bir metodda topladık (Kod temizliği!)
    private async Task HandleClearSession()
    {
        bool confirm = await DisplayAlert("Dikkat", "Tüm verileriniz silinecek, emin misiniz?", "Evet", "Hayır");

        if (confirm)
        {
            // 1. Hafızayı temizle
            UserSession.ClearSession();
            Preferences.Default.Remove("LastFinanceData");

            // 2. Arayüzü temizle
            UniEntry.Text = string.Empty;
            NewsLabel.Text = "Veriler temizlendi.";
            AdviceLabel.Text = "Veriler temizlendi.";
            BoardTitle.Text = "📌 Finansal Özet (Tıkla ve Gör)";

            // Kutuyu kapat
            isExpanded = false;
            DetailsLayout.IsVisible = false;
            MiniBoard.HeightRequest = 100;

            await DisplayAlert("Başarılı", "Her şey fabrika ayarlarına döndü.", "Tamam");
        }
    }

    // AMAÇ: Ekrandaki Label'ları doldurmak için kod tekrarını önleyen yardımcı metod.
    void UpdateUI(FinanceData data)
    {
        if (data == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Modelde isimleri küçük harf yaptığımız için burada da küçük harf kullanıyoruz
            AdviceLabel.Text = data.tavsiye ?? "Tavsiye boş geldi.";

            if (data.haberler != null && data.haberler.Count > 0)
                NewsLabel.Text = "• " + string.Join("\n\n• ", data.haberler);
            else
                NewsLabel.Text = "Haber listesi boş.";

            BoardTitle.Text = $"📌 {UserSession.University} Finans Özeti ({data.KayitTarihi:HH:mm})";

            DetailsLayout.IsVisible = true;
            MiniBoard.HeightRequest = -1; // -1, içeriğe göre otomatik büyümesini sağlar (Kritik!)
            isExpanded = true;
        });
    }
}
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit() // <-- ensure this is present
            .ConfigureFonts(fonts =>
            {
                // ... existing font registrations ...
            });

        // ... other registrations ...

        return builder.Build();
    }
}
