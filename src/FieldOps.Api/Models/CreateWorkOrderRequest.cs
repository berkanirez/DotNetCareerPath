using System.ComponentModel.DataAnnotations;

namespace FieldOps.Api.Models;

// OrganizationId is not part of the request body, same reasoning as
// CreateEmployeeRequest (Day 35): it comes from the X-Organization-Id
// header instead, never from something the client states directly.
public record CreateWorkOrderRequest([Required, StringLength(200)] string Title);
