#!/bin/bash
# One-click build script for K-OCR C# projects on Linux

set -e  # Exit on error

echo "Building K-OCR Solution (C# projects only)..."
echo "=============================================="

# Build each C# project individually
echo ""
echo "Building K-OCRLib..."
dotnet build K-OCRLib/K-OCRLib.csproj

echo ""
echo "Building K-OCR (WPF - will compile but won't run on Linux)..."
dotnet build K-OCR/K-OCR.csproj

echo ""
echo "Building K-OCRIntegrationTests..."
dotnet build K-OCRIntegrationTests/K-OCRIntegrationTests.csproj

echo ""
echo "=============================================="
echo "✓ All C# projects built successfully!"
echo ""
echo "Note: C++ projects (K-OCRLibRunner) are skipped on Linux."
echo "Note: K-OCR WPF app compiled but requires Windows to run."
