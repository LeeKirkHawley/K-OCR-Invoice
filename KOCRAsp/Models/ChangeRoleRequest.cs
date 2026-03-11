namespace KOCRAsp.Models;

public class ChangeRoleRequest
{
    public string UserId { get; set; } = string.Empty;
    public string NewRole { get; set; } = string.Empty;
}
