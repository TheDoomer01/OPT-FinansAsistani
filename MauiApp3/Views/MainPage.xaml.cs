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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace MauiApp3.Views;

public partial class MainPage : ContentPage
{
    private readonly GeminiService _geminiService;
    private readonly DatabaseService _databaseService;
    private bool isExpanded = false;

    public ObservableCollection<FinanceData> HistoryItems { get; set; } = new();

    // Geçmiş öğelere tıklamayı algılayacak Komut (Command) eklendi
    public ICommand SelectHistoryCommand { get; set; }

    public MainPage(GeminiService geminiService, DatabaseService databaseService)
    {
        InitializeComponent();

        _geminiService = geminiService;
        _databaseService = databaseService;

        // Kullanıcı geçmişteki bir analize tıkladığında çalışacak kod parçası
        SelectHistoryCommand = new Command<FinanceData>((data) =>
        {
            if (data != null)
            {
                // Seçilen veriyi alıp Popup olarak ekranda gösteriyoruz
                this.ShowPopup(new AnalysisPopup(data));
            }
        });

        BindingContext = this;

        var savedUni = UserSession.University;
        UniEntry.Text = string.IsNullOrEmpty(savedUni) ? "" : savedUni;

        Dispatcher.Dispatch(async () =>
        {
            await Task.Delay(300);
            await UpdateChart();
            await LoadHistory();
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

        if (popup.IsSuccess)
        {
            await UpdateChart();
            await DisplayAlert("Harika", "Harcama başarıyla eklendi ve grafik güncellendi!", "OK");
        }
    }

    void OnSliderValueChanged(object sender, ValueChangedEventArgs e)
    {
        double value = Math.Round(e.NewValue);
        BudgetValueLabel.Text = $"{value:0} TL";

        // Değer değiştiğinde Text'i sadece eğer farklıysa güncelle, yoksa sonsuz döngüye girer
        if (BudgetEntry.Text != value.ToString())
            BudgetEntry.Text = value.ToString();
    }

    void OnBudgetEntryChanged(object sender, TextChangedEventArgs e)
    {
        if (double.TryParse(e.NewTextValue, out double val))
        {
            // Yazı değiştiğinde Slider'ı sadece eğer farklıysa güncelle
            if (BudgetSlider.Value != val)
                BudgetSlider.Value = val;
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

    private async Task<string> GetSpendingSummaryAsync()
    {
        var expenses = await _databaseService.GetExpensesAsync();
        if (expenses == null || !expenses.Any())
            return "Henüz hiç harcama girilmedi.";

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
            case 0: days = 1; break;
            case 1: days = 7; break;
            case 2: days = 30; break;
            case 3: days = 365; break;
        }

        await RefreshDataWithFilter(days);
    }

    async Task RefreshDataWithFilter(int days)
    {
        try
        {
            // 1. Veritabanından sadece seçilen tarih aralığındaki harcamaları çekiyoruz
            var filteredList = await _databaseService.GetSpendingsByDateAsync(days);

            if (filteredList == null) return;

            // 2. Grafiği ve hemen altındaki mini harcama listesini filtrelenmiş verilerle güncelliyoruz
            // UpdateChart metodu zaten kendi içinde UI güncellemelerini yapıyor.
            await UpdateChart(filteredList);

            // 3. Ortadaki Toplam Tutar etiketini UI (Arayüz) thread'inde güncelliyoruz
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // HATA DÜZELTİLDİ: HistoryItems listesine müdahale eden döngü buradan kaldırıldı!
                // Artık yapay zeka geçmiş analizleri harcamalarla karışmayacak.
                TotalExpenseLabel.Text = $"{filteredList.Sum(x => x.Amount):N0} ₺";
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", "Filtreleme sırasında bir sorun oluştu: " + ex.Message, "OK");
        }
    }
    async void OnAnalyzeClicked(object sender, EventArgs e)
    {
        var selectedBudget = Math.Round(BudgetSlider.Value);

        try
        {
            // 1. UI Hazırlığı (Zaten UI iş parçacığında tetiklendiği için güvenli)
            AnalyzeButton.IsEnabled = false;
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            await Task.Delay(1000);

            // 2. Verileri Topla
            string spendingData = await GetSpendingSummaryAsync();
            if (string.IsNullOrEmpty(spendingData)) spendingData = "Henüz harcama kaydı bulunmuyor.";

            string prompt = $"Sen bir üniversite finansal asistanısın. Kullanıcı öğrencisi. " +
                            $"Bugünkü bütçesi: {selectedBudget} TL. " +
                            $"Son harcamaları ve kategorileri: {spendingData}. " +
                            "Bu verilere dayanarak; harcamaların bütçeye oranını analiz et, öğrenciye özel tasarruf " +
                            "tavsiyeleri ve komik/ilginç finansal haber başlıkları üret. " +
                            "Cevabı SADECE şu JSON formatında ver: " +
                            "{ \"tavsiye\": \"...\", \"haberler\": [\"...\", \"...\", \"...\"] }";

            // 3. API Çağrısı
            var response = await _geminiService.GetResponseAsync(prompt);

            // KONTROL 1: API Cevap Vermezse
            if (string.IsNullOrWhiteSpace(response))
            {
                // Android güvenliği için UI kodlarını InvokeOnMainThreadAsync içine alıyoruz
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Bağlantı Hatası", "Yapay zeka cevap vermedi. İnternet iznini veya API Anahtarını kontrol et.", "Tamam");
                });
                return;
            }

            // KONTROL 2: Markdown Temizliği
            response = response.Replace("```json", "").Replace("```", "").Trim();
            int start = response.IndexOf("{");
            int end = response.LastIndexOf("}");

            if (start >= 0 && end >= 0)
            {
                string cleanJson = response.Substring(start, (end - start) + 1);
                var result = JsonConvert.DeserializeObject<FinanceData>(cleanJson);

                if (result != null)
                {
                    result.KayitTarihi = DateTime.Now;

                    // Veritabanı ve Önbellek Kayıtları
                    await _databaseService.AddAnalysisAsync(result);
                    Preferences.Default.Set("LastFinanceData", JsonConvert.SerializeObject(result));

                    // 4. BAŞARILI DURUM: Popup'ı ve UI'ı ana iş parçacığında güvenle aç!
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        UpdateUI(result);
                        this.ShowPopup(new AnalysisPopup(result));
                    });

                    _ = LoadHistory();
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlert("Dönüştürme Hatası", "Gelen veri okunamadı.", "Tamam");
                    });
                }
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Format Hatası", "JSON bulunamadı. Gelen cevap:\n" + response, "Tamam");
                });
            }
        }
        catch (Exception ex)
        {
            // 5. KRİTİK HATA YAKALAMA: Uygulamanın sessizce çökmesini engeller
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Sistem Hatası", "Kod çöktü: " + ex.Message, "Tamam");
            });
        }
        finally
        {
            // 6. HER DURUMDA UI SIFIRLAMA
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
                AnalyzeButton.IsEnabled = true;
            });
        }
    }
    void UpdateUI(FinanceData data)
    {
        if (data == null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AdviceLabel.Text = data.tavsiye ?? "Tavsiye boş.";

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
        // Ekrana çıkacak menüye "API Anahtarı Gir" seçeneği eklendi
        string action = await DisplayActionSheet("Ayarlar", "Vazgeç", null, "API Anahtarı Gir", "Verileri Sıfırla", "Hakkında");

        if (action == "Verileri Sıfırla")
        {
            await HandleClearSession();
        }
        else if (action == "API Anahtarı Gir")
        {
            // Yeni seçeneğe tıklandığında API girme metodunu çalıştırır
            await HandleSetApiKey();
        }
        else if (action == "Hakkında")
        {
            // Projenin kimliğini yansıtan şık bir hakkında bilgi kutusu
            await DisplayAlert("Hakkında", "ÖPT - Öğrenci Para Takip\nBursa Uludağ Üniversitesi \nBTK Hackathon 2026 Projesi", "Tamam");
        }
    }

    // 2. KULLANICIDAN API ANAHTARINI ALIP GÜVENLİ KASAYA KAYDEDEN METOT
    private async Task HandleSetApiKey()
    {
        // Önce kasada zaten kayıtlı bir anahtar var mı diye bakıyoruz
        string currentKey = await SecureStorage.Default.GetAsync("UserApiKey");

        // Ekrana yazılı girdi alabileceğimiz bir kutucuk (Prompt) çıkartıyoruz
        string result = await DisplayPromptAsync(
            "API Ayarları",
            "Gemini API anahtarınızı girin:",
            "Kaydet",
            "İptal",
            "AIzaSy...",
            -1,
            Keyboard.Text,
            currentKey);

        // Eğer kullanıcı boş bırakmadıysa ve iptale basmadıysa kaydet
        if (!string.IsNullOrWhiteSpace(result))
        {
            // Girdiği değeri telefonun güvenli kasasına (SecureStorage) kaydediyoruz
            await SecureStorage.Default.SetAsync("UserApiKey", result.Trim());
            await DisplayAlert("Başarılı", "API Anahtarınız telefona güvenle kaydedildi!", "Tamam");
        }
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

    private async Task LoadHistory()
    {
        var history = await _databaseService.GetAnalysesAsync();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            HistoryItems.Clear();

            if (history == null) return;

            // .Take(4) sınırını kaldırdık. 
            // Tüm listeyi tarihe göre azalan (en yeni en üstte) şekilde sıralayıp ekliyoruz.
            foreach (var item in history.OrderByDescending(x => x.KayitTarihi))
            {
                HistoryItems.Add(item);
            }
        });
    }

    private async Task UpdateChart(List<Expense> filterData = null)
    {
        try
        {
            await Task.Delay(100);

            var allExpenses = filterData ?? await _databaseService.GetExpensesAsync();

            if (allExpenses == null || !allExpenses.Any())
            {
                MainThread.BeginInvokeOnMainThread(() => {
                    var emptyEntries = new List<ChartEntry> {
                        new ChartEntry(1) { Color = SKColor.Parse("#E0E0E0") }
                    };
                    ExpenseChartView.Chart = new DonutChart
                    {
                        Entries = emptyEntries,
                        BackgroundColor = SKColors.Transparent,
                        LabelColor = SKColor.Parse("#333333"),
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

            var totalSpent = allExpenses.Sum(e => e.Amount);

            MainThread.BeginInvokeOnMainThread(() => {
                TotalExpenseLabel.Text = totalSpent.ToString("N0") + " ₺";
                ExpenseChartView.Chart = new DonutChart
                {
                    Entries = entries,
                    LabelTextSize = 20,
                    LabelMode = LabelMode.None,
                    Typeface = SKTypeface.FromFamilyName("Arial"),
                    BackgroundColor = SKColors.White,
                    LabelColor = SKColor.Parse("#333333"),
                    HoleRadius = 0.7f,
                    GraphPosition = GraphPosition.Center
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

    private async void OnScanReceiptClicked(object sender, EventArgs e)
    {
        OnAddExpenseClicked(sender, e);
    }

    private async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        OnAddExpenseClicked(sender, e);
    }
}