using System;
using System.Collections.Generic;
using System.Text;

namespace MauiApp3.Helpers
{
    public static class UserSession
    {
        // Sabit anahtar kelimeler (Hata yapmamak için)
        private const string UniversityKey = "user_university";
        private const string DepartmentKey = "user_department";

        // Üniversite Bilgisi
        public static string University
        {
            get => Preferences.Default.Get(UniversityKey, string.Empty); // Burayı değiştirdik
            set => Preferences.Default.Set(UniversityKey, value);
        }

        // İstersen Bölüm Bilgisini de ekleyebilirsin
        public static string Department
        {
            get => Preferences.Default.Get(DepartmentKey, "Bilgisayar Programcılığı");
            set => Preferences.Default.Set(DepartmentKey, value);
        }

        // Önceki aramaları temizlemek istersen bir metod
        public static void ClearSession()
        {
            Preferences.Default.Remove(UniversityKey);
            Preferences.Default.Remove(DepartmentKey);
        }
    }
}