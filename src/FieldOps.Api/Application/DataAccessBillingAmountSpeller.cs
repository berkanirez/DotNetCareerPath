using System.ServiceModel;
using FieldOps.Api.NumberConversionClient;

namespace FieldOps.Api.Application;

// Day 83: today's only IBillingAmountSpeller implementation — the ONE class
// in FieldOps.Api allowed to know that this particular gateway happens to
// be a SOAP service, or what its generated proxy types look like.
// Everything about NumberConversionSoapTypeClient, its request/response
// wrapper types, and its FaultException type stays inside this one file.
public class DataAccessBillingAmountSpeller : IBillingAmountSpeller
{
    public async Task<string> SpellAmountInWordsAsync(decimal amount, CancellationToken cancellationToken)
    {
        // A fresh client per call — the same deliberate simplification
        // RabbitMqEventPublisher made on Day 67 (a pooled, reused channel
        // would be the real production shape; not justified for today's
        // low-frequency, demo-scale calls).
        using var client = new NumberConversionSoapTypeClient(NumberConversionSoapTypeClient.EndpointConfiguration.NumberConversionSoap12);

        try
        {
            // ALTTAN ALTA: a real SOAP envelope goes out over HTTP here —
            // <NumberToDollars><dNum>250.00</dNum></NumberToDollars>, wrapped
            // in <soap:Envelope>. This one line is the entire "RPC call" ADR
            // 0008 described: no URL path names the operation, the operation
            // name lives inside the XML body instead.
            var response = await client.NumberToDollarsAsync(amount);
            return response.Body.NumberToDollarsResult;
        }
        catch (FaultException ex)
        {
            // Day 80's exact reasoning, transposed from Elasticsearch's
            // IsValidResponse to SOAP's own failure shape: FaultException is
            // this dependency's genuine, native way of reporting an error —
            // it does NOT map to an HTTP status code the way a REST client's
            // failure would. Translated here, at the boundary, into a plain
            // FieldOps exception, so nothing outside this class ever needs
            // to know what a SOAP Fault even is.
            throw new InvalidOperationException($"The billing amount-in-words service rejected {amount}: {ex.Message}", ex);
        }
    }
}
