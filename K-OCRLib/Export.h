#pragma once

#ifdef KOCRLIB_EXPORTS
#define KOCRLIB_API __declspec(dllexport)
#else
#define KOCRLIB_API __declspec(dllimport)
#endif