using Avalonia.Input;
using K_OCR.Models;
using System;
using System.Collections.Generic;

namespace K_OCRDesktop.Services
{
    public class KeyboardNavigationService
    {
        public event Action<int>? NavigateToFieldRequested;
        public event Action? ExportRequested;
        public event Action? SaveRequested;

        private int _currentFieldIndex = -1;

        public int CurrentFieldIndex => _currentFieldIndex;

        public KeyboardNavigationService()
        {
        }

        public void HandleKeyDown(KeyEventArgs e, int totalFields, int currentIndex)
        {
            _currentFieldIndex = currentIndex;

            if (e.Key == Key.Down || e.Key == Key.Tab)
            {
                if (_currentFieldIndex < totalFields - 1)
                {
                    NavigateToFieldRequested?.Invoke(_currentFieldIndex + 1);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up || (e.Key == Key.Tab && (e.KeyModifiers & KeyModifiers.Shift) != 0))
            {
                if (_currentFieldIndex > 0)
                {
                    NavigateToFieldRequested?.Invoke(_currentFieldIndex - 1);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Home && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                NavigateToFieldRequested?.Invoke(0);
                e.Handled = true;
            }
            else if (e.Key == Key.End && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                NavigateToFieldRequested?.Invoke(totalFields - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.S && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                SaveRequested?.Invoke();
                e.Handled = true;
            }
            else if (e.Key == Key.E && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                ExportRequested?.Invoke();
                e.Handled = true;
            }
        }
    }
}
