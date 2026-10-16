using System.Text;
using PublicView.Api.Data;
using Microsoft.EntityFrameworkCore;
using PublicView.Api.Models;
using Stripe;
using Stripe.Checkout;

namespace PublicView.Api.Services;

public interface IPaymentService
{
    bool IsConfigured { get; }
    public const decimal PostingFee = 5.00m;

    Task<string?> CreateCheckoutSessionAsync(Posting posting, string successUrl, string cancelUrl);
    Task<bool> CompletePaymentFromWebhookAsync(string requestBody, string stripeSignatureHeader);
    void CompletePaymentSimulated(Guid postingId);
}

public class PaymentService : IPaymentService
{
    public const string SessionIdMetadataPrefix = "sid_";

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(AppDbContext db, IConfiguration config, ILogger<PaymentService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
        if (IsConfigured)
        {
            StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];
        }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_config["Stripe:SecretKey"]);

    public async Task<string?> CreateCheckoutSessionAsync(Posting posting, string successUrl, string cancelUrl)
    {
        if (!IsConfigured) return null;

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            AllowedPaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = (long)(IPaymentService.PostingFee * 100),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"PublicView posting: {posting.Title}",
                            Description = "30-day classifieds listing"
                        }
                    }
                }
            },
            Metadata = new Dictionary<string, string>
            {
                ["posting_id"] = posting.Id.ToString()
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl
        });

        return session.Url;
    }

    public async Task<bool> CompletePaymentFromWebhookAsync(string requestBodyJson, string stripeSignatureHeader)
    {
        var webhookSecret = _config["Stripe:WebhookSecret"];
        var eventId = "unknown";

        try
        {
            var jsonEvent = webhookSecret is not null
                ? EventUtility.ConstructEvent(requestBodyJson, stripeSignatureHeader, webhookSecret)
                : EventUtility.ParseEvent(requestBodyJson);

            if (jsonEvent.Type != "checkout.session.completed") return true;
            eventId = jsonEvent.Id;

            var session = jsonEvent.Data.Object as Stripe.Checkout.Session;
            if (session?.Metadata is null || !session.Metadata.TryGetValue("posting_id", out var postingIdStr))
            {
                _logger.LogWarning("Checkout session completed without posting_id metadata");
                return true;
            }

            if (!Guid.TryParse(postingIdStr, out var postingId))
            {
                _logger.LogWarning("Could not parse posting_id metadata value: {Value}", postingIdStr);
                return false;
            }

            await CompletePaymentAsync(postingId, session.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stripe webhook processing failed for event {EventId}", eventId);
            return false;
        }
    }

    public void CompletePaymentSimulated(Guid postingId)
    {
        CompletePaymentAsync(postingId, $"simulated_{SessionIdMetadataPrefix}{postingId:N}").GetAwaiter().GetResult();
    }

    private async Task CompletePaymentAsync(Guid postingId, string sessionId)
    {
        var posting = await _db.Postings.FirstOrDefaultAsync(p => p.Id == postingId);
        if (posting is null) return;

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.PostingId == postingId) ?? new Payment { Id = Guid.NewGuid(), PostingId = postingId };
        payment.Amount = IPaymentService.PostingFee;
        payment.Status = PaymentStatus.Paid;
        payment.StripeSessionId = sessionId;
        payment.PaidAt = DateTimeOffset.UtcNow;

        if (posting.Status == PostingStatus.PendingPayment)
        {
            posting.Status = PostingStatus.Published;
        }

        await _db.SaveChangesAsync();
    }
}
