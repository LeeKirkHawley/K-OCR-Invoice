using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace K_OCRDesktop.Models;

public class FileListItem : INotifyPropertyChanged
{
    private string _fileName = string.Empty;
    private bool _isProcessed;
    private bool _isValidated;
    private bool _hasSuspectFields;

    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName != value)
            {
                _fileName = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsProcessed
    {
        get => _isProcessed;
        set
        {
            if (_isProcessed != value)
            {
                _isProcessed = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsValidated
    {
        get => _isValidated;
        set
        {
            if (_isValidated != value)
            {
                _isValidated = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasSuspectFields
    {
        get => _hasSuspectFields;
        set
        {
            if (_hasSuspectFields != value)
            {
                _hasSuspectFields = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
