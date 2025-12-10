// K-OCRLibRunner.cpp : This file contains the 'main' function. Program execution begins and ends there.
//
#include <iostream>
#include "..\K-OCRLib\ImageToText.h"

int main()
{
    ImageToText ocr;

    // Initialize Tesseract here; no environment variables needed.
    // Ensure this path contains a "tessdata" subfolder with eng.traineddata.
    const char* datapath = "C:/Program Files/Tesseract-OCR/tessdata";
    if (!ocr.Initialize(datapath, "eng")) {
        std::cerr << "Failed to initialize Tesseract. Expected: " << datapath << "/tessdata/eng.traineddata" << std::endl;
        return 1;
    }

    //char* result = ocr.OCRImage("E:\\Datasets\\High-Quality Invoice Images for OCR\\batch_1\\batch_1\\batch1_1\\batch1-0001.jpg");
    char* result = ocr.OCRImage("E:/Datasets/Bad Images/Scan_20251206.jpg");
    std::cout << result << std::endl;

    //ocr.FreeMessage(const_cast<char*>(result.c_str()));
    ocr.FreeMessage(result);

    return 0;
}
// Run program: Ctrl + F5 or Debug > Start Without Debugging menu
// Debug program: F5 or Debug > Start Debugging menu

// Tips for Getting Started: 
//   1. Use the Solution Explorer window to add/manage files
//   2. Use the Team Explorer window to connect to source control
//   3. Use the Output window to see build output and other messages
//   4. Use the Error List window to view errors
//   5. Go to Project > Add New Item to create new code files, or Project > Add Existing Item to add existing code files to the project
//   6. In the future, to open this project again, go to File > Open > Project and select the .sln file
