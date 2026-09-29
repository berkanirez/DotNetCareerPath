using FieldOps.Api.Application;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

// Day 83: a small, standalone demo controller — proving ADR 0008's SOAP
// isolation pattern actually works, not yet wiring it into a real FieldOps
// workflow (e.g. WorkOrdersController.Complete). Deliberately has no
// X-Organization-Id/X-Employee-Id checks: it touches no tenant data at all,
// only a public, stateless amount-to-words conversion. A real production
// use (e.g. printing a billed amount on a customer-facing invoice) would
// live inside an already-authenticated action instead of its own controller.
[ApiController]
[Route("api/[controller]")]
public class BillingController : ControllerBase
{
    private readonly IBillingAmountSpeller _billingAmountSpeller;

    public BillingController(IBillingAmountSpeller billingAmountSpeller)
    {
        _billingAmountSpeller = billingAmountSpeller;
    }

    [HttpGet("amount-in-words")]
    public async Task<ActionResult<string>> GetAmountInWords([FromQuery] decimal amount, CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            return BadRequest("Amount must be a positive number.");
        }

        try
        {
            var words = await _billingAmountSpeller.SpellAmountInWordsAsync(amount, cancellationToken);
            return Ok(words);
        }
        catch (InvalidOperationException ex)
        {
            // Day 83: the SOAP Fault, already translated by DataAccessBillingAmountSpeller
            // into a plain FieldOps exception, is handled here exactly the
            // way any other failed dependency call in this codebase is —
            // this controller never learns that a "fault" was ever involved.
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }
}
