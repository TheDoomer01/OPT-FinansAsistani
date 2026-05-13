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
            // 1. ZORUNLU ALAN KONTROLÜ: Tutar geçerli bir sayı mı?
            if (string.IsNullOrWhiteSpace(AmountEntry.Text) || !double.TryParse(AmountEntry.Text, out double girilenTutar))
            {
                // Eğer tutar girilmediyse işlemi durdur.
                return;
            }

            // 2. ZORUNLU ALAN KONTROLÜ: Kategori seçilmiş mi?
            if (CategoryPicker.SelectedItem == null)
            {
                // Eğer listeden bir kategori seçilmediyse işlemi durdur.
                return;
            }

            // 3. İSTEĞE BAĞLI ALAN (ORTA YOL): Açıklama boşsa varsayılan metin ata
            string girilenAciklama = string.IsNullOrWhiteSpace(DescEntry.Text)
                ? "Belirtilmedi" // Kullanıcı boş bırakırsa bu metin geçerli olur (istersen "" olarak da değiştirebilirsin)
                : DescEntry.Text.Trim(); // Doluysa başındaki/sonundaki gereksiz boşlukları temizle

            // 4. Veriyi oluştur ve kaydet
            var newExpense = new Expense
            {
                Amount = girilenTutar,
                // Kategori kesin seçildiği için doğrudan .ToString() ile alabiliriz
                Category = CategoryPicker.SelectedItem.ToString(),
                Date = DateTime.Now,

                // NOT: Eğer Expense modelinde (sınıfında) açıklama için bir özellik 
                // (örneğin Description) tanımladıysan, o değeri de burada atayabilirsin:
                Description = girilenAciklama
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
            string prompt = "Bu bir alışveriş fişi. Eğer market fişine benzeyen bir kağıt görürsen kategoriyi 'Market' olarak belirle. Diğer durumlarda içeriğe göre Yemek, Ulaşım veya Eğlence olarak sınıflandır. " +
                            "Cevabı sadece şu JSON formatında ver, başka metin ekleme: " +
                            "{ \"Amount\": 0.0, \"Category\": \"...\", \"Date\": \"yyyy-MM-dd\" }";

            // 3. Gemini Servisini çağır
            var resultJson = await _geminiService.AnalyzeImageAsync(prompt, base64Image);

            // 4. Gelen Yanıtın İçinden Sadece JSON Kısmını Çıkar
            // DİKKAT: Değişkeni burada, en dışta tanımlıyoruz ki alt satırlar onu görebilsin
            string cleanJson = string.Empty;

            int startIndex = resultJson.IndexOf('{');
            int endIndex = resultJson.LastIndexOf('}');

            // Eğer metnin içinde { ve } işaretleri düzgün bir şekilde varsa:
            if (startIndex >= 0 && endIndex > startIndex)
            {
                // Sadece ilk { ve son } işaretleri ile bunların arasındakileri al
                cleanJson = resultJson.Substring(startIndex, (endIndex - startIndex) + 1);
            }
            else
            {
                // Yapay zeka tamamen yanlış bir format döndüyse hata fırlat
                throw new Exception("Yapay zeka beklenen formata uygun cevap vermedi. \nCevap: " + resultJson);
            }

            // cleanJson artık yukarıda tanımlandığı için hata vermeyecek
            var expenseData = JsonConvert.DeserializeObject<Expense>(cleanJson);

            // Gelen veri boş değilse arayüzü güncelle
            if (expenseData != null)
            {
                // UI (Arayüz) güncellemelerini Ana İş Parçacığına (Main Thread) alıyoruz
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    AmountEntry.Text = expenseData.Amount.ToString();
                    DescEntry.Text = expenseData.Category;

                    // Picker'da bu kategori varsa onu da seç
                    if (CategoryPicker.Items.Contains(expenseData.Category))
                        CategoryPicker.SelectedItem = expenseData.Category;

                    await App.Current.MainPage.DisplayAlert("Başarılı", "Fiş bilgileri başarıyla ayıklandı ve forma eklendi!", "Tamam");
                });
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

    // Parametrelerin (object sender, EventArgs e) olması ŞARTTIR.
    private async void OnCategorySelected(object sender, EventArgs e)
    {
        var picker = (Picker)sender;
        int selectedIndex = picker.SelectedIndex;

        // Eğer yanlışlıkla tetiklendiyse veya seçim boşsa çık
        if (selectedIndex != -1) return;
        {
            string selectedCategory = picker.Items[selectedIndex];

            // 1. Miktar girişini kontrol et (Entry ismin neyse onu kullan, örn: AmountEntry)
            if (string.IsNullOrWhiteSpace(AmountEntry.Text) || !double.TryParse(AmountEntry.Text, out double amount))
            {
                await Application.Current.MainPage.DisplayAlert("Uyarı", "Lütfen önce miktarı girin.", "Tamam");
                picker.SelectedIndex = -1; // Seçimi temizle
                return;
            }

            // 2. Harcama nesnesini oluştur
            var newExpense = new Expense
            {
                Amount = amount,
                Category = selectedCategory,
                Description = "Hızlı Kayıt",
                Date = DateTime.Now
            };

            try
            {

                // 3. Veritabanına kaydet (Hata vermedi dediğin metot)
                await _dbService.AddExpenseAsync(newExpense);

            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Kaydedilirken bir sorun oluştu: " + ex.Message, "Tamam");
            }
            
        }
    }
}