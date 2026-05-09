using SQLite;
using MauiApp3.Models;

namespace MauiApp3.Services
{
    public class DatabaseService
    {
        // TEK bir bağlantı (İki kabloya gerek yok)
        private SQLiteAsyncConnection _db;

        private async Task Init()
        {
            if (_db != null) return;

            // Tek bir yol, tek bir dosya ismi
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ProjectFinance.db3");
            _db = new SQLiteAsyncConnection(dbPath);

            await _db.CreateTableAsync<Expense>();
            await _db.CreateTableAsync<FinanceData>();
        }

        // --- HARCAMA (EXPENSE) İŞLEMLERİ ---
        public async Task<int> AddExpenseAsync(Expense expense)
        {
            await Init();
            return await _db.InsertAsync(expense);
        }

        public async Task<List<Expense>> GetExpensesAsync()
        {
            await Init();
            return await _db.Table<Expense>().OrderByDescending(x => x.Date).ToListAsync();
        }

        public async Task<int> DeleteExpenseAsync(Expense expense)
        {
            await Init();
            return await _db.DeleteAsync(expense);
        }

        // --- ANALİZ (FINANCEDATA) İŞLEMLERİ ---
        public async Task AddAnalysisAsync(FinanceData data)
        {
            await Init();
            if (data.haberler != null && data.haberler.Count > 0)
            {
                data.HaberlerMetni = string.Join("|", data.haberler);
            }
            await _db.InsertAsync(data);
        }

        public async Task<List<FinanceData>> GetAnalysesAsync()
        {
            await Init();
            return await _db.Table<FinanceData>().OrderByDescending(x => x.KayitTarihi).ToListAsync();
        }
    }
}