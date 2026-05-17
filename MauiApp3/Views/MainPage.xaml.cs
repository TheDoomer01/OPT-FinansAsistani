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
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Windows.Input;

namespace MauiApp3.Views;

public partial class MainPage : ContentPage
{
    private readonly GeminiService _geminiService;
    private readonly DatabaseService _databaseService;
    private bool isExpanded = false;
    // Sınıfın en üstüne, metotların dışına ekle:
    private FileResult _selectedStatementFile;

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

    /* private async Task<string> GetSpendingSummaryAsync()
    {
        var expenses = await _databaseService.GetExpensesAsync();
        if (expenses == null || !expenses.Any())
            return "Henüz hiç harcama girilmedi.";

        var summary = expenses.OrderByDescending(x => x.Date).Take(10)
            .Select(x => $"{x.Category}: {x.Amount}TL")
            .ToList();

        return string.Join(", ", summary);
    }
    */
    

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
            // 1. UI Hazırlığı (Senin orijinal yapın)
            AnalyzeButton.IsEnabled = false;
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            await Task.Delay(1000);

            // 2. Verileri Topla (Tek değişiklik: Gemini'ye açıklamayı da fırlatıyoruz)
            var expenses = await _databaseService.GetExpensesAsync();
            string spendingData = "Henüz harcama bulunmuyor.";

            if (expenses != null && expenses.Any())
            {
                // Gemini'nin "Diğer"i anlaması için Kategori yanına (Açıklamayı) ekledik
                spendingData = string.Join(", ", expenses.Select(x => $"{x.Category} ({x.Description}): {x.Amount}TL"));
            }

            // Hafızadan üniversite ismini çekiyoruz
            string universityName = string.IsNullOrWhiteSpace(UserSession.University) ? "Üniversite" : UserSession.University;

            // 3. SENİN İLK PROMPTUN (Sadece Üniversite ismi ve küçük bir "açıklama" notu eklendi)
            string prompt = $"Sen bir üniversite finansal asistanısın. Kullanıcı {universityName} öğrencisi. " +
                            $"Bugünkü bütçesi: {selectedBudget} TL. " +
                            $"Son harcamaları ve kategorileri: {spendingData}. " +
                            $"Bu verilere dayanarak; harcamaların bütçeye oranını analiz et, öğrenciye özel {universityName} odaklı tasarruf " +
                            "tavsiyeleri ve finansal haber başlıkları bul. (Not: Kategori 'Diğer' ise parantez içindeki açıklamayı da analiz et). " +
                            "Cevabı SADECE şu JSON formatında ver: " +
                            "{ \"tavsiyeler\": \"...\", \"haberler\": [\"...\", \"...\", \"...\"] }";

            // 4. API Çağrısı ve JSON Çözme (Arada hiçbir C# filtresi veya dayatması yok)
            var response = await _geminiService.GetResponseAsync(prompt);

            // 🛑 EMNİYET KİLİDİ 1: Cevap tamamen boşsa
            if (string.IsNullOrWhiteSpace(response))
            {
                await MainThread.InvokeOnMainThreadAsync(async () => {
                    await DisplayAlert("API Hatası", "Yapay zekadan boş cevap döndü. İnterneti kontrol edin.", "Tamam");
                });
                return;
            }

            // 🛑 EMNİYET KİLİDİ 2: Kota sınırına (429) yakalandıysak sessizce geçme, uyar!
            if (response.Contains("429") || response.ToLower().Contains("quota") || response.ToLower().Contains("exhausted"))
            {
                await MainThread.InvokeOnMainThreadAsync(async () => {
                    await DisplayAlert("Kota Sınırı (429)", "Gemini ücretsiz kullanım kotanız doldu. Lütfen 1-2 dakika bekleyip tekrar deneyin. ⏳", "Tamam");
                });
                return;
            }

            response = response.Replace("```json", "").Replace("```", "").Trim();
            int start = response.IndexOf("{");
            int end = response.LastIndexOf("}");

            if (start >= 0 && end >= 0)
            {
                string cleanJson = response.Substring(start, (end - start) + 1);
                var result = JsonConvert.DeserializeObject<FinanceData>(cleanJson);

                if (result != null)
                {
                    var jObj = Newtonsoft.Json.Linq.JObject.Parse(cleanJson);
                    result.tavsiye = jObj["tavsiyeler"]?.ToString() ?? jObj["Tavsiyeler"]?.ToString() ?? jObj["tavsiye"]?.ToString() ?? result.tavsiye;
                    result.haberler = jObj["haberler"]?.ToObject<List<string>>() ?? jObj["Haberler"]?.ToObject<List<string>>() ?? result.haberler;
                    result.KayitTarihi = DateTime.Now;

                    // Veritabanı ve Önbellek Kayıtları
                    await _databaseService.AddAnalysisAsync(result);
                    Preferences.Default.Set("LastFinanceData", JsonConvert.SerializeObject(result));

                    // UI Güncelleme ve Popup Açılışı
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        // Ekrandaki label'lara temiz veriyi basıyoruz
                        if (AdviceLabel != null) AdviceLabel.Text = result.tavsiye;
                        if (NewsLabel != null && result.haberler != null) NewsLabel.Text = "• " + string.Join("\n• ", result.haberler);

                        UpdateUI(result);
                        this.ShowPopup(new AnalysisPopup(result));
                    });

                    _ = LoadHistory();
                }
            }
            else
            {
                // 🛑 EMNİYET KİLİDİ 3: Format bozuk geldiyse ne geldiğini ekrana bas ki görelim
                await MainThread.InvokeOnMainThreadAsync(async () => {
                    await DisplayAlert("Format Hatası", "Yapay zeka geçerli bir JSON döndüremedi. Gelen ham veri:\n" + response, "Tamam");
                });
            }
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () => {
                await DisplayAlert("Sistem Hatası", ex.Message, "Tamam");
            });
        }
        finally
        {
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

    // AYARLAR MENÜSÜ (ONAY KUTUCUKLARI EKLENDİ)
    async void OnSettingsClicked(object sender, EventArgs e)
    {
        // YENİ: Tüm menü işlemlerini güvenlik çemberine (try) alıyoruz
        try
        {
            string action = await DisplayActionSheet(
                "Ayarlar",
                "Menüyü Kapat", // Alttan açılan menüyü kapatmak için ana vazgeç butonu
                null,
                "API Anahtarı Gir",
                "Verileri Sıfırla",
                "API Anahtarını Sil",
                "Hakkında");

            if (action == "Verileri Sıfırla")
            {
                // Kullanıcıya onay soruyoruz.
                bool isConfirmed = await DisplayAlert("Dikkat", "Tüm harcama verileri ve üniversite adınız silinsin mi?", "Evet, Sıfırla", "Vazgeç");

                if (isConfirmed)
                {
                    // 1. ADIM: Mevcut silme metodun aynen çalışıyor (Verileri arkada uçurur)
                    await HandleClearSession();

                    // 2. ADIM: YENİ EKLENEN KISIM (Grafikleri ve ekranı anlık sıfırlama)
                    // .NET MAUI'de ekranın donmasını engelleyen ana iplik tetikleyicisi
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        // Ana grafiği güncelle (veritabanı temizlendiği için grafik de sıfırlanacak)
                        await UpdateChart();

                        // Trend grafiğine bomboş bir liste göndererek onu da anlık olarak temizle
                        var bosListe = new List<Expense>();
                        await UpdateTrendChart(bosListe, false);

                        // NOT: Eğer MainPage üzerinde toplam harcamayı yazdırdığın bir Label varsa 
                        // onun da eski veride kalmaması için elinle sıfırlayabilirsin. Örnek:
                        // ToplamTutarLabel.Text = "0 ₺";
                    });
                }
            }
            else if (action == "API Anahtarını Sil")
            {
                // YENİ: API anahtarını silmeden önce sorulan onay (Vazgeç) kutucuğu
                bool isConfirmed = await DisplayAlert("Uyarı", "API Anahtarınızı tamamen silmek istediğinize emin misiniz?", "Evet, Sil", "Vazgeç");

                if (isConfirmed)
                {
                    SecureStorage.Default.Remove("UserApiKey");
                    await DisplayAlert("Bilgi", "API Anahtarı başarıyla cihazdan silindi.", "Tamam");
                }
            }
            else if (action == "API Anahtarı Gir")
            {
                await HandleSetApiKey();
            }
            else if (action == "Hakkında")
            {
                await DisplayAlert("Hakkında", "ÖPT - Öğrenci Para Takip\nBursa Uludağ Üniversitesi \nBTK Hackathon 2026 Projesi \nAhmet Behlül Özer", "Tamam");
            }
        }
        catch (Exception ex)
        {
            // Eğer bir hata olursa uygulama çökmez, bu uyarıyı verir
            await DisplayAlert("Sistem Uyarısı", $"Menü işlemi sırasında bir hata oluştu: {ex.Message}", "Tamam");
        }
    }

    // 2. KULLANICIDAN API ANAHTARINI ALIP GÜVENLİ KASAYA KAYDEDEN METOT
    private async Task HandleSetApiKey()
    {
        // YENİ: Güvenli kasa işlemlerini hata yakalayıcı içine alıyoruz
        try
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

            // 1. DURUM: Kullanıcı "İptal" butonuna bastıysa (result null döner)
            if (result == null) return;

            // 2. DURUM: Kullanıcı "Kaydet" dedi ama içi boş veya sadece boşluk var
            if (string.IsNullOrWhiteSpace(result))
            {
                // Kullanıcıyı uyarıyoruz
                await DisplayAlert("Uyarı", "API anahtarı boş bırakılamaz. Lütfen geçerli bir anahtar girin.", "Tamam");

                // Kullanıcıya kolaylık olsun diye giriş ekranını tekrar açıyoruz (Rekürsif çağrı)
                await HandleSetApiKey();
                return;
            }

            {
                // Girdiği değeri telefonun güvenli kasasına (SecureStorage) kaydediyoruz
                await SecureStorage.Default.SetAsync("UserApiKey", result.Trim());
                await DisplayAlert("Başarılı", "API Anahtarınız telefona güvenle kaydedildi!", "Tamam");
            }
        }
        catch (Exception ex)
        {
            // Donanımsal kasa şifreleme hatası olursa çökmeyi engeller
            await DisplayAlert("Güvenlik Uyarısı", $"Cihazın güvenli kasasına erişilemedi: {ex.Message}", "Tamam");
        }
    }

    private async Task HandleClearSession()
    {
        try
        {
            // 1. ADIM: VERİTABANINI TAMAMEN TEMİZLE
            // SQLite içindeki tüm harcamaları uçuruyoruz
            if (_databaseService != null)
            {
                // Bir önceki adımda yazdığımız tümünü silme metodunu çağırıyoruz
                await _databaseService.ClearAllExpensesAsync();
            }

            // 2. ADIM: CACHE VE OTURUM VERİLERİNİ SİL
            UserSession.ClearSession();
            Preferences.Default.Remove("LastFinanceData");

            // Üniversite giriş kutusunu boşalt
            if (UniEntry != null) UniEntry.Text = string.Empty;

            // 3. ADIM: ARAYÜZÜ (UI) VE GEÇMİŞ ANALİZLERİ ANLIK SIFIRLAMA
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Ana grafiği ve trend grafiğini boşalt (Gri halkaya dönecekler)
                await UpdateChart();
                await UpdateTrendChart(new List<Expense>(), false);

                if (HistoryList != null)
                {
                    HistoryList.Children.Clear();                }

                if (MiniExpenseList != null)
                {
                    MiniExpenseList.Children.Clear();
                }
                if (AdviceLabel != null)
                {
                    AdviceLabel.Text = "Henüz bir finansal analiz yapılmadı.";
                }

                if (NewsLabel != null)
                {
                    NewsLabel.Text = "Geçmiş finansal haberler sıfırlandı.";
                }
                if (Preferences.Default.ContainsKey("LastFinanceData"))
                {
                    Preferences.Default.Remove("LastFinanceData");
                }
                await _databaseService.ClearAllExpensesAsync(); // Harcamaları sil
                await _databaseService.ClearAllAnalysisAsync(); // Analiz geçmişini de sil (metodunun adı her neyse)

            });

            await DisplayAlert("Başarılı", "Tüm harcamalar, geçmiş analizler ve kategoriler sıfırlandı! 🧹", "Tamam");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", $"Sıfırlama esnasında bir hata oluştu: {ex.Message}", "Tamam");
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
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Önce grafiği tamamen boşa çıkarıp eski text önbelleğini temizliyoruz
                    ExpenseChartView.Chart = null;
                    if (ChartLegendLayout != null)
                    {
                        ChartLegendLayout.Children.Clear();
                    }
                    var emptyEntries = new List<ChartEntry> {
                        // Label ve ValueLabel alanlarını açıkça boş string yapıyoruz ki eski yazılar ezilsin
                    new ChartEntry(1) { Color = SKColor.Parse("#E0E0E0"), Label = "", ValueLabel = "" }
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

                await UpdateTrendChart(allExpenses, filterData != null);
                return;
            }

            var categoryTotals = allExpenses
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "DİĞER" : e.Category.Trim().ToUpper())
                .Select(g => new
                {
                    Category = g.Key,
                    Total = g.Sum(e => e.Amount)
                }).ToList();

            var entries = new List<Microcharts.ChartEntry>();
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

            MainThread.BeginInvokeOnMainThread(() =>
            {
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


            // DİKKAT: İsmini 'donutEntries' yapmıştık, o şekilde devam ediyoruz
            var donutEntries = new List<Microcharts.ChartEntry>();

            // Eski göstergeleri temizliyoruz
            ChartLegendLayout.Children.Clear();

            // 1. ÇÖZÜM: KATEGORİLERİ TEK TİP YAPARAK GRUPLAMA (Büyük/küçük harf ve boşlukları düzeltme)
            var categoryGroups = allExpenses.GroupBy(e =>
                string.IsNullOrWhiteSpace(e.Category) ? "Diğer" :
                char.ToUpper(e.Category.Trim()[0]) + e.Category.Trim().Substring(1).ToLower()
            );

            foreach (var group in categoryGroups)
            {
                string categoryName = group.Key;
                Color categoryColor = GetColorForCategory(categoryName);

                // --- 2. ÇÖZÜM: GRAFİK DİLİMİ TEMİZLİĞİ ---
                var entry = new Microcharts.ChartEntry((float)group.Sum(e => e.Amount))
                {
                    // Label ve ValueLabel içini BOŞ bırakıyoruz ki grafiğin etrafında o karmaşık yazılar çıkmasın!
                    Label = "",
                    ValueLabel = "",
                    Color = categoryColor.ToSKColor()
                };

                donutEntries.Add(entry);

                // --- GÖSTERGE (LEGEND) KISMI ---
                var legendItem = new HorizontalStackLayout
                {
                    Spacing = 6,
                    Margin = new Thickness(5, 2),
                    Children =
                    {
                        new Border
                        {
                            WidthRequest = 12,
                            HeightRequest = 12,
                            BackgroundColor = categoryColor,
                            StrokeShape = new RoundRectangle { CornerRadius = 6 },
                            VerticalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = categoryName,
                            FontSize = 11,
                            TextColor = Colors.DarkGray,
                            VerticalOptions = LayoutOptions.Center
                        }
                    }
                };

                ChartLegendLayout.Children.Add(legendItem);
            }

            // DÖNGÜ BİTTİKTEN SONRA: Yeni listemizi grafiğe bağlıyoruz
            ExpenseChartView.Chart = new Microcharts.DonutChart
            {
                Entries = donutEntries,
                BackgroundColor = SkiaSharp.SKColors.Transparent,
                HoleRadius = 0.6f
            };


            await UpdateTrendChart(allExpenses, filterData != null);

            MiniExpenseList.Children.Clear();
            // Tüm harcamaları alıyoruz veya performansı korumak için Take(50) gibi geniş bir limit de verebilirsin.
            var recentExpenses = allExpenses.OrderByDescending(x => x.Date).Take(50);

            foreach (var exp in recentExpenses)
            {
                // 1. IZGARA OLUŞTURMA: Alanı sadece sağ ve sol olarak ikiye ayırıyoruz
                var grid = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star }, // Sol Taraf: Yazılar için alabildiği kadar geniş alan (*)
                        new ColumnDefinition { Width = GridLength.Auto }  // Sağ Taraf: Buton için sadece gerektiği kadar dar alan (Auto)
                    },
                    ColumnSpacing = 15, // Yazı ile buton arasındaki mesafeyi artırdık
                    Padding = new Thickness(12), // KUTUNUN İÇ BOŞLUĞU: Her yönden 12 birim boşluk vererek ferahlattık
                    VerticalOptions = LayoutOptions.Center
                };

                // 2. SOL TARAF: Kategori, Açıklama ve Tutarın Alt Alta Dizilmesi
                var textLayout = new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Spacing = 4, // Yazılar arasına minik bir nefes payı
                    Children = {
                        // 1. Kategori (En üstte, kalın ve belirgin)
                        new Label {
                            Text = exp.Category,
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.Black
                        },
                        
                        // 2. YENİ EKLENEN: Açıklama (Kategorinin hemen altında, küçük ve zarif)
                        new Label {
                            Text = exp.Description,
                            FontSize = 11,
                            TextColor = Colors.Gray,
                            MaxLines = 1,
                            LineBreakMode = LineBreakMode.TailTruncation
                        },

                        // 3. Tutar (En altta, yeşil renkte)
                        new Label {
                            Text = exp.Amount.ToString("N0") + " ₺",
                            FontSize = 12,
                            TextColor = Colors.DarkGreen
                        }
                    }
                };

                // 3. SAĞ TARAF: Sil Butonumuz
                var deleteBtn = new Button
                {
                    Text = "Sil",
                    BackgroundColor = Colors.Red,
                    TextColor = Colors.White,
                    FontSize = 10,
                    Padding = new Thickness(10, 0), // Butonun içine sağdan soldan minik bir pay verdik
                    HeightRequest = 35,
                    CornerRadius = 8,
                    VerticalOptions = LayoutOptions.Center,
                    CommandParameter = exp
                };
                deleteBtn.Clicked += OnDeleteExpenseClicked;

                // 4. İÇERİKLERİ YERLEŞTİRME
                grid.Add(textLayout, 0, 0); // Yazı bloğu 0. sütuna (Sola)
                grid.Add(deleteBtn, 1, 0);  // Buton 1. sütuna (Sağa)

                // 5. DIŞ ÇERÇEVE
                var frame = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = 8 },
                    Margin = new Thickness(0, 0, 0, 10), // KUTULAR ARASI BOŞLUK: Alt alta binen kutuların arasına 10 piksel mesafe koyduk
                    Content = grid
                };

                MiniExpenseList.Children.Add(frame);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Grafik Hatası: {ex.Message}");
        }
    }

    // Görünen "Sil" butonuna tıklandığında çalışır
    private async void OnDeleteExpenseClicked(object sender, EventArgs e)
    {
        // 1. Tıklanan nesneyi bir "Button" olarak algıla
        var button = sender as Button;

        // 2. Butona bağladığımız CommandParameter içindeki Harcama (Expense) verisini kontrol et
        if (button?.CommandParameter is Expense expenseToDelete)
        {
            // 3. Kullanıcıya yanlışlıkla silmemesi için onay sor
            bool isConfirmed = await DisplayAlert(
                "Harcama Silinecek",
                $"{expenseToDelete.Category} kategorisindeki {expenseToDelete.Amount:N0} ₺ tutarındaki harcamayı silmek istediğinize emin misiniz?",
                "Evet, Sil",
                "Vazgeç");

            // 4. Onay verildiyse veritabanından sil
            if (isConfirmed)
            {
                await _databaseService.DeleteExpenseAsync(expenseToDelete);

                // Ekranı anında güncelle
                await UpdateChart();

                await DisplayAlert("Başarılı", "Harcama başarıyla silindi.", "Tamam");
            }
        }
    }

    // Listenin durumunu (Açık/Kapalı) aklında tutacak olan bool değişkeni
    private bool _isHistoryExpanded = false;

    // Tıklama gerçekleştiğinde çalışacak olan metot
    private void OnHistoryExpandTapped(object sender, TappedEventArgs e)
    {
        // Durumu tersine çevir (Açıksa kapat, kapalıysa aç)
        _isHistoryExpanded = !_isHistoryExpanded;

        if (_isHistoryExpanded)
        {
            // LİSTEYİ GENİŞLET: Yüksekliği 450 piksele çıkarıyoruz
            HistoryScrollView.HeightRequest = 450;

            // Kullanıcıya artık listeyi daraltabileceğini bildiriyoruz
            HistoryExpandLabel.Text = "Daralt ▲";
        }
        else
        {
            // LİSTEYİ DARALT: İlk baştaki derli toplu boyutu olan 220 piksele geri döndürüyoruz
            HistoryScrollView.HeightRequest = 220;

            // Kullanıcıya tekrar genişletebileceğini bildiriyoruz
            HistoryExpandLabel.Text = "Daha Fazla Göster ▼";
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

    private async Task UpdateTrendChart(List<Expense> currentExpenses, bool isFilterActive = false)
    {
        // 1. KONTROL: Listede hiçbir veri yoksa
        if (currentExpenses == null || !currentExpenses.Any())
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MyChartView.IsVisible = false;
                EmptyDataLabel.Text = isFilterActive ? "Bu filtrelere uygun harcama bulunamadı." : "Henüz hiçbir harcama kaydınız yok.";
                EmptyDataLabel.IsVisible = true;
            });
            return;
        }

        // 2. KONTROL: Bugün harcama YAPILMADIYSA ve FİLTRE YOKSA
        bool hasTodayExpense = currentExpenses.Any(e =>
            e.Date.Year == DateTime.Today.Year &&
            e.Date.Month == DateTime.Today.Month &&
            e.Date.Day == DateTime.Today.Day);

        if (!isFilterActive && !hasTodayExpense)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MyChartView.IsVisible = false;
                EmptyDataLabel.Text = "Bugün henüz hiçbir harcama yapmadınız. Harika gidiyorsunuz! 🎉";
                EmptyDataLabel.IsVisible = true;
            });
            return;
        }

        // 3. VERİ FİLTRELEME VE HAZIRLIK (Arka Planda)
        var recentExpensesQuery = currentExpenses.AsEnumerable();

        if (!isFilterActive)
        {
            var last7Days = DateTime.Today.AddDays(-6);
            recentExpensesQuery = recentExpensesQuery.Where(e => e.Date >= last7Days);
        }

        var recentExpenses = recentExpensesQuery
            .GroupBy(e => new DateTime(e.Date.Year, e.Date.Month, e.Date.Day))
            .Select(g => new
            {
                ExactDate = g.Key,
                Total = g.Sum(e => e.Amount)
            })
            .OrderBy(x => x.ExactDate)
            .ToList();

        if (!recentExpenses.Any())
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MyChartView.IsVisible = false;
                EmptyDataLabel.Text = "Seçilen tarih aralığında grafik verisi bulunamadı.";
                EmptyDataLabel.IsVisible = true;
            });
            return;
        }

        // Tek nokta çizim bug'ı çözümü
        if (recentExpenses.Count == 1)
        {
            var singleDate = recentExpenses[0].ExactDate;
            recentExpenses.Insert(0, new { ExactDate = singleDate.AddDays(-1), Total = 0.0 });
        }

        var entries = new List<Microcharts.ChartEntry>();
        foreach (var item in recentExpenses)
        {
            entries.Add(new ChartEntry((float)item.Total)
            {
                Label = item.ExactDate.ToString("dd MMM"),
                ValueLabel = item.Total.ToString("N0") + " ₺",
                Color = SKColor.Parse("#2196F3")
            });
        }

        // 4. GRAFİK NESNESİNİ OLUŞTURMA (Hala Arka Planda)
        var newChart = new LineChart
        {
            Entries = entries,
            LabelTextSize = 22,
            BackgroundColor = SKColors.Transparent,
            Margin = 20,
            LineMode = LineMode.Spline,
            LineSize = 8,
            PointMode = PointMode.Circle,
            PointSize = 18,
            LabelOrientation = Microcharts.Orientation.Horizontal,
            ValueLabelOrientation = Microcharts.Orientation.Horizontal,
            AnimationDuration = TimeSpan.Zero,
            LineAreaAlpha = 0
        };

        // 5. 🎯 GÖRÜNTÜ BUG'I İÇİN KESİN ÇÖZÜM
        // Önce arayüze eski çizimi tamamen silmesini söylüyoruz
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MyChartView.Chart = null;
            MyChartView.IsVisible = true;
            EmptyDataLabel.IsVisible = false;
        });

        // Çizim motorunun (SkiaSharp) ekranı temizlemesi için sadece 50 milisaniyelik bir nefes tanıyoruz.
        // Bu sayede rakamlar birbiri üstüne binmek yerine tertemiz bir alana yazılır.
        await Task.Delay(50);

        // Temizliğin bittiğinden emin olduktan sonra yeni grafiği ekrana basıyoruz
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MyChartView.Chart = newChart;
        });
    }
    private async void OnScanReceiptClicked(object sender, EventArgs e)
    {
        OnAddExpenseClicked(sender, e);
    }

    private async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        OnAddExpenseClicked(sender, e);
    }

    // Kategori ismine göre renk döndüren yardımcı C# metodu
    private Color GetColorForCategory(string categoryName)
    {
        // switch yapısı, gelen kategoriName değişkenini kontrol eder
        switch (categoryName)
        {
            case "Yemek":
                return Color.FromArgb("#FFB300"); // Canlı Turuncu

            case "Ulaşım":
                return Color.FromArgb("#1E88E5"); // Mavi

            case "Eğitim":
                return Color.FromArgb("#43A047"); // Yeşil

            case "Market":
                return Color.FromArgb("#E53935"); // Kırmızı

            case "Eğlence":
                return Color.FromArgb("#8E24AA"); // Mor

            // Eğer kategori bu yukarıdakilerden hiçbiri değilse (örneğin boşsa veya farklıysa)
            default:
                return Color.FromArgb("#757575"); // Nötr Gri (Varsayılan Renk)
        }
    }

    private async void OnEkstreYukleClicked(object sender, EventArgs e)
    {
        try
        {
            // 1. DOSYA SEÇİMİ
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/pdf", "image/jpeg", "image/png" } },
            { DevicePlatform.WinUI, new[] { ".pdf", ".jpg", ".jpeg", ".png" } }
        });

            var options = new PickOptions { PickerTitle = "Ekstre veya Fiş Seç", FileTypes = customFileType };
            var result = await FilePicker.Default.PickAsync(options);

            // Seçilen dosyayı hafızaya alıyoruz (Diğer buton kullansın diye)
            _selectedStatementFile = result;

            if (result == null) return;

            // Önizleme Resmi Mantığın
            if (result.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || result.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                if (PreviewImage != null)
                {
                    PreviewImage.Source = ImageSource.FromStream(() => _selectedStatementFile.OpenReadAsync().Result);
                    PreviewImage.IsVisible = true;

                    // Çarpı butonunu da burada görünür yapıyoruz
                    if (ClosePreviewButton != null) ClosePreviewButton.IsVisible = true;
                }
            }

            // Durum etiketini güncelle ve TAMAM butonunu görünür yap!
            if (StatusLabel != null)
                StatusLabel.Text = $"Seçilen Dosya: {result.FileName} 📄";

            if (ConfirmAnalyzeButton != null)
                ConfirmAnalyzeButton.IsVisible = true; // XAML tarafındaki Tamam butonu açılır

            // 🛑 BURADA DURUYORUZ! 
            // Geri kalan tüm API ve veritabanı kodlarını bu metodun içinden temizledik.
            // Çünkü o kodlar zaten OnConfirmAnalyzeClicked metodunun içinde tıkır tıkır çalışıyor.
        }
        catch (Exception ex)
        {
            await DisplayAlert("Sistem Hatası", $"Dosya seçilirken bir hata oluştu: {ex.Message}", "Tamam");
        }
    }
    private async void OnConfirmAnalyzeClicked(object sender, EventArgs e)
    {
        if (_selectedStatementFile == null) return;

        try
        {
            // UI Kilitle ve Loading Göster
            if (ConfirmAnalyzeButton != null) ConfirmAnalyzeButton.IsEnabled = false;
            if (StatusLabel != null) StatusLabel.Text = "Yapay Zeka Okuyor... ⏳";

            // 2. SECURE STORAGE'DAN ANAHTARI AL
            string apiAnahtarim = await SecureStorage.Default.GetAsync("UserApiKey");
            if (string.IsNullOrEmpty(apiAnahtarim))
            {
                await DisplayAlert("Hata", "Lütfen ayarlardan API anahtarınızı girin.", "Tamam");
                return;
            }

            // 3. GEMINI'YE GÖNDER (Hafızadaki dosyayı açıyoruz)
            using var imageStream = await _selectedStatementFile.OpenReadAsync();
            var geminiService = new GeminiVisionService(apiAnahtarim);
            string aiYaniti = await geminiService.EkstreAnalizEt(imageStream, _selectedStatementFile.FileName);

            if (aiYaniti.StartsWith("API_ERROR") || aiYaniti.StartsWith("EXCEPTION"))
            {
                await DisplayAlert("Google AI Hatası", aiYaniti, "Tamam");
                return;
            }

            // Temizlik
            aiYaniti = aiYaniti.Replace("```json", "").Replace("```", "").Trim();

            // 4. JARRAY AYRIŞTIRMASI (Senin kurşun geçirmez eşleştirme mantığın birebir korundu)
            var jsonArray = Newtonsoft.Json.Linq.JArray.Parse(aiYaniti);

            if (jsonArray != null && jsonArray.Count > 0)
            {
                var dbService = _databaseService;
                int eklenenKayitSayisi = 0;

                foreach (var item in jsonArray)
                {
                    var tutarToken = item["Amount"] ?? item["amount"] ?? item["Tutar"] ?? item["tutar"] ?? item["Miktar"] ?? item["miktar"];
                    var tarihToken = item["Date"] ?? item["date"] ?? item["Tarih"] ?? item["tarih"];
                    var kategoriToken = item["Category"] ?? item["category"] ?? item["Kategori"] ?? item["kategori"];
                    var aciklamaToken = item["Description"] ?? item["description"] ?? item["Aciklama"] ?? item["aciklama"];

                    double asilMiktar = 0;
                    if (tutarToken != null)
                    {
                        string rawTutar = tutarToken.ToString().Trim();
                        string sadeceSayi = new string(rawTutar.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());

                        if (sadeceSayi.Contains(",") && sadeceSayi.Contains("."))
                        {
                            sadeceSayi = sadeceSayi.Replace(",", "");
                        }
                        else if (sadeceSayi.Contains(","))
                        {
                            sadeceSayi = sadeceSayi.Replace(",", ".");
                        }

                        double.TryParse(sadeceSayi, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out asilMiktar);
                    }

                    // Senin o hayalet veri filtren
                    if (asilMiktar <= 0) continue;

                    var yeniHarcama = new Expense
                    {
                        Date = (tarihToken != null && DateTime.TryParse(tarihToken.ToString(), out DateTime dt)) ? dt : DateTime.Now,
                        Amount = asilMiktar,
                        Category = kategoriToken?.ToString() ?? "Diğer",
                        Description = aciklamaToken?.ToString() ?? "Ekstreden eklendi"
                    };

                    await dbService.AddExpenseAsync(yeniHarcama);
                    eklenenKayitSayisi++;
                }

                if (StatusLabel != null) StatusLabel.Text = $"Başarılı! {eklenenKayitSayisi} harcama eklendi. ✅";
                await DisplayAlert("Başarılı", $"{eklenenKayitSayisi} adet harcama ekstrenizden başarıyla okundu!", "Harika");

                // 5. ANLIK GRAFİK GÜNCELLEME (MainThread Sihri)
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await UpdateChart();
                    var allExpenses = await _databaseService.GetExpensesAsync();
                    await UpdateTrendChart(allExpenses, false);
                });

                // İşlem bitti, onay butonunu kapat ve hafızayı temizle
                if (ConfirmAnalyzeButton != null) ConfirmAnalyzeButton.IsVisible = false;
                _selectedStatementFile = null;
                if (PreviewImage != null)
                {
                    PreviewImage.Source = ImageSource.FromStream(() => _selectedStatementFile.OpenReadAsync().Result);
                    PreviewImage.IsVisible = true;

                    // 👇 BU SATIRI EKLE (Çarpı butonu da resimle birlikte ekranda belirir)
                    if (ClosePreviewButton != null) ClosePreviewButton.IsVisible = true;
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Sistem Hatası", $"İşlem sırasında bir hata oluştu: {ex.Message}", "Tamam");
        }
        finally
        {
            if (ConfirmAnalyzeButton != null) ConfirmAnalyzeButton.IsEnabled = true;
        }
    }
    private void OnClosePreviewClicked(object sender, EventArgs e)
    {
        // 1. Görselleri ve butonları gizle
        if (PreviewImage != null) PreviewImage.IsVisible = false;
        if (ClosePreviewButton != null) ClosePreviewButton.IsVisible = false;
        if (ConfirmAnalyzeButton != null) ConfirmAnalyzeButton.IsVisible = false;

        // 2. HAFIZAYI SIFIRLA (Bak burada 'result = null' değil, alttakini yazıyoruz)
        _selectedStatementFile = null;

        // 3. Yazıyı eski orijinal haline döndür (Burada result.FileName KULLANMA)
        if (StatusLabel != null) StatusLabel.Text = "Henüz dosya seçilmedi.";
    }
}
