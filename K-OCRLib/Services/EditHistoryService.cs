using K_OCRLib.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace K_OCRLib.Services;

/// <summary>
/// Service for serializing and deserializing edit histories.
/// Handles both the new FieldEdit list format and legacy Map format for backward compatibility.
/// </summary>
public class EditHistoryService
{
    private readonly ILogger<EditHistoryService> _logger;

    public EditHistoryService(ILogger<EditHistoryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Deserializes an edit JSON string (which may be new FieldEdit format or legacy Map format)
    /// into a FieldEdit list.
    /// </summary>
    public List<FieldEdit> DeserializeEdits(string? editsJson)
    {
        if (string.IsNullOrWhiteSpace(editsJson))
            return new List<FieldEdit>();

        try
        {
            // Try to deserialize as FieldEdit list (new format)
            var edits = JsonConvert.DeserializeObject<List<FieldEdit>>(editsJson);
            if (edits != null && edits.Count > 0 && edits[0].UserName != null)
            {
                // It's the new format (has UserName field)
                return edits;
            }
        }
        catch (JsonException)
        {
            // Not the new format, try legacy format
        }

        try
        {
            // Try to deserialize as legacy format: array-of-pairs [[field, value], ...]
            var legacyData = JsonConvert.DeserializeObject<List<List<object>>>(editsJson);
            if (legacyData != null)
            {
                var edits = new List<FieldEdit>();
                var baseTime = DateTime.UtcNow;
                
                foreach (var pair in legacyData)
                {
                    if (pair.Count >= 2)
                    {
                        edits.Add(new FieldEdit
                        {
                            FieldName = pair[0].ToString() ?? string.Empty,
                            Value = pair[1].ToString() ?? string.Empty,
                            UserName = "legacy", // Legacy data has no user info
                            TimestampUtc = baseTime
                        });
                    }
                }
                
                if (edits.Count > 0)
                {
                    _logger.LogInformation("Converted {Count} legacy-format edits to new FieldEdit format", edits.Count);
                }
                return edits;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize edits in either format");
            return new List<FieldEdit>();
        }

        return new List<FieldEdit>();
    }

    /// <summary>
    /// Serializes a FieldEdit list to JSON string.
    /// </summary>
    public string SerializeEdits(List<FieldEdit>? edits)
    {
        if (edits == null || edits.Count == 0)
            return "[]";

        return JsonConvert.SerializeObject(edits);
    }

    /// <summary>
    /// Creates a new FieldEdit from a field name, value, and current user.
    /// </summary>
    public FieldEdit CreateEdit(string fieldName, string value, string userName)
    {
        return new FieldEdit
        {
            FieldName = fieldName,
            Value = value,
            UserName = userName,
            TimestampUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Appends a new edit to an existing edit list and returns the serialized JSON.
    /// </summary>
    public string AppendEdit(string? currentEditsJson, string fieldName, string value, string userName)
    {
        var edits = DeserializeEdits(currentEditsJson);
        edits.Add(CreateEdit(fieldName, value, userName));
        return SerializeEdits(edits);
    }
}
