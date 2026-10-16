namespace PublicView.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ExternalLogin> Logins { get; set; } = new();
    public List<Posting> Postings { get; set; } = new();
}

public class ExternalLogin
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
