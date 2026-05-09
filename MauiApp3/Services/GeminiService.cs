using Newtonsoft.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;

namespace MauiApp3.Services;

public class GeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    // Gemini 2.5 Flash için en güncel URL
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent";

    public GeminiService(IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        // secrets.json içindeki 'Gemini:ApiKey' hiyerarşisinden anahtarı güvenle çekiyoruz
        _apiKey = configuration["Gemini:ApiKey"];
    }

    public async Task<string> GetResponseAsync(string soru)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return "Hata: API Key bulunamadı! secrets.json dosyasını kontrol et.";
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

            // URL sonuna API anahtarını ekleyerek isteği gönderiyoruz
            var response = await _httpClient.PostAsync($"{BaseUrl}?key={_apiKey}", content);
            var resultText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // Gelen cevabı dynamic olarak çözüp içindeki metni ayıklıyoruz
                dynamic data = JsonConvert.DeserializeObject(resultText);

                // Gemini 3 Flash yanıt yapısı: candidates -> content -> parts -> text
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
            return $"Bağlantı Hatası: {ex.Message}";
        }
    }
}