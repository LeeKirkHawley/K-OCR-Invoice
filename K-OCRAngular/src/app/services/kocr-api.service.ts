// All code above imports removed. listDirectory will be inside the class below.
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class KocrApiService {
  private apiUrl = 'http://localhost:5055'; // Point to the C# API server

  constructor(private http: HttpClient) {}

  getFiles(directory: string, extensions?: string[]): Observable<string[]> {
    // TODO: Add support for extensions
    return this.http.get<string[]>(`${this.apiUrl}/File`, {
      params: { directory }
    });
  }

  listDirectory(path?: string | null) {
    const url = `${this.apiUrl}/File/ListDirectory`;
    let params = {};
    if (path) {
      params = { path };
    }
    console.log('🌐 API Call:', url, 'Params:', params);
    return this.http.get<any[]>(url, {
      params,
      responseType: 'json' as const
    });
  }

  // TODO: Add batch processing endpoint
  // processBatch(files: File[]): Observable<any> {
  //   // Call K-OCR-API batch processing endpoint here
  // }

  // TODO: Add settings endpoints if needed
}
