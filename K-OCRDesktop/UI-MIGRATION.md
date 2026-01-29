# K-OCR Desktop UI Migration

## Overview
Successfully duplicated the WPF UI from K-OCR into the new Avalonia-based K-OCRDesktop project.

## UI Features Implemented

### Layout Structure
- **Menu Bar** with File, Export, Edit, and Help menus
- **Two-panel layout** with vertical splitter:
  - **Left panel**: Original image display with scroll viewer
  - **Right panel**: OCR overlay with detected text and tables
- **Top caption bar**: Displays current file path

### Menu Items
- **File Menu**:
  - Open (opens image file picker)
  - Exit
- **Export Menu**:
  - Export to DOCX (placeholder)
- **Edit Menu**:
  - Copy (placeholder)
  - Paste (placeholder)
- **Help Menu**:
  - About dialog

### OCR Visualization
- **Text Overlay**: Red semi-transparent text blocks positioned over recognized text
- **Table Highlight**: Yellow semi-transparent rectangles over detected tables
- **Canvas-based rendering**: Absolute positioning matches original image coordinates

## Technical Implementation

### MVVM Architecture
- **MainWindowViewModel**: 
  - Properties for image sources, canvas dimensions, file caption
  - Commands for menu actions using CommunityToolkit.Mvvm
  - Methods to load images and prepare OCR data

- **MainWindow (Code-behind)**:
  - File picker integration using Avalonia Storage API
  - OCR overlay rendering using Canvas and absolute positioning
  - About dialog display

### Key Files
- `/K-OCRDesktop/Views/MainWindow.axaml` - Avalonia XAML UI definition
- `/K-OCRDesktop/Views/MainWindow.axaml.cs` - Code-behind for UI logic
- `/K-OCRDesktop/ViewModels/MainWindowViewModel.cs` - View model with data binding

## Differences from WPF Version

### Avalonia vs WPF
1. **Storage API**: Uses `IStorageProvider.OpenFilePickerAsync()` instead of `OpenFileDialog`
2. **Dialogs**: Custom window for About dialog (can be enhanced with MessageBox library)
3. **XAML Namespace**: `https://github.com/avaloniaui` instead of WPF namespaces
4. **GridSplitter**: Simplified configuration in Avalonia
5. **Commands**: Wire-up in code-behind for window-dependent commands

### Not Yet Implemented
- **OCR Processing**: Pipeline integration needs to be added
- **Multi-file support**: Currently loads only first selected file
- **DOCX Export**: Export functionality placeholder
- **Copy/Paste**: Clipboard operations not yet implemented
- **Navigation**: Previous/Next file navigation
- **FlowDocument**: Not needed (was Windows-specific)

## Next Steps

1. **Integrate OCR Services**:
   - Add dependency injection for OCR services
   - Wire up file processing pipeline
   - Display OCR results in overlay

2. **Enhance File Handling**:
   - Support multiple file selection and navigation
   - Add prev/next buttons
   - File list sidebar

3. **Complete Export Feature**:
   - Implement DOCX export using DocumentFormat.OpenXml
   - Add save file picker

4. **Add Configuration**:
   - Copy appsettings.json to K-OCRDesktop
   - Set up configuration services

5. **Polish UI**:
   - Add proper MessageBox dialogs (MessageBox.Avalonia package)
   - Improve error handling
   - Add progress indicators for OCR processing
   - Status bar for processing feedback

## Testing

Build command:
```bash
dotnet build K-OCRDesktop/K-OCRDesktop.csproj
```

Run command:
```bash
dotnet run --project K-OCRDesktop/K-OCRDesktop.csproj
```

Debug in VS Code: Use "Launch K-OCRDesktop (Avalonia)" configuration

## Summary
The UI structure has been successfully replicated in Avalonia. The application now has:
- ✅ Cross-platform compatibility (Linux, Windows, macOS)
- ✅ Same visual layout as WPF version
- ✅ File opening functionality
- ✅ Image display
- ✅ OCR overlay rendering structure
- 🔄 OCR processing integration (in progress)
- 🔄 Full feature parity (in progress)
