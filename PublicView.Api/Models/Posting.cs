namespace PublicView.Api.Models;

public class Category
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "tag";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public List<Posting> Postings { get; set; } = new();
}

public enum PostingStatus
{
    PendingPayment = 0,
    Published = 1,
    Removed = 2
}

public class Posting
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string? City { get; set; }
    public string? ContactInfo { get; set; }
    public string Condition { get; set; } = "used";
    public List<string> ImageUrls { get; set; } = new();
    public string AiKeywords { get; set; } = string.Empty;
    public PostingStatus Status { get; set; } = PostingStatus.PendingPayment;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public List<Payment> Payments { get; set; } = new();
}

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2
}

public class Payment
{
    public Guid Id { get; set; }
    public Guid PostingId { get; set; }
    public Posting Posting { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "usd";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string Provider { get; set; } = "stripe";
    public string? StripeSessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
}
