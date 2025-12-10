#pragma once
#include "Export.h"
#include <string>

// Expose Tesseract directly
#include <tesseract/baseapi.h>
#include <leptonica/allheaders.h>

class KOCRLIB_API ImageToText
{
private:
    Pix* deskew(Pix* pixs);

public:
    ImageToText() : m_api(nullptr) {}
    ~ImageToText();

    // datapath must be the parent folder of "tessdata"
    // e.g., "C:/Program Files/Tesseract-OCR"
    bool Initialize(const char* datapath, const char* language = "eng");
    void Shutdown();

    // return messages will be allocated in InitMessage() and freed in FreeMessage() ONLY!!!
    // the goal here is to return strings without using std::string to avoid memory management issues across DLL boundaries
    char* OCRImage(const char* imagePath);
    char* InitMessage(const char* message);
    void FreeMessage(char* str);

private:
    tesseract::TessBaseAPI* m_api;
};