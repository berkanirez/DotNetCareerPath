using System.ComponentModel.DataAnnotations;

namespace FieldOps.Api.Models;

// OrganizationId is not part of the request body, same reasoning as
// CreateEmployeeRequest (Day 35): it comes from the X-Organization-Id
// header instead, never from something the client states directly.
// CustomerId (Day 47) IS part of the body — optional, since not every
// work order needs a customer on record today.
public record CreateWorkOrderRequest([Required, StringLength(200)] string Title, int? CustomerId = null);
