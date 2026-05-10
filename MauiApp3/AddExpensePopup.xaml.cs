using CommunityToolkit.Maui.Views;
using MauiApp3.Models;
using MauiApp3.Services;
using Newtonsoft.Json;

namespace MauiApp3.Views;

public partial class AddExpensePopup : Popup
{
    private readonly DatabaseService _dbService;
    private readonly GeminiService _geminiService;

    // MAUI Toolkit'in çakışmalarını aşmak için kendi bayrağımızı üretiyoruz:
    public bool IsSuccess { get; private set; } = false;

    public AddExpensePopup(DatabaseService dbService, GeminiService geminiService)
    {
        InitializeComponent();
        _dbService = dbService;
        _geminiService = geminiService;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            // Kontroller...
            if (string.IsNullOrWhiteSpace(DescEntry.Text) || string.IsNullOrWhiteSpace(AmountEntry.Text)) return;
            if (!double.TryParse(AmountEntry.Text, out double girilenTutar)) return;

            // Veriyi oluştur ve kaydet
            var newExpense = new Expense
            {
                Amount = girilenTutar,
                Category = CategoryPicker.SelectedItem?.ToString() ?? DescEntry.Text,
                Date = DateTime.Now
            };

            await _dbService.AddExpenseAsync(newExpense);

            // İŞLEM BAŞARILI BAYRAĞINI KALDIRIYORUZ
            IsSuccess = true;

            // Senin sisteminde çalışan kapatma metodu
            await CloseAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Kayıt Hatası: {ex.Message}");
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        IsSuccess = false;
        await CloseAsync();
    }

    // --- 1. BAŞLATICI: KAMERA BUTONU ---
    private async void OnScanReceiptClicked(object sender, EventArgs e)
    {
        try
        {
            // Kamera için izin kontrolü
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Camera>();
            }

            if (status != PermissionStatus.Granted)
            {
                await App.Current.MainPage.DisplayAlert("İzin Gerekli", "Kamera izni olmadan fiş taranamaz.", "Tamam");
                return;
            }

            // Fotoğraf çek
            var photo = await MediaPicker.Default.CapturePhotoAsync();

            // Fotoğraf geldiyse ortak analiz motoruna gönder
            await AnalyzePhotoAsync(photo);
        }
        catch (Exception ex)
        {
            await App.Current.MainPage.DisplayAlert("Hata", "Kamera açılırken hata oluştu: " + ex.Message, "Tamam");
        }
    }

    // --- 2. BAŞLATICI: GALERİ BUTONU ---
    private async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        try
        {
            // Galeriden seç (Galeri için genellikle ekstra izin kodu gerekmez, sistem picker açar)
            var photo = await MediaPicker.Default.PickPhotoAsync();

            // Fotoğraf seçildiyse ortak analiz motoruna gönder
            await AnalyzePhotoAsync(photo);
        }
        catch (Exception ex)
        {
            await App.Current.MainPage.DisplayAlert("Hata", "Galeri açılırken hata oluştu: " + ex.Message, "Tamam");
        }
    }

    // --- ORTAK MOTOR: GEMINI ANALİZ METODU (Private Task) ---
    private async Task AnalyzePhotoAsync(FileResult photo)
    {
        // Eğer kullanıcı fotoğraf çekmeden/seçmeden iptal ettiyse dur
        if (photo == null) return;

        try
        {
            // UI Hazırlığı
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            ScanButton.IsEnabled = false; // İşlem bitene kadar butonları kilitle

            // 1. Dosyayı Base64 formatına çevir
            using var stream = await photo.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            string base64Image = Convert.ToBase64String(memoryStream.ToArray());

            // 2. Gemini'ye talimat gönder (Prompt Engineering)
            string prompt = "Bu bir alışveriş fişi. Eğer fiş üzerinde BİM, A101, ŞOK, Migros, Carrefour gibi mağaza isimleri görüyorsan kategoriyi direkt 'Market' olarak belirle. Diğer durumlarda içeriğe göre Yemek, Ulaşım veya Eğlence olarak sınıflandır. " +
                            "Cevabı sadece şu JSON formatında ver, başka metin ekleme: " +
                            "{ \"Amount\": 0.0, \"Category\": \"...\", \"Date\": \"yyyy-MM-dd\" }";

            // 3. Gemini Servisini çağır
            var resultJson = await _geminiService.AnalyzeImageAsync(prompt, base64Image);

            // 4. Gelen JSON'u temizle ve kutulara doldur
            var cleanJson = resultJson.Replace("```json", "").Replace("```", "").Trim();
            var expenseData = JsonConvert.DeserializeObject<Expense>(cleanJson);

            if (expenseData != null)
            {
                AmountEntry.Text = expenseData.Amount.ToString();
                DescEntry.Text = expenseData.Category;

                // Picker'da bu kategori varsa onu da seç
                if (CategoryPicker.Items.Contains(expenseData.Category))
                    CategoryPicker.SelectedItem = expenseData.Category;

                await App.Current.MainPage.DisplayAlert("Başarılı", "Fiş bilgileri ayıklandı!", "Tamam");
            }
        }
        catch (Exception ex)
        {
            await App.Current.MainPage.DisplayAlert("Hata", "Analiz başarısız: " + ex.Message, "Tamam");
        }
        finally
        {
            // İşlem bittiğinde her şeyi eski haline getir
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            ScanButton.IsEnabled = true;
        }
    }
}