using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using MauiApp3.Helpers;
using MauiApp3.Models;
using MauiApp3.Services;
using Newtonsoft.Json;
using Microcharts;
using SkiaSharp;
using System.Diagnostics;

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

        // DÜZELTME 1: Havada duran kodlar buraya, metodun içine alındı.
        var savedUni = UserSession.University;
        UniEntry.Text = string.IsNullOrEmpty(savedUni) ? "" : savedUni;

        Dispatcher.Dispatch(async () =>
        {
            await Task.Delay(500);
            UpdateChart();
            LoadHistory();
        });
    }

    void OnUniEntryUnfocused(object sender, FocusEventArgs e)
    {
        UserSession.University = UniEntry.Text;
        Preferences.Default.Remove("LastFinanceData");
    }

    private async void OnAddExpenseClicked(object sender, EventArgs e)
    {
        var popup = new AddExpensePopup(_databaseService);

        await this.ShowPopupAsync(popup);
        // Popup tamamen kapandıktan sonra bizim oluşturduğumuz bayrağı (IsSuccess) kontrol ediyoruz!
        if (popup.IsSuccess)
        {
            UpdateChart();
            await DisplayAlert("Harika", "Harcama başarıyla eklendi ve grafik güncellendi!", "OK");
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
                NewsLabel.Text = "• " + string.Join("\n\n• ", data.haberler);
            }
            else if (!string.IsNullOrEmpty(data.HaberlerMetni))
            {
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
        var selectedData = e.CurrentSelection.FirstOrDefault() as FinanceData;

        if (selectedData != null)
        {
            this.ShowPopup(new AnalysisPopup(selectedData));
            ((CollectionView)sender).SelectedItem = null;
        }
    }

    public async void UpdateChart()
    {
        try
        {
            // Veritabanının yazma işlemini tamamlaması için çok kısa bir bekleme (opsiyonel ama güvenlidir)
            await Task.Delay(100);

            var allExpenses = await _databaseService.GetExpensesAsync();

            if (allExpenses == null || !allExpenses.Any())
            {
                // UI işlemlerini ana thread'de yapalım
                MainThread.BeginInvokeOnMainThread(() => {
                    // Veri yoksa şık bir "Boş" halka çizdir
                    var emptyEntries = new List<ChartEntry> {
            new ChartEntry(1) { Color = SKColor.Parse("#E0E0E0") } // Açık gri bir halka
        };
                    ExpenseChartView.Chart = new DonutChart
                    {
                        Entries = emptyEntries,
                        BackgroundColor = SKColors.Transparent,
                        HoleRadius = 0.6f
                    };
                    if (TotalExpenseLabel != null) TotalExpenseLabel.Text = "0 ₺";
                });
                return;
            }

            var categoryTotals = allExpenses
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "DİĞER" : e.Category.Trim().ToUpper())
                .Select(g => new {
                Category = g.Key,
                Total = g.Sum(e => e.Amount)
                }).ToList();

            var entries = new List<ChartEntry>();
            string[] colors = { "#3498DB", "#E74C3C", "#2ECC71", "#F1C40F", "#9B59B6" };
            int colorIndex = 0;

            foreach (var item in categoryTotals)
            {
                entries.Add(new ChartEntry((float)item.Total)
                {
                    Label = item.Category,
                    ValueLabel = item.Total.ToString("N0") + " ₺",
                    Color = SKColor.Parse(colors[colorIndex % colors.Length])
                });
                colorIndex++;
            }

            // Toplamı hesapla
            var totalSpent = allExpenses.Sum(e => e.Amount);

            // ARAYÜZ GÜNCELLEMELERİ (Mutlaka MainThread içinde)
            MainThread.BeginInvokeOnMainThread(() => {
                TotalExpenseLabel.Text = totalSpent.ToString("N0") + " ₺";

                // Yeni grafik nesnesini oluştur ve ata
                ExpenseChartView.Chart = new DonutChart
                {
                    Entries = entries,
                    LabelTextSize = 35,
                    Typeface = SKTypeface.FromFamilyName("Arial"),
                    BackgroundColor = SKColors.White, 
                    LabelColor = SKColor.Parse("#333333"),  // Koyu gri/Siyah yazılar (ARTIK GÖRÜNECEK!)
                    HoleRadius = 0.6f
                };
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Grafik Hatası: {ex.Message}");
        }
    }

    private async void OnChartTapped(object sender, TappedEventArgs e)
    {
        // Veritabanından o anki toplam harcamayı hızlıca alalım
        var expenses = await _databaseService.GetExpensesAsync();

        if (expenses != null && expenses.Any())
        {
            var total = expenses.Sum(x => x.Amount);
            await DisplayAlert("Bütçe Dağılımı", $"Şu ana kadar toplam {total} TL harcama yaptınız.", "Tamam");
        }
        else
        {
            await DisplayAlert("Bilgi", "Henüz bir harcama girmediniz.", "Tamam");
        }
    }
}