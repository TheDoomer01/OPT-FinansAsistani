using CommunityToolkit.Maui.Views;
using MauiApp3.Models;
using MauiApp3.Services;

namespace MauiApp3.Views;

// 'Popup'ın CommunityToolkit.Maui.Views'dan geldiğini garanti ediyoruz.
public partial class AddExpensePopup : Popup
{
    private readonly DatabaseService _dbService;

    // Store the result so callers can read it after the popup is closed.
    public AddExpenseResult? Result { get; private set; }

    public AddExpensePopup(DatabaseService dbService)
    {
        InitializeComponent();
        _dbService = dbService;
    }

    // Use CloseAsync (available on the Popup base type in the provided signatures).
    // Make the handlers async void because they're event handlers.
    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // Preserve the same payload as original code.
        Result = new AddExpenseResult { IsSuccess = true };

        await CloseAsync();
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        Result = new AddExpenseResult { IsSuccess = false };

        await CloseAsync();
    }
}