namespace FieldOps.Api.Application;

// Day 83: ADR 0008's isolation pattern, now real — WorkOrdersController (or
// any future billing-related controller) depends only on THIS interface,
// never on FieldOps.Api.NumberConversionClient's generated SOAP types
// (NumberConversionSoapTypeClient, FaultException, EndpointConfiguration).
// The realistic scenario ADR 0008 named: writing a billed amount out in
// words, the way an invoice or a check does — today's public demo SOAP
// service (dataaccess.com's NumberConversion) stands in for whatever real
// legacy accounting/ERP system would actually offer this in production.
public interface IBillingAmountSpeller
{
    Task<string> SpellAmountInWordsAsync(decimal amount, CancellationToken cancellationToken);
}
