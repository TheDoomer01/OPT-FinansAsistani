# ÖPT (Öğrenci Para Takibi)
ÖPT, öğrencilerin harcamalarını takip etmelerine yardımcı olan bir uygulamadır. Bu uygulama, öğrencilerin bütçelerini yönetmelerine ve harcamalarını kontrol altında tutmalarına olanak tanır.
Bu proje öğrencilerin finansal durumlarını daha iyi anlamalarına ve harcamalarını daha bilinçli bir şekilde yapmalarına yardımcı olmak amacıyla geliştirilmiştir. ÖPT, kullanıcı dostu arayüzü ve çeşitli özellikleri ile öğrencilerin finansal yönetim becerilerini geliştirmelerine katkıda bulunmayı hedeflemektedir.

## Kullanılan Teknolojiler
.NET MAUI, C#, SQLite, Google Gemini API, MVVM (Model-View-ViewModel) tasarım deseni.

## Öne Çıkan Özellikler
* OCR ile fiş okuma
* Harcamaların kategorilere ayrılması ve haftalık veya aylık gibi grafikte gösterilmesi
* Gemini API ile harcamaların analiz edilmesi 
* Bütçeye göre tasarruf önerileri sunulması 
* Gemini API'si ekleme özelliği 

## Ekran Görüntüleri	
| Ana Ekran | Ana Ekran 2| Ayarlar | Harcama Ekle |
<img src="images/mainpage.jpg" width=200> 
<img src="images/mainpage2.jpg" width=200> 
<img src="images/settings.jpg" width=200> 
<img src="images/addexpense.jpg" width=200>
----

## Kurulum (Setup)
Projeyi yerel makinenizde çalıştırmak için aşağıdaki adımları izleyebilirsiniz:
1. **Depoyu Klonlayın:**
   ```bash
   git clone [https://github.com/TheDoomer01/OPT-FinansAsistani.git](https://github.com/TheDoomer01/OPT-FinansAsistani.git)
2.Gerekli SDK'ları Kontrol Edin:
    - .NET 8.0 veya üzeri SDK'nın yüklü olduğundan emin olun.
    - Visual Studio 2022 içerisinde ".NET Multi-platform App UI development" iş yükünün yüklü olduğunu doğrulayın.

3.Bağımlılıkları Yükleyim:
    Proje dizinine gidin ve aşağıdaki komutu çalıştırarak gerekli NuGet paketlerini yükleyin:
    ```bash
    dotnet restore

4. **Uygulamayı Çalıştırın:**
- Visual Studio üzerinden projenizi açın.
   - Hedef cihaz olarak bir **Android Emulator** veya **Gerçek Android Cihaz** seçin.
   - `F5` tuşuna basarak hata ayıklama modunda başlatın.

5. **API Anahtarı Yapılandırması:**
   - Uygulama içindeki Ayarlar menüsünden Google Gemini API anahtarınızı girerek tüm özellikleri aktif hale getirebilirsiniz.



