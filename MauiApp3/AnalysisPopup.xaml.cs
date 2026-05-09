using CommunityToolkit.Maui.Views;
using MauiApp3.Models;

namespace MauiApp3.Views;

public partial class AnalysisPopup : Popup
{
    public AnalysisPopup(FinanceData data)
    {
        // Namespace düzeldiği için burası artık XAML ile el sıkışacak
        InitializeComponent();

        if (data != null)
        {
            AdviceLabel.Text = data.tavsiye ?? "Tavsiye bulunamadı.";

            if (data.haberler != null && data.haberler.Count > 0)
                NewsLabel.Text = "• " + string.Join("\n• ", data.haberler);
            else if (!string.IsNullOrEmpty(data.HaberlerMetni))
                NewsLabel.Text = "• " + data.HaberlerMetni.Replace("|", "\n• ");
        }
    }

    // Fix: call CloseAsync to close the popup instead of recursively calling the handler.
    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await CloseAsync();
    }
}