import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-results-view',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './results-view.component.html',
  styleUrls: ['./results-view.component.css']
})
export class ResultsViewComponent {
  @Input() results: any[] = [];
  // TODO: Display OCR results
}
