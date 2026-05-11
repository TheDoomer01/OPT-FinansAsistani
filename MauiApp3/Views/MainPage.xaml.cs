using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using MauiApp3.Helpers;
using MauiApp3.Models;
using MauiApp3.Services;
using Microcharts;
using Microcharts.Maui;
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
        var popup = new AddExpensePopup(_databaseService, _geminiService);
        await this.ShowPopupAsync(popup);
        // Popup tamamen kapandıktan sonra bizim oluşturduğumuz bayrağı (IsSuccess) kontrol ediyoruz!
        if (popup.IsSuccess)
        {
            await UpdateChart();
            await DisplayAlert("Harika", "Harcama başarıyla eklendi ve grafik güncellendi!", "OK");
        }
    }

    void OnSliderValueChanged(object sender, ValueChangedEventArgs e)
    {
        double stepValue = Math.Round(e.NewValue);
        BudgetSlider.Value = stepValue; // Tam sayıya yuvarla

        // Entry'nin TextChanged olayını tetiklememesi için kontrol edebiliriz
        if (BudgetEntry.Text != stepValue.ToString())
        {
            BudgetEntry.Text = stepValue.ToString();
        }
    }

    void OnBudgetEntryChanged(object sender, TextChangedEventArgs e)
    {
        if (double.TryParse(e.NewTextValue, out double result))
        {
            // Slider limitleri içindeyse güncelle
            if (result >= BudgetSlider.Minimum && result <= BudgetSlider.Maximum)
            {
                BudgetSlider.Value = result;
            }
        }
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

    async void OnFilterChanged(object sender, EventArgs e)
    {
        var picker = (Picker)sender;
        int selectedIndex = picker.SelectedIndex;

        int days = 0;
        switch (selectedIndex)
        {
            case 0: days = 1; break;  // Bugün
            case 1: days = 7; break;  // 1 Hafta
            case 2: days = 30; break; // 1 Ay
            case 3: days = 365; break; // Tümü
        }

        // Filtreye göre verileri tekrar çek ve grafiği güncelle
        await RefreshDataWithFilter(days);
    }

    async Task RefreshDataWithFilter(int days)
    {
        try
        {
            // 1. Veritabanından filtrelenmiş listeyi çek
            var filteredList = await _databaseService.GetSpendingsByDateAsync(days);

            if (filteredList == null) return;

            // 2. Grafiği bu listeye göre tekrar çiz
            // Not: UpdateChart 'Task' olduğu için await kullanmalısın
            await UpdateChart(filteredList);

            // 3. Alt taraftaki geçmiş listesini güncelle
            // 'ItemsSource'un başına HistoryList ekledik
            if (HistoryList != null)
            {
                HistoryList.ItemsSource = filteredList.OrderByDescending(x => x.Date).ToList();
            }

            // 5. Toplam tutarı yeniden hesapla
            double total = filteredList.Sum(x => x.Amount);
            if (TotalExpenseLabel != null)
            {
                TotalExpenseLabel.Text = $"{total:N0} ₺";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", "Filtreleme yapılamadı: " + ex.Message, "Tamam");
        }
    }

    // --- ŞİMDİ ANA METODUN GÜNCEL HALİ ---
    async void OnAnalyzeClicked(object sender, EventArgs e)
    {
        var selectedBudget = Math.Round(BudgetSlider.Value);

        // 1. KOTA/HIZ KONTROLÜ (Cache)
        // Eğer son 1 saat içinde bir analiz yapıldıysa, API'yi yormadan eskiyi getir
        string cachedJson = Preferences.Default.Get("LastFinanceData", string.Empty);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            var cachedData = JsonConvert.DeserializeObject<FinanceData>(cachedJson);
            // Eğer üzerinden 1 saat geçmediyse ve veritabanına yeni harcama eklenmediyse bunu göster
            // (Geliştirme aşamasında burayı yorum satırı yapabilirsin ki her seferinde yeni cevap gelsin)
            /*
            if (cachedData != null && (DateTime.Now - cachedData.KayitTarihi).TotalHours < 1)
            {
                this.ShowPopup(new AnalysisPopup(cachedData));
                return;
            }
        }
            */
            try
            {
                // UI Hazırlığı
                AnalyzeButton.IsEnabled = false;
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                await Task.Delay(2000);

                // 2. VERİLERİ TOPLA (İşte kritik nokta burası!)
                // GetSpendingSummaryAsync metodun veritabanından harcamaları string olarak getiriyor olmalı
                string spendingData = await GetSpendingSummaryAsync();

                if (string.IsNullOrEmpty(spendingData)) spendingData = "Henüz harcama kaydı bulunmuyor.";

                // 3. TEK VE GÜÇLÜ PROMPT
                string prompt = $"Sen bir üniversite finansal asistanısın. Kullanıcı {UserSession.University} öğrencisi. " +
                                $"Bugünkü bütçesi: {selectedBudget} TL. " +
                                $"Son harcamaları ve kategorileri: {spendingData}. " +
                                "Bu verilere dayanarak; harcamaların bütçeye oranını analiz et, öğrenciye özel tasarruf " +
                                "tavsiyeleri ve komik/ilginç finansal haber başlıkları üret. " +
                                "Cevabı SADECE şu JSON formatında ver: " +
                                "{ \"tavsiye\": \"...\", \"haberler\": [\"...\", \"...\", \"...\"] }";

                // 4. GEMINI SERVİS ÇAĞRISI
                var response = await _geminiService.GetResponseAsync(prompt);

                // JSON Ayıklama
                int start = response.IndexOf("{");
                int end = response.LastIndexOf("}");

                if (start >= 0 && end >= 0)
                {
                    response = response.Substring(start, (end - start) + 1);
                    var result = JsonConvert.DeserializeObject<FinanceData>(response);

                    if (result != null)
                    {
                        result.KayitTarihi = DateTime.Now;

                        UpdateUI(result); // Bu satırı eklediğin an UpdateUI kodu parlayacak!

                        // 5. KAYITLAR (SQLite ve Cache)
                        await _databaseService.AddAnalysisAsync(result);
                        Preferences.Default.Set("LastFinanceData", JsonConvert.SerializeObject(result));

                        // 6. GÖSTERİM
                        this.ShowPopup(new AnalysisPopup(result));
                        LoadHistory(); // Ana sayfadaki listeyi tazele
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Hata", "Analiz sırasında bir sorun oluştu: " + ex.Message, "Tamam");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
                AnalyzeButton.IsEnabled = true;
            }
        }
    }
    // 'List<Expense> filterData = null' ekleyerek dışarıdan veri alabilir hale getirdik
    private async Task LoadHistory()
    {
        try
        {
            // 1. Eğer dışarıdan filtreli veri gelmişse onu kullan, 
            // yoksa veritabanından ANALİZLERİ (veya harcamaları) çek.
            var history = await _databaseService.GetAnalysesAsync();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (HistoryList != null)
                {
                    // Eğer liste boşsa
                    if (history == null || !history.Any())
                    {
                        HistoryList.ItemsSource = null;
                        // NoHistoryLabel.IsVisible = true; // Eğer böyle bir label'ın varsa
                    }
                    else
                    {
                        // Analizleri tarih sırasına göre basıyoruz
                        HistoryList.ItemsSource = history.OrderByDescending(x => x.KayitTarihi).ToList();
                        // NoHistoryLabel.IsVisible = false;
                    }
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

    private async Task UpdateChart(List<Expense> filterData = null)
    {
        try
        {
            // Veritabanının yazma işlemini tamamlaması için çok kısa bir bekleme (opsiyonel ama güvenlidir)
            await Task.Delay(100);

            var allExpenses = filterData ?? await _databaseService.GetExpensesAsync();

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
                        LabelColor = SKColor.Parse("#333333"), // Yazıları gerekirse Colors.White yapabilirsin
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
                    LabelTextSize = 20,
                    // EKSTRA: Etiketlerin duruşunu değiştirerek çakışmayı azaltabilirsin
                    // LabelMode.Horizontal (Yatay) veya LabelMode.None (Hiç gösterme)
                    LabelMode = LabelMode.None,
                    Typeface = SKTypeface.FromFamilyName("Arial"),
                    BackgroundColor = SKColors.White, 
                    LabelColor = SKColor.Parse("#333333"),  // Koyu gri/Siyah yazılar (ARTIK GÖRÜNECEK!)
                    HoleRadius = 0.7f, // İçerideki boşluğu biraz artırırsan daha modern durur
                    GraphPosition = GraphPosition.Center // Grafiği merkeze hizalar
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
    // Üst bardaki 'Tara' butonuna basınca çalışır
    private async void OnScanReceiptClicked(object sender, EventArgs e)
    {
        // Toolbar'dan tıklandığında direkt Harcama Ekle popup'ını açıyoruz
        OnAddExpenseClicked(sender, e);
    }

    // Üst bardaki 'Galeri' butonuna basınca çalışır
    private async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        // Aynı şekilde popup'ı açıyoruz
        OnAddExpenseClicked(sender, e);
    }

    private async void OnCategorySelected(object sender, EventArgs e)
    {
        var picker = (Picker)sender;
        int selectedIndex = picker.SelectedIndex;

        if (selectedIndex != -1)
        {
            string selectedCategory = picker.Items[selectedIndex];

            // 1. Miktarın girilip girilmediğini kontrol et
            if (string.IsNullOrWhiteSpace(BudgetEntry.Text))
            {
                await DisplayAlert("Uyarı", "Lütfen önce harcama miktarını girin.", "Tamam");
                picker.SelectedIndex = -1; // Seçimi sıfırla ki tekrar seçebilsin
                return;
            }

            if (double.TryParse(BudgetEntry.Text, out double amount))
            {
                // 2. Yeni harcama nesnesini oluştur (Açıklama boş kalabilir)
                var newExpense = new Expense
                {
                    Amount = amount,
                    Category = selectedCategory,
                    Description = "Hızlı Kayıt", // Veya string.Empty diyebilirsin
                    Date = DateTime.Now
                };

                // 3. Veritabanına kaydet
                await _databaseService.AddExpenseAsync(newExpense);

                // 4. UI'ı güncelle (Grafik ve Liste)
                await UpdateChart();
                await LoadHistory();

                // 5. Giriş alanlarını temizle
                BudgetEntry.Text = string.Empty;
                picker.SelectedIndex = -1;

                // Küçük bir geri bildirim (isteğe bağlı)
                // await DisplayAlert("Başarılı", $"{selectedCategory} harcaması kaydedildi.", "Tamam");
            }
        }
    }

}