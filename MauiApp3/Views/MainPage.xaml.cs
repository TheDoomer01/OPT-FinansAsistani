using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using MauiApp3.Helpers;
using MauiApp3.Models;
using MauiApp3.Services;
using Microcharts;
using Microsoft.Maui.Controls.Shapes;
using Newtonsoft.Json;
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

    // --- ÖNCE BU YARDIMCI METODU EKLE (OnAnalyzeClicked metodunun dışına) ---
    private async Task<string> GetSpendingSummaryAsync()
    {
        var expenses = await _databaseService.GetExpensesAsync();
        if (expenses == null || !expenses.Any())
            return "Henüz hiç harcama girilmedi.";

        // Son 10 harcamayı Gemini'nin anlayacağı bir metne çeviriyoruz
        var summary = expenses.OrderByDescending(x => x.Date).Take(10)
            .Select(x => $"{x.Category}: {x.Amount}TL")
            .ToList();

        return string.Join(", ", summary);
    }
    // --- ŞİMDİ ANA METODUN GÜNCEL HALİ ---
    async void OnAnalyzeClicked(object sender, EventArgs e)
    {
        var clickedButton = sender as Button; // Hangi butona basıldığını anlıyoruz
        var selectedBudget = Math.Round(BudgetSlider.Value);

        // 1. KOTA DOSTU KONTROL (Cache mantığı aynı kalıyor)
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
            // --- 1. BUTONU KİLİTLE (Tekrar basmayı engeller) ---
            AnalyzeButton.IsEnabled = false;
            if (clickedButton != null) clickedButton.IsEnabled = false;            
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;                        
            
            NewsLabel.Text = "";

            string prompt = "";

            await Task.Delay(3000);

            // İŞTE BURASI: BUTONA GÖRE PROMPT BELİRLİYORUZ
            if (clickedButton != null && clickedButton.Text == "Analiz Et")
            {
                // Veritabanındaki gerçek verileri çekiyoruz
                string spendingData = await GetSpendingSummaryAsync();

                prompt = $"Sen bir üniversite finansal asistanısın. Kullanıcının bütçesi {selectedBudget} TL, " +
                         $"okuduğu okul: {UserSession.University} ve son harcamaları şunlar: {spendingData}. " +
                         $"Bu harcamaları analiz ederek öğrenciye tasarruf önerileri ver. " +
                         $"Sadece ve sadece şu JSON formatında cevap ver: " +
                         "{ \"tavsiye\": \"...\", \"haberler\": [\"...\", \"...\", \"...\"] }";
            }
            else
            {
                // Genel Tavsiye Modu
                prompt = $"Sen bir üniversite finansal asistanısın. Kullanıcının bütçesi {selectedBudget} TL " +
                         $"ve okuduğu okul: {UserSession.University}. " +
                         $"Sadece ve sadece şu JSON formatında cevap ver: " +
                         "{ \"tavsiye\": \"...\", \"haberler\": [\"...\", \"...\", \"...\"] }";
            }

            // Gemini Servis Çağrısı
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
                await DisplayAlert("Uyarı", "Gemini'den anlamlı bir analiz gelmedi.", "Tamam");
            }
            LoadHistory();
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("429"))
                await DisplayAlert("Kota Sınırı", "Lütfen 1 dakika bekleyin.", "Tamam");
            else
                NewsLabel.Text = "Hata: " + ex.Message;
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            AnalyzeButton.IsEnabled = true;
            if (clickedButton != null) clickedButton.IsEnabled = true;
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
            MiniExpenseList.Children.Clear();

            var recentExpenses = allExpenses.OrderByDescending(x => x.Date).Take(5);

            foreach (var exp in recentExpenses)
            {
                var frame = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = 5 },
                    Padding = 5,
                    Content = new VerticalStackLayout
                    {
                        Children = {
                    new Label { Text = exp.Category, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black },
                    new Label { Text = exp.Amount.ToString("C"), FontSize = 10, TextColor = Colors.DarkGreen }
                }
                    }
                };
                MiniExpenseList.Children.Add(frame);
            }
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