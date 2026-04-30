using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace K_OCR.Models
{
    [Table("Batches")]
    public class Batch
    {
        [Key]
        public int BatchId { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public int BatchNumber { get; set; }

        [Required, MaxLength(1000)]
        public string FolderPath { get; set; } = string.Empty;

        [MaxLength(450)]
        public string? LockedByUserId { get; set; }

        public DateTime? LockAcquiredAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(450)]
        public string CreatedByUserId { get; set; } = string.Empty;

        /// <summary>When the batch was marked for deletion (soft-delete). Null if not deleted.</summary>
        public DateTime? MarkedForDeletionAtUtc { get; set; }
    }
}
