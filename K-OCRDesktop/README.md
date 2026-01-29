# K-OCRDesktop

Cross-platform desktop application for OCR (Optical Character Recognition) built with Avalonia UI and .NET 10.

## Overview

K-OCRDesktop is the cross-platform replacement for the original K-OCR WPF application. It uses:
- **Avalonia UI** - Cross-platform XAML-based UI framework (runs on Linux, Windows, macOS)
- **MVVM Pattern** - Model-View-ViewModel architecture with CommunityToolkit.Mvvm
- **K-OCRLib** - Shared library containing OCR services, Azure Document Intelligence integration, and data models
- **.NET 10** - Latest .NET runtime for performance and modern features

## Features

The application will provide:
- Tesseract OCR integration for text recognition
- Azure Document Intelligence for invoice parsing
- Image analysis and table detection
- Support for multiple languages
- Export to Word documents

## Building and Running

### Prerequisites
- .NET 10 SDK
- Linux with required graphics libraries (libgtk-3-0, etc.)

### Build
```bash
dotnet build K-OCRDesktop/K-OCRDesktop.csproj
```

### Run
```bash
dotnet run --project K-OCRDesktop/K-OCRDesktop.csproj
```

Or use VS Code's debug configuration: "Launch K-OCRDesktop (Avalonia)"

## Project Structure

- `Views/` - Avalonia XAML views
- `ViewModels/` - View models for MVVM pattern
- `Models/` - Application-specific models
- `Assets/` - Images, icons, and other resources
- `App.axaml` - Application entry point and styling

## Dependencies

- Avalonia 11.3.11 - UI framework
- CommunityToolkit.Mvvm 8.2.1 - MVVM helpers
- K-OCRLib - Core OCR functionality (Tesseract, Azure AI, OpenCV)
