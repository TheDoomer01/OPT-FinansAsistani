using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using MauiApp3.Helpers;
using MauiApp3.Models;
using MauiApp3.Services;
using Newtonsoft.Json;

namespace MauiApp3.Views;

public partial class MainPage : ContentPage
{
    private readonly GeminiService _geminiService;
    private readonly DatabaseService _databaseService;
    private bool isExpanded = false;

    public MainPage(GeminiService geminiService, DatabaseService databaseService)
    {
        InitializeComponent();

        // Servisleri güvenli yuvalarına atıyoruz
        _geminiService = geminiService;
        _databaseService = databaseService;

        // Üniversite bilgisini çekiyoruz
        var savedUni = UserSession.University;
        UniEntry.Text = string.IsNullOrEmpty(savedUni) ? "" : savedUni;

        // Geçmişi yüklüyoruz
        LoadHistory();
    }

    void OnUniEntryUnfocused(object sender, FocusEventArgs e)
    {
        UserSession.University = UniEntry.Text;
        Preferences.Default.Remove("LastFinanceData");
    }

    private async void OnAddExpenseClicked(object sender, EventArgs e)
    {
        // ARTIK Handler.MauiContext KULLANMIYORUZ! Direkt _databaseService kullanıyoruz.
        var popup = new AddExpensePopup(_databaseService);

        object result = await this.ShowPopupAsync(popup);

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
            MiniBoard.Animate("Expand", x => MiniBoard.HeightRequest = x, 100, 350, length: 250,
                finished: (v, c) => MainThread.BeginInvokeOnMainThread(() => DetailsLayout.IsVisible = true));
        }
        else
        {
            DetailsLayout.IsVisible = false;
            MiniBoard.Animate("Collapse", x => MiniBoard.HeightRequest = x, 350, 100, length: 250);
        }
    }

    async void OnAnalyzeClicked(object sender, EventArgs e)
    {
        var selectedBudget = Math.Round(BudgetSlider.Value);

        // 1. KOTA DOSTU KONTROL
        string cachedJson = Preferences.Default.Get("LastFinanceData", string.Empty);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            var cachedData = JsonConvert.DeserializeObject<FinanceData>(cachedJson);
            if (cachedData != null && !string.IsNullOrEmpty(cachedData.tavsiye) &&
                (DateTime.Now - cachedData.KayitTarihi).TotalHours < 3)
            {
                UpdateUI(cachedData);
                return;
            }
        }

        try
        {
            // 1. Hazırlık: Rapor yazılarını gizle, yükleme simgesini aç
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            AnalyzeButton.IsEnabled = false;
            NewsLabel.Text = ""; // Eski raporu temizle

            string prompt = $"Sen bir üniversite finansal asistanısın. Kullanıcının bütçesi {selectedBudget} TL " +
                $"ve okuduğu okul: {UserSession.University}. " +
                $"Sadece ve sadece şu JSON formatında cevap ver: " +
                "{ \"tavsiye\": \"...\", \"haberler\": [\"...\", \"...\", \"...\"] }";

            var response = await _geminiService.GetResponseAsync(prompt);

            // BU SATIRI EKLE: Gemini'den gelen ham metni ekranda gör
            //await DisplayAlert("Gemini Ne Dedi?", response, "Tamam");

            int start = response.IndexOf("{");
            int end = response.LastIndexOf("}");

            if (start < 0)
            {
                NewsLabel.Text = "API Hatası: " + response;
                return;
            }

            response = response.Substring(start, (end - start) + 1);

            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            var result = JsonConvert.DeserializeObject<FinanceData>(response, settings);

            if (result == null) return;

            result.KayitTarihi = DateTime.Now;

            // 3. SQLITE KAYDI
            await _databaseService.AddAnalysisAsync(result);

            // 4. PREFERENCES GÜNCELLEME
            Preferences.Default.Set("LastFinanceData", JsonConvert.SerializeObject(result));

            // 5. ARAYÜZÜ GÜNCELLE
            if (result != null && !string.IsNullOrEmpty(result.tavsiye))
            {
                this.ShowPopup(new AnalysisPopup(result));
            }
            else
            {
                await DisplayAlert("Uyarı", "Gemini'den anlamlı bir analiz gelmedi. Lütfen tekrar deneyin.", "Tamam");
            }
            LoadHistory();

            this.ShowPopup(new AnalysisPopup(result));

        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("429"))
                await DisplayAlert("Kota Sınırı", "Dakikalık istek sınırına takıldık. Lütfen 1 dakika bekleyin.", "Tamam");
            else NewsLabel.Text = "Hata: " + ex.Message;
        }
        finally
        {
            // 4. Bitiş: Yükleme simgesini kapat, butonu aç
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            AnalyzeButton.IsEnabled = true;
        }
    }

    private async void LoadHistory()
    {
        try
        {
            var history = await _databaseService.GetAnalysesAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (HistoryList != null)
                {
                    HistoryList.ItemsSource = history;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Hata: {ex.Message}");
        }
    }

    void UpdateUI(FinanceData data)
    {
        if (data == null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AdviceLabel.Text = data.tavsiye ?? "Tavsiye boş.";

            // Haberleri Gösterme Mantığı
            if (data.haberler != null && data.haberler.Count > 0)
            {
                // Eğer Gemini'den yeni geldiyse (Liste dolu)
                NewsLabel.Text = "• " + string.Join("\n\n• ", data.haberler);
            }
            else if (!string.IsNullOrEmpty(data.HaberlerMetni))
            {
                // Eğer Veritabanından (SQLite) geldiyse (Metin dolu)
                var haberListesi = data.HaberlerMetni.Split('|');
                NewsLabel.Text = "• " + string.Join("\n\n• ", haberListesi);
            }
            else
            {
                NewsLabel.Text = "Haber listesi boş.";
            }

            BoardTitle.Text = $"📌 {UserSession.University} Finans Özeti ({data.KayitTarihi:HH:mm})";
            DetailsLayout.IsVisible = true;
            MiniBoard.HeightRequest = -1;
            isExpanded = true;
        });
    }

    async void OnSettingsClicked(object sender, EventArgs e)
    {
        string action = await DisplayActionSheet("Ayarlar", "Vazgeç", null, "Verileri Sıfırla", "Hakkında");
        if (action == "Verileri Sıfırla") await HandleClearSession();
    }

    private async Task HandleClearSession()
    {
        if (await DisplayAlert("Dikkat", "Tüm veriler silinsin mi?", "Evet", "Hayır"))
        {
            UserSession.ClearSession();
            Preferences.Default.Remove("LastFinanceData");
            UniEntry.Text = string.Empty;
            await DisplayAlert("Başarılı", "Sıfırlandı.", "Tamam");
        }
    }
    private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Seçilen öğeyi FinanceData olarak al
        var selectedData = e.CurrentSelection.FirstOrDefault() as FinanceData;

        if (selectedData != null)
        {

            this.ShowPopup(new AnalysisPopup(selectedData));
            ((CollectionView)sender).SelectedItem = null;

            // Görsel geri bildirim: Sayfayı yukarı kaydır (Analiz kutusuna odaklan)
            // MainScrollView.ScrollToAsync(0, 0, true); 
        }
    }
}