import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-settings-dialog',
  templateUrl: './settings-dialog.component.html',
  styleUrls: ['./settings-dialog.component.css'],
  standalone: true,
  imports: [CommonModule]
})
export class SettingsDialogComponent {
  // TODO: Add settings fields and logic
  // Example: @Input() settings: any;
  // TODO: Save settings to backend or local storage
}
