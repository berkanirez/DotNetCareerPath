# ADR 0006: REST Versus Messaging — When to Use Which

## Status

Accepted — 2026-09-28 (Phase 4, Day 77)

## Context

Day 76 genuinely extracted `FieldOps.NotificationService` as a separate, independently-deployed process, communicating with `FieldOps.Api` only through RabbitMQ. FieldOps, however, already had a second, much older example of cross-actor communication that was never built on messaging at all: `WorkOrdersController.Approve` — a customer confirming, over plain HTTP, that a completed work order is acceptable. No ADR has ever stated *why* one is REST and the other is messaging, or what should decide that choice the next time a new interaction is designed (a reporting service reading data from `FieldOps.Api`, or any future service-to-service call).

Week 15's roadmap explicitly names this as its own topic ("REST versus messaging decisions") — this ADR states the rule once, so it doesn't need to be re-litigated from scratch for every future interaction.

## Decision

FieldOps uses the following rule to decide between REST (synchronous HTTP) and messaging (asynchronous, via RabbitMQ) for any given interaction:

1. **If the caller needs the real result before it can proceed, use REST.** `WorkOrdersController.Approve` is the existing example: a customer's HTTP request only completes once `_workOrderDirectory.Approve(id)` has actually run and either succeeded or failed — the caller cannot meaningfully continue (or render a result to a human) without that answer. A message queue has no natural way to hand back "the answer" to the original caller; forcing one to do so (a request/reply pattern over a queue) adds real complexity to solve a problem synchronous HTTP already solves for free.
2. **If the caller does not need to wait for the result, and the receiver being temporarily unreachable is acceptable, use messaging.** `WorkOrdersController.Complete` is the existing example: it writes an outbox row and returns immediately — it does not need to know whether `FieldOps.NotificationService` (or the in-process audit consumer) has processed the event yet, or is even reachable at that exact moment. This is exactly what Day 72's retry/backoff and Day 71's Outbox pattern exist to make safe: the receiver can be down for a while without the caller ever noticing or failing.
3. **If a service needs to *ask a question* of another service's current data (a query, not a notification of something that already happened), use REST, not messaging.** A future reporting service asking "how many work orders did organization 7 complete this month" is a query with one caller waiting on one answer — the same shape as point 1, just between two services instead of a client and a service. Messaging is built for "broadcast a fact to whoever is listening," not "ask one specific question and wait for one specific answer."

The deciding factor in all three cases is never the topic (an event about a business fact vs. a request for confirmation) or the level of "importance" — it's whether the interaction is fundamentally a **question-that-needs-an-answer-now** (REST) or a **fact-being-announced-for-whoever-cares** (messaging).

## Consequences

* **Positive:** Both existing interactions in the codebase already follow this rule without having been designed against it explicitly — this ADR mostly documents a decision that was already made correctly, twice, by instinct, and makes the reasoning explicit and reusable for the next interaction that needs deciding.
* **Positive:** Future service-to-service reads (e.g. a reporting service querying `FieldOps.Api`) have a default answer (REST) instead of defaulting to "everything is a message now that we have RabbitMQ" — a common overcorrection once a message broker exists in a system.
* **Cost (deferred, not solved today):** This ADR does not address what happens when a REST call's target is a genuinely separate, independently-deployed service that might be temporarily down (unlike today's in-process `Approve`, where "the receiver is down" isn't a real scenario). Real inter-service REST calls need their own resilience story (timeouts, retries, circuit breaking) — none of which exists yet, because no REST call between two independently-deployed FieldOps services exists yet either. That is a problem for the day such a call is actually built, not today.
* **No code changed today.** This ADR is a decision rule, stated once, for future service-boundary design — not an implementation.

## Alternatives Considered

* **Decide REST vs. messaging case by case, with no written rule.** Rejected — this is exactly what happened until today (both existing choices turned out correct, but by instinct, not by a stated rule), and the next person (or the next session) making this decision has nothing to check the choice against. A named rule now means a badly-reasoned choice next time is at least visible as breaking one.
* **Route everything through RabbitMQ, including request/reply-shaped interactions like `Approve`, now that a message broker already exists in the stack.** Rejected — a request/reply-over-a-queue pattern exists (correlation IDs, reply-to queues) but adds real infrastructure and complexity to solve a problem synchronous HTTP already solves directly; introducing it here would be exactly the premature complexity this workspace has consistently avoided (ADR 0003's Redis/database-per-module reasoning, Day 63's `IAiProvider`).
