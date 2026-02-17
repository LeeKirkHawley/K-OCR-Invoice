using Avalonia.Controls;
using Avalonia.VisualTree;
using K_OCR.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace K_OCRDesktop.Services
{
    public class ValidationPanelService
    {
        private Avalonia.Controls.ScrollViewer? _validationScrollViewer;

        public ValidationPanelService(Avalonia.Controls.ScrollViewer validationScrollViewer)
        {
            _validationScrollViewer = validationScrollViewer;
        }

        public void ScrollToField(int fieldIndex)
        {
            if (_validationScrollViewer == null)
                return;

            // Estimate the vertical position of the field (approximately 40 pixels per field)
            double estimatedFieldHeight = 40;
            double targetOffset = fieldIndex * estimatedFieldHeight;

            // Scroll to show the field with some padding above
            _validationScrollViewer.Offset = new Avalonia.Vector(0, Math.Max(0, targetOffset - 50));
        }

        public bool FocusFieldTextBox(DocumentField field)
        {
            if (_validationScrollViewer == null)
                return false;

            // Find all TextBoxes in the validation panel
            var textBoxes = _validationScrollViewer.GetVisualDescendants()
                .OfType<TextBox>()
                .Where(tb => tb.Tag != null && tb.Tag.ToString() == field.Name)
                .ToList();

            // Focus the first matching TextBox
            var tb = textBoxes.FirstOrDefault();
            if (tb != null)
            {
                tb.Focus();
                return true;
            }

            return false;
        }

        public bool FocusLineItemTextBox(InvoiceItemDto lineItem)
        {
            if (_validationScrollViewer == null)
                return false;

            // Try to find a TextBox whose DataContext is the same line item instance
            var textBoxes = _validationScrollViewer.GetVisualDescendants()
                .OfType<TextBox>();

            foreach (var tb in textBoxes)
            {
                if (object.ReferenceEquals(tb.DataContext, lineItem) || tb.DataContext != null && tb.DataContext.Equals(lineItem))
                {
                    tb.Focus();
                    return true;
                }
            }

            // If not found directly, try to find a parent control (e.g., Border/Grid) with that DataContext
            var descendants = _validationScrollViewer.GetVisualDescendants().OfType<Avalonia.Controls.Control>();
            foreach (var ctrl in descendants)
            {
                if (object.ReferenceEquals(ctrl.DataContext, lineItem) || (ctrl.DataContext != null && ctrl.DataContext.Equals(lineItem)))
                {
                    // Find the first TextBox descendant of this control
                    var tb = ctrl.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                    if (tb != null)
                    {
                        tb.Focus();
                        return true;
                    }
                }
            }

            // Fallback - focus the first TextBox
            var first = _validationScrollViewer.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (first != null)
            {
                first.Focus();
                return false;
            }

            return false;
        }

        public void ClearFocus()
        {
            // No specific action needed
        }
    }
}
