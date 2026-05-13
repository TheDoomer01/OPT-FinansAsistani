using Newtonsoft.Json;
using System.Text;
using Microsoft.Extensions.Configuration; // IConfiguration arabirimi için gereklidir

namespace MauiApp3.Services;

public class GeminiService
{
    private readonly HttpClient _httpClient;

    // Eski '_apiKey' ismini sildik. Artık yedek anahtarımız bu.
    private readonly string _fallbackApiKey;

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent";

    public GeminiService(IConfiguration configuration)
    {
        _httpClient = new HttpClient();

        // DÜZELTME: secrets.json dosyasındaki isimle tam olarak aynı olmalı
        _fallbackApiKey = configuration["GeminiApiKey"];
    }

    public async Task<string> GetResponseAsync(string soru)
    {
        // 1. ADIM: Önce kullanıcının telefondan girdiği güncel anahtara bak
        string activeApiKey = await SecureStorage.Default.GetAsync("UserApiKey");

        // 2. ADIM: Telefon kasası boşsa, secrets.json'daki yedek anahtarı kullan
        if (string.IsNullOrWhiteSpace(activeApiKey))
        {
            activeApiKey = _fallbackApiKey;
        }

        // 3. ADIM: İkisi de yoksa işlemi durdur
        if (string.IsNullOrEmpty(activeApiKey))
        {
            return "Hata: API Key bulunamadı! Lütfen 'Ayarlar' menüsünden kendi API anahtarınızı girin.";
        }

        try
        {
            // Google'ın beklediği veri paketi (JSON) yapısı
            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = soru } } }
                }
            };

            // Veriyi JSON formatına çeviriyoruz
            string json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // KRİTİK DÜZELTME: Eski _apiKey yerine yeni activeApiKey değişkenimizi URL'ye veriyoruz!
            var response = await _httpClient.PostAsync($"{BaseUrl}?key={activeApiKey}", content);
            var resultText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // Gelen cevabı dynamic olarak çözüp içindeki metni ayıklıyoruz
                dynamic data = JsonConvert.DeserializeObject(resultText);

                // Gemini 2.5 Flash Lite yanıt yapısı
                string text = data.candidates[0].content.parts[0].text;
                return text;
            }
            else
            {
                return $"Sunucu Hatası: {response.StatusCode}\nDetay: {resultText}";
            }
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    public async Task<string> AnalyzeImageAsync(string prompt, string base64Image)
    {
        try
        {
            // 1. ADIM: Önce kullanıcının telefondan girdiği güncel anahtara bak
            string activeApiKey = await SecureStorage.Default.GetAsync("UserApiKey");

            // 2. ADIM: Telefon kasası boşsa, secrets.json'daki yedek anahtarı kullan
            if (string.IsNullOrWhiteSpace(activeApiKey))
            {
                activeApiKey = _fallbackApiKey;
            }

            // 3. ADIM: İkisi de yoksa güvenli bir şekilde işlemi durdur
            if (string.IsNullOrWhiteSpace(activeApiKey))
            {
                throw new Exception("API Anahtarı bulunamadı! Lütfen Ayarlar menüsünden anahtarınızı girin veya uygulamanın yapılandırma dosyasını kontrol edin.");
            }

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { inline_data = new { mime_type = "image/jpeg", data = base64Image } }
                        }
                    }
                }
            };

            var jsonPayload = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // DÜZELTME: Artık boş olan _fallbackApiKey yerine, bulduğumuz activeApiKey'i kullanıyoruz!
            var response = await _httpClient.PostAsync($"{BaseUrl}?key={activeApiKey}", content);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                dynamic result = JsonConvert.DeserializeObject(jsonResponse);
                return result.candidates[0].content.parts[0].text;
            }
            else
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                throw new Exception($"API_REDDETTI: {response.StatusCode} \nMesaj: {errorResponse}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception("Görsel analiz hatası: " + ex.Message);
        }
    }
}

