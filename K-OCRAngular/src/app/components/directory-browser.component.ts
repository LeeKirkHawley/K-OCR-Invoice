import { Component, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { KocrApiService } from '../services/kocr-api.service';

interface DirectoryEntry {
  name: string;
  path: string;
  isDirectory: boolean;
}

@Component({
  selector: 'app-directory-browser',
  templateUrl: './directory-browser.component.html',
  styleUrls: ['./directory-browser.component.css'],
  standalone: true,
  imports: [CommonModule]
})
export class DirectoryBrowserComponent implements OnDestroy {
  entries: DirectoryEntry[] = [];
  currentPath: string | null = null;
  loading = false;
  error: string | null = null;
  pathStack: string[] = [];

  constructor(private kocrApi: KocrApiService, private cdr: ChangeDetectorRef) {
    const ts = new Date().toISOString();
    console.log(`🔄 [${ts}] DirectoryBrowserComponent constructor called, loading:`, this.loading);
    this.loadDirectory();
  }

  loadDirectory(path: string | null = null) {
    const timestamp = new Date().toISOString();
    console.log(`⏰ [${timestamp}] Loading directory:`, path);
    this.loading = true;
    this.error = null;
    console.log('API URL:', this.kocrApi['apiUrl']); // Access private property for debugging
    this.kocrApi.listDirectory(path).subscribe({
      next: (data) => {
        const ts = new Date().toISOString();
        console.log(`✅ [${ts}] API call successful`);
        console.log('Received data:', data);
        console.log('Data type:', typeof data);
        console.log('Data length:', Array.isArray(data) ? data.length : 'not array');
        console.log('First item:', data && data.length > 0 ? data[0] : 'no data');
        console.log('Setting loading to false...');
        this.entries = data;
        this.currentPath = path;
        this.loading = false;
        console.log('Loading state after setting:', this.loading);
        this.cdr.detectChanges(); // Force change detection
        console.log('Change detection triggered');
        console.log('Entries set:', this.entries);
        console.log('Current entries in component:', this.entries);
      },
      error: (err) => {
        const ts = new Date().toISOString();
        console.error(`❌ [${ts}] API call failed:`, err);
        console.error('Error details:', err.message);
        console.error('Error status:', err.status);
        this.error = 'Failed to load directory: ' + err.message;
        this.loading = false;
        this.cdr.detectChanges(); // Force change detection
      }
    });
  }

  enterDirectory(entry: DirectoryEntry) {
    if (entry.isDirectory) {
      this.pathStack.push(this.currentPath || '');
      this.loadDirectory(entry.path);
    }
  }

  goUp() {
    if (this.pathStack.length > 0) {
      const prev = this.pathStack.pop() || null;
      this.loadDirectory(prev);
    }
  }

  ngOnDestroy() {
    const ts = new Date().toISOString();
    console.log(`🗑️ [${ts}] DirectoryBrowserComponent destroyed`);
  }
}
