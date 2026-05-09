using Newtonsoft.Json;
using System.Text;

namespace MauiApp3.Services;

public class GeminiService
{
    // 1. Buraya sadece Google AI Studio'dan aldığın anahtarı yapıştır
    private const string ApiKey = "AIzaSyAlXPLgqMis37UGnxfsbpTIGLyMKEtnmdQ";

    // Listende gördüğün tam ismi buraya yazıyoruz. 
    // v1beta veya v1 denemelerini yapabilirsin ama 2.0 modelleri genelde v1beta ile başlar.
    // Listende 'models/gemma-2-9b-it' veya benzeri bir isim görmüş olmalısın
    private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=";



    private readonly HttpClient _httpClient;

    public GeminiService()
    {
        _httpClient = new HttpClient();
    }

    public async Task<string> GetResponseAsync(string soru)
    {
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

            // Veriyi JSON formatına çevirip paketliyoruz
            string json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // İsteği gönderiyoruz (ApiKey ve ApiUrl birleşiyor)
            var response = await _httpClient.PostAsync(ApiUrl + ApiKey, content);
            var resultText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // Gelen cevabın içindeki o uzun metni ayıklıyoruz
                dynamic data = JsonConvert.DeserializeObject(resultText);
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