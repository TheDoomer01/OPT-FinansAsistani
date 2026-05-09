using System;
using System.Collections.Generic;
using System.Text;

namespace MauiApp3.Models;
public class ChatMessage
{
    public string Text { get; set; }
    public bool IsUser { get; set; } // Kullanıcı ise true, Gemini ise false
}