#include "pch.h"
#include "ImageToText.h"

// Opaque handle is just the C++ instance pointer
extern "C" {

// Create an ImageToText instance
__declspec(dllexport) ImageToText* ImageToText_Create()
{
    return new ImageToText();
}

// Destroy the instance
__declspec(dllexport) void ImageToText_Destroy(ImageToText* instance)
{
    if (instance) {
        delete instance;
    }
}

// Initialize with datapath (parent of "tessdata") and language
__declspec(dllexport) bool ImageToText_Initialize(ImageToText* instance, const char* datapath, const char* language)
{
    if (!instance) return false;
    return instance->Initialize(datapath, language);
}

// Perform OCR on an image path. Returns a char* allocated inside the DLL.
// Must be freed via ImageToText_FreeMessage.
__declspec(dllexport) const char* ImageToText_OCRImage(ImageToText* instance, const char* imagePath)
{
    if (!instance) return nullptr;
    return instance->OCRImage(imagePath);
}

// Free a string returned by OCRImage
__declspec(dllexport) void ImageToText_FreeMessage(ImageToText* instance, const char* str)
{
    if (!instance || !str) return;
    // cast away const to match FreeMessage signature
    instance->FreeMessage(const_cast<char*>(str));
}

__declspec(dllexport) void FreeMessage(char* str)
{
    free(str);
}

} // extern "C"