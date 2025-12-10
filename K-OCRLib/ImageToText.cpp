#include "pch.h"
#include "ImageToText.h"
#include <stdexcept>
#include <cmath>
#include "leptonica/allheaders.h"

# define M_PI           3.14159265358979323846  /* pi */

bool write_deskewed_image = false; // for debugging

ImageToText::~ImageToText()
{
    Shutdown();
}

bool ImageToText::Initialize(const char* datapath, const char* language)
{
    if (m_api) {
        Shutdown();
    }

    m_api = new tesseract::TessBaseAPI();

    // datapath should contain a "tessdata" subfolder:
    // Tesseract will look for <datapath>/tessdata/<language>.traineddata
    const int rc = m_api->Init(datapath, language);
    if (rc != 0) {
        delete m_api;
        m_api = nullptr;
        return false;
    }
    return true;
}

void ImageToText::Shutdown()
{
    if (m_api) {
        m_api->End();
        delete m_api;
        m_api = nullptr;
    }
}

char* ImageToText::OCRImage(const char* imagePath)
{
    if (!m_api) {
        return InitMessage("Error: Tesseract not initialized. Call Initialize() first.");
    }

    Pix* image = pixRead(imagePath);
    if (!image) {
        std::string msg = std::string("Error: Could not open image file: ") + imagePath;
        return InitMessage(msg.c_str());
    }

    // Deskew (deskew destroys 'image' and returns new PIX*)
    Pix* deskewed = deskew(image);

    // Persist the deskewed image
    if (write_deskewed_image)
    {
        const char* outPath = "C:/temp/deskewed.png";
        int writeRc = 0; // 0 == success in Leptonica
        if (deskewed) {
            writeRc = pixWrite(outPath, deskewed, IFF_PNG);
            if (writeRc != 0) {
                // optional: log failure
                // std::cerr << "Failed to write deskewed image: " << outPath << std::endl;
            }
        }
    }

    // Use deskewed image for OCR if available, else fallback.
    m_api->SetImage(deskewed ? deskewed : image);

    char* outText = m_api->GetUTF8Text();
    char* result = InitMessage(outText ? outText : "Error");
    delete[] outText;

    if (deskewed) pixDestroy(&deskewed);
    // 'image' was destroyed inside deskew()

    return result;
}

char* ImageToText::InitMessage(const char* message)
{
    size_t len = strlen(message);
    char* result = (char*)malloc(len + 1); // Allocate inside DLL
    if (result) {
        strcpy_s(result, len + 1, message);
    }
    return result;
}

// Companion free function
//extern "C" __declspec(dllexport)
void ImageToText::FreeMessage(char* str)
{
    free(str);  // Free inside DLL
}

Pix* ImageToText::deskew(Pix* pixs)
{
    // Convert to 8bpp grayscale if needed
    PIX* gray = pixConvertTo8(pixs, 0);
    if (!gray) {
        gray = pixClone(pixs);
    }

    // Otsu adaptive threshold
    const l_int32 sx = 32, sy = 32;
    const l_int32 smoothx = 1, smoothy = 1;
    const l_int32 mindiff = 0;

    PIX* mask = nullptr;      // threshold mask (optional)
    PIX* binarized = nullptr; // binarized image
    l_int32 otsuRc = pixOtsuAdaptiveThreshold(gray, sx, sy, smoothx, smoothy, mindiff, &mask, &binarized);

    PIX* binary = nullptr;
    if (otsuRc == 0 && binarized) {
        binary = binarized;
    } else {
        binary = pixThresholdToBinary(gray, 128);
        if (!binary) binary = pixClone(gray);
    }

    // Strengthen text lines using a 3x3 dilation brick
    // pixDilateBrick: (pixd=nullptr for new), (pixs), (hsize), (vsize)
    PIX* binStrong = pixDilateBrick(nullptr, binary, 3, 3);

    // Detect skew angle on binarized image
    float angle = 0.0f;
    float conf = 0.0f;
    PIX* skewSrc = binStrong ? binStrong : binary;
    l_int32 skewRc = pixFindSkew(skewSrc, &angle, &conf);

    // Decide rotation using detected angle or Leptonica auto-deskew
    PIX* result = nullptr;
    if (skewRc == 0 && std::abs(angle) > 0.1f && conf > 2.0f) 
    {
        // Flip the sign: rotate by +angle instead of -angle
        result = pixRotate(pixs, angle * (float)M_PI / 180.0f, L_ROTATE_AREA_MAP, L_BRING_IN_WHITE, 0, 0);
    } 
    else 
    {
        result = pixDeskew(pixs, 0);
        if (!result) 
            result = pixClone(pixs);
    }

    // Cleanup intermediates
    pixDestroy(&gray);
    if (mask) pixDestroy(&mask);
    if (binarized && binarized != binary) pixDestroy(&binarized);
    if (binary && binary != result) pixDestroy(&binary);
    if (binStrong) pixDestroy(&binStrong);

    // Replace original with deskewed
    pixDestroy(&pixs);
    return result;
}
