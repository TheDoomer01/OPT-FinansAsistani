using SQLite;
using MauiApp3.Models; // Model klasörünü dahil ediyoruz

namespace MauiApp3.Services
{
    public class DatabaseService
    {
        private SQLiteAsyncConnection _database;

        // Veritabanı yolunu belirliyoruz (Cihazın güvenli alanı)
        private static string DbPath => Path.Combine(FileSystem.AppDataDirectory, "UserFinance.db3");

        private async Task Init()
        {
            if (_database != null)
                return;

            // Bağlantıyı kuruyoruz
            _database = new SQLiteAsyncConnection(DbPath);

            // Expense (Harcama) tablosunu otomatik oluşturuyoruz
            await _database.CreateTableAsync<Expense>();
        }

        // --- CRUD İŞLEMLERİ ---

        // 1. Harcama Ekleme
        public async Task<int> AddExpenseAsync(Expense expense)
        {
            await Init();
            return await _database.InsertAsync(expense);
        }

        // 2. Tüm Harcamaları Getirme
        public async Task<List<Expense>> GetExpensesAsync()
        {
            await Init();
            return await _database.Table<Expense>().OrderByDescending(x => x.Date).ToListAsync();
        }

        // 3. Harcama Silme (Gerekirse)
        public async Task<int> DeleteExpenseAsync(Expense expense)
        {
            await Init();
            return await _database.DeleteAsync(expense);
        }
    }
}