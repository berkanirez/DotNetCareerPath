using FieldOps.Modules.WorkOrders;

namespace FieldOps.Api.Application;

// Day 79: the searchable copy of a work order — deliberately a small,
// flat, denormalized shape (not a reference to the module's own
// WorkOrderSummary), since a search index is not the source of truth and
// only ever needs the handful of fields a search result actually shows.
public record WorkOrderSearchDocument(int Id, int OrganizationId, string Title, WorkOrderStatus Status);
