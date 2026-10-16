using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PublicView.Api.Data;
using PublicView.Api.Models;
using PublicView.Api.Services;

namespace PublicView.Api.Controllers;

[ApiController]
[Route("api/payments")]
[EnableCors("spa")]
public class PaymentsController(AppDbContext db, IPaymentService payments, IWebHostEnvironment env) : ControllerBase
{
    [Authorize]
    [HttpPost("checkout/{postingId:guid}")]
    public async Task<IActionResult> Checkout(Guid postingId)
    {
        var posting = await db.Postings.FirstOrDefaultAsync(p => p.Id == postingId);
        if (posting is null) return NotFound();

        if (posting.Status == PostingStatus.Published)
            return Ok(new { alreadyPaid = true, simulated = false });

        var baseUrl = Request.Headers.Origin.FirstOrDefault() ?? $"{Request.Scheme}://{Request.Host}";
        var successUrl = $"{baseUrl}/success?posting={postingId}";
        var cancelUrl = $"{baseUrl}/post/{postingId}/pay?canceled=1";

        if (payments.IsConfigured)
        {
            var url = await payments.CreateCheckoutSessionAsync(posting, successUrl, cancelUrl);
            if (url is not null) return Ok(new { checkoutUrl = url, simulated = false });
        }

        if (!env.IsDevelopment())
            return Problem("Payments are not configured (missing Stripe key).");

        payments.CompletePaymentSimulated(postingId);
        return Ok(new { simulated = true });
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();
        var handled = await payments.CompletePaymentFromWebhookAsync(json, Request.Headers["Stripe-Signature"]);
        return handled ? Ok() : BadRequest();
    }
}
