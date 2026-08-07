namespace Lana.Models;

public sealed class AppUser
{
    public int Id { get; init; }
    public required string Username { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = "Member";
}
