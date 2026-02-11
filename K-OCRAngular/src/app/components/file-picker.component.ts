import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { KocrApiService } from '../services/kocr-api.service';

interface DirectoryEntry {
  name: string;
  path: string;
  isDirectory: boolean;
}

@Component({
  selector: 'app-file-picker',
  templateUrl: './file-picker.component.html',
  styleUrls: ['./file-picker.component.css'],
  standalone: true,
  imports: [CommonModule]
})
export class FilePickerComponent implements OnInit {
  entries: DirectoryEntry[] = [];
  currentPath: string | null = null;
  loading = false;
  error: string | null = null;
  pathStack: string[] = [];

  constructor(private kocrApi: KocrApiService) {}

  ngOnInit() {
    this.loadDirectory();
  }

  loadDirectory(path: string | null = null) {
    this.loading = true;
    this.error = null;
    this.kocrApi.listDirectory(path).subscribe({
      next: (data) => {
        this.entries = data;
        this.currentPath = path;
        this.loading = false;
      },
      error: (err) => {
        this.error = 'Failed to load directory: ' + err.message;
        this.loading = false;
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
}
