using CommunityToolkit.Maui.Views;
using MauiApp3.Models;
using MauiApp3.Services;

namespace MauiApp3.Views;

public partial class AddExpensePopup : Popup
{
    private readonly DatabaseService _dbService;

    // MAUI Toolkit'in çakışmalarını aşmak için kendi bayrağımızı üretiyoruz:
    public bool IsSuccess { get; private set; } = false;

    public AddExpensePopup(DatabaseService dbService)
    {
        InitializeComponent();
        _dbService = dbService;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(DescEntry.Text) || string.IsNullOrWhiteSpace(AmountEntry.Text))
            {
                return;
            }

            if (!double.TryParse(AmountEntry.Text, out double girilenTutar))
            {
                return;
            }

            var newExpense = new Expense
            {
                Amount = girilenTutar,
                Category = DescEntry.Text,
                Date = DateTime.Now
            };

            await _dbService.AddExpenseAsync(newExpense);

            // 1. İşlem tamam! Kendi bayrağımızı "Başarılı" (true) yapıyoruz
            IsSuccess = true;

            // 2. Parametresiz, hatasız, standart kapatma metodunu çağırıyoruz
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
}