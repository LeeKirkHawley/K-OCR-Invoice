import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-batch-process-dialog',
  templateUrl: './batch-process-dialog.component.html',
  styleUrls: ['./batch-process-dialog.component.css'],
  standalone: true,
  imports: [CommonModule]
})
export class BatchProcessDialogComponent {
  @Input() files: File[] = [];
  // TODO: Add logic to trigger batch processing via K-OCR-API
  // Example: this.kocrService.processBatch(this.files)
}
