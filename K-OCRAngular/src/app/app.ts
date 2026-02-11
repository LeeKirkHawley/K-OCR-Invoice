import { Component, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { KocrApiService } from './services/kocr-api.service';
import { DirectoryBrowserComponent } from './components/directory-browser.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [DirectoryBrowserComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private http = inject(HttpClient);
  
  protected readonly title = signal('K-OCRAngular');
  // All file picker logic removed. Directory browser is now the only navigation UI.
}
