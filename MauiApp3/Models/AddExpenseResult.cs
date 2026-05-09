using System;
using System.Collections.Generic;
using System.Text;

namespace MauiApp3.Models
{
    public class AddExpenseResult
    {
        public bool IsSuccess { get; set; }
        public Expense? Expense { get; set; }
    }
}
