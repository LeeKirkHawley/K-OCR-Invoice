using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace K_OCR.Models
{
    [Table("UserBatchSessions")]
    public class UserBatchSession
    {
        [Required, MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        // SET NULL when the referenced batch is deleted (configured in OnModelCreating)
        public int? BatchId { get; set; }

        public DateTime LastAccessedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
