using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MauiApp3.Services // Kendi namespace'ini yazmayı unutma
{
    public class GeminiVisionService
    {
        private readonly string _apiKey;

        public GeminiVisionService(string apiKey)
        {
            _apiKey = apiKey?.Trim();
        }

        public async Task<string> EkstreAnalizEt(Stream imageStream, string fileName)
        {
            try
            {
                // Senin çalışan modelin ve tam URL'n
                string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent?key={_apiKey}";

                using var memoryStream = new MemoryStream();
                await imageStream.CopyToAsync(memoryStream);
                var base64File = Convert.ToBase64String(memoryStream.ToArray());

                string mimeType = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "image/jpeg";

                // AI'nin kafasını karıştırmayacak, kesin formatlı şablon
                string prompt = "This is an invoice, receipt or bank statement. Analyze the individual expense items.\n" +
                                "CRITICAL RULES:\n" +
                                "1. Do NOT include summary lines like 'Subtotal', 'Tax', 'KDV', 'Total', or 'Grand Total'. Only extract individual items (e.g. 'Kahve', 'Kitap') or actual transaction records.\n" +
                                "2. The 'Amount' field MUST be a primitive JSON number (e.g., 150.50 or 100), NOT a string. Do NOT include any currency symbols (₺, TL, $) or words inside the Amount.\n\n" +
                                "Return ONLY a plain JSON array matching this schema exactly, without any additional text, explanations, or formatting:\n";

                var requestBody = new
                {
                    contents = new[] { new { parts = new object[] { new { text = prompt }, new { inline_data = new { mime_type = mimeType, data = base64File } } } } }
                };

                using var client = new HttpClient();
                var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    return $"API_ERROR: {response.StatusCode} - {err}";
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var jsonObject = Newtonsoft.Json.Linq.JObject.Parse(responseString);

                var aiAsilCevap = jsonObject["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                return aiAsilCevap ?? "EMPTY_RESPONSE";
            }
            catch (Exception ex)
            {
                return $"EXCEPTION: {ex.Message}";
            }
        }
    }
}