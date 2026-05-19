# ÖPT (Öğrenci Para Takibi)
ÖPT, öğrencilerin harcamalarını takip etmelerine yardımcı olan bir uygulamadır. Bu uygulama, öğrencilerin bütçelerini yönetmelerine ve harcamalarını kontrol altında tutmalarına olanak tanır.
Bu proje öğrencilerin finansal durumlarını daha iyi anlamalarına ve harcamalarını daha bilinçli bir şekilde yapmalarına yardımcı olmak amacıyla geliştirilmiştir. ÖPT, kullanıcı dostu arayüzü ve çeşitli özellikleri ile öğrencilerin finansal yönetim becerilerini geliştirmelerine katkıda bulunmayı hedeflemektedir.

## Kullanılan Teknolojiler
.NET MAUI, C#, SQLite, Google Gemini API, MVVM (Model-View-ViewModel) tasarım deseni.

## Öne Çıkan Özellikler
* OCR ile fiş okuma
* Ekspre ekleme ve analiz etme özelliği
* Harcamaların kategorilere ayrılması ve haftalık veya aylık gibi grafikte gösterilmesi
* Gemini API ile harcamaların analiz edilmesi
* Bütçeye göre tasarruf önerileri sunulması 
* Gemini API'si ekleme özelliği 
* Windows ve Android platformlarında çalışabilme


## 📱 Ekran Görüntüleri

| Ana Ekran | Ana Ekran 2 | Ana Ekran 3 | Ayarlar | Harcama Ekle |
| :---: | :---: | :---: | :---: | :---: |
| ![Ana Sayfa](images/mainpage.jpg) | ![Ana Sayfa 2](images/mainpage2(new).jpg) | ![Ana Ekran 3](images/mainpage3(new).jpg) | ![Ayarlar](images/settings.jpg) | ![Harcama Ekle](images/addexpense.jpg) |

# 🚀 PROJE GÜNCELLEMESİ (EKSPRE EKLEME ÖZELLİĞİ) 

* 📹 **Yeni Özelliğin 24 Saniyelik Videosu:** [Buraya Tıklayarak İzleyin](https://youtu.be/R3S_i0Gbgx8?si=x1J8MkBTWU2S36i5)

---

## 📥 Uygulama Nasıl Kurulur ve Çalıştırılır? (Kaynak Kodlarla Uğraşmadan)

Projenin kaynak kodlarıyla uğraşmadan direkt uygulamayı denemek istiyorsanız, sağ taraftaki **Releases** bölümünden **v1.2.0** sürümünü bulun veya aşağıdaki adımları takip edin:

### 🪟 Windows İçin (Bilgisayar)
1. Sürüm sayfasından `OPT-v1.2.0-windows.zip` dosyasını indirin.
2. İndirdiğiniz zip dosyasını bir klasöre ayıklayın (Zipten çıkarmadan çalıştırırsanız hata verebilir).
3. Klasörün içerisindeki MauiApp3.exe dosyasına çift tıklayarak uygulamayı başlatın.

### 🤖 Android İçin (Telefon)
1. Telefon tarayıcınızdan sürüm sayfasına girerek `OPT-v1.2.0.apk` dosyasını indirin.
2. İndirilen `.apk` dosyasına tıklayarak kurulumu başlatın.
3. *Not: Telefonunuz ilk defa dışarıdan uygulama yüklediğiniz için "Bilinmeyen kaynaklardan uygulama yükleme" izni isteyebilir, bu izni onaylayarak kurulumu tamamlayabilirsiniz.*

---

## Kurulum (Setup)

Projeyi yerel makinenizde çalıştırmak için aşağıdaki adımları izleyebilirsiniz:
1. **Depoyu Klonlayın:**
   ```bash
   git clone [https://github.com/TheDoomer01/OPT-FinansAsistani.git](https://github.com/TheDoomer01/OPT-FinansAsistani.git)
2. Gerekli SDK'ları Kontrol Edin:
    - .NET 8.0 veya üzeri SDK'nın yüklü olduğundan emin olun.
    - Visual Studio 2026 (veya 2022) içerisinde ".NET Multi-platform App UI development" iş yükünün yüklü olduğunu doğrulayın.

3. Bağımlılıkları Yükleyin:
    Proje dizinine gidin ve aşağıdaki komutu çalıştırarak gerekli NuGet paketlerini yükleyin:
    ```bash
    dotnet restore

4. ### 🔑 API Anahtarı ve Gizli Bilgilerin Ayarlanması (secrets.json)

Projemiz güvenlik nedeniyle gerçek API anahtarlarını GitHub üzerinde barındırmamaktadır. Projeyi bilgisayarınızda çalıştırabilmek için aşağıdaki adımları izleyerek yerel konfigürasyon dosyanızı oluşturmalısınız:

- **Dosyayı Kopyalayın:**
   Proje ana dizininde bulunan `secrets.example.json` dosyasının bir kopyasını oluşturun ve adını `secrets.json` olarak değiştirin.

   *(Terminal kullanıyorsanız şu komutu girebilirsiniz:)*
   ```bash
   cp secrets.example.json secrets.json

          
- İçeriği Doldurun:
   Yeni oluşturduğunuz secrets.json dosyasını bir metin editörüyle açın ve kendi Google Gemini API anahtarınızı ilgili alana yapıştırın:
   {
  "GeminiApiKey": "BURAYA_KENDI_API_ANAHTARINIZI_YAZIN"
   }

 - "Alternatif API Girişi: Eğer secrets.json dosyasını doldurmadıysanız, uygulama açıldıktan sonra Ayarlar menüsünden de Gemini API anahtarınızı girerek özellikleri aktif hale getirebilirsiniz."
   
- **Visual Studio Ayarı (Çok Önemli):**
   - Visual Studio'da "Solution Explorer" (Çözüm Gezgini) penceresini açın.
   - `secrets.json` dosyasına sağ tıklayıp **"Properties" (Özellikler)** seçeneğine girin.
   - **"Build Action" (Derleme Eylemi)** kısmını **"Embedded resource" (Gömülü Kaynak)** olarak ayarlayın. (Aksi takdirde uygulama dosyayı bulamaz).


5. **Uygulamayı Çalıştırın:**
- Visual Studio üzerinden projenizi açın.
   -  Hedef cihaz olarak bir **Android Emulator** veya **Gerçek Android Cihaz** seçin.
   - `F5` tuşuna basarak hata ayıklama modunda başlatın.

6. **API Anahtarı Yapılandırması:**
   - Uygulama içindeki Ayarlar menüsünden Google Gemini API anahtarınızı girerek tüm özellikleri aktif hale getirebilirsiniz.



