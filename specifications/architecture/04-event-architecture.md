# 04. Event Architecture

## Overview

The platform uses events and explicit cross-module contracts while preserving strict module boundaries.

Modules never reference another module's implementation project or query another module's tables directly.

Communication occurs through:

- Domain Events
- Integration Events
- Module-owned contracts and purpose-specific read models for synchronous queries or coordinated operations

This architecture supports:

- Loose coupling
- Eventual consistency
- Future service extraction
- Optional module-scoped outbox processing where reliable deferred delivery is required
- Reliable notification delivery

---

## Event Types

### Domain Events

Domain Events are internal to a module.

Purpose:

- Trigger behaviour inside the same module
- Enforce business rules
- Maintain consistency

Examples:

```text
EmployeeCreated
LeaveApproved
CandidateHired
```

Domain Events must not be consumed outside the owning module.

---

### Integration Events

Integration Events are used for cross-module communication.

Purpose:

- Notify other modules
- Trigger workflows
- Trigger notifications
- Trigger reporting updates

Examples:

```text
EmployeeCreatedIntegrationEvent
CandidateHiredIntegrationEvent
LeaveApprovedIntegrationEvent
```

---

## Event Ownership

The module that owns the business capability owns the event.

Examples:

| Event | Owner |
|---------|---------|
| EmployeeCreated | Employees |
| LeaveApproved | Leave |
| CandidateHired | Recruitment |
| TaskCompleted | Tasks |

---

## Event Flow

Typical flow:

```text
Request
  ->
Handler
  ->
Domain Entity
  ->
Domain Event
  ->
Integration Event
  ->
Outbox
  ->
Consumers
```

---

## Domain Events

### Characteristics

- Synchronous
- In-process
- Transactional
- Module-local

### Examples

EmployeeCreated

Triggers:

- Audit entry
- Internal projections

LeaveApproved

Triggers:

- Leave balance updates
- Internal calculations

---

## Integration Events

### Characteristics

- Asynchronous
- Cross-module
- Eventually consistent
- Reliable delivery

### Examples

CandidateHiredIntegrationEvent

Consumers:

- Employees
- Tasks
- Notifications

---

## Event Naming

Events must use:

Past tense business language.

Good:

```text
EmployeeCreated
LeaveApproved
CandidateHired
```

Bad:

```text
CreateEmployee
ProcessLeave
HandleCandidate
```

---

## Event Contracts

Integration Events live in:

```text
Contracts/Events
```

Event contracts should contain only:

- Identifiers
- Relevant business data
- Timestamps

Avoid exposing entities.

---

## Outbox Pattern

An outbox is not a universal V1 requirement. Integration events may be dispatched in-process where that is sufficient for the workflow and failure model.

A module may adopt a local outbox when it has a concrete requirement for durable deferred delivery. The Companies module currently uses a scoped outbox for selected events; this does not require every module to add one.

Purpose:

- Reliable event delivery
- Prevent lost messages
- Support eventual consistency

---

## Optional Outbox Flow

1. Business transaction completes.
2. Integration event stored in Outbox.
3. Transaction commits.
4. Hangfire processes Outbox.
5. Event dispatched.
6. Event marked processed.

---

## Optional Outbox Table

Suggested fields:

- Id
- EventType
- Payload
- CreatedAt
- ProcessedAt
- Status

---

## Event Consumers

Consumers should:

- Be idempotent
- Handle retries
- Handle duplicates safely

---

## Idempotency

Consumers must support repeated delivery.

Example:

CandidateHired event received twice.

Result:

Employee should only be created once.

---

## Retry Strategy

Failed event processing should:

- Retry automatically
- Log failures
- Escalate persistent failures

Handled through Hangfire.

---

## Notifications Integration

Business modules never send notifications directly.

Instead:

```text
LeaveApproved
  ->
LeaveApprovedIntegrationEvent
  ->
Notification Consumer
  ->
Email/In-App Notification
```

---

## Reporting Integration

Reports consume integration events to maintain local projections.

Example:

```text
EmployeeCreated
  ->
Reporting Projection Update
```

---

## Read Model Updates

Modules may maintain local projections.

Example:

Tasks module stores:

- EmployeeId
- EmployeeName

Source of truth remains Employees module.

---

## Event Versioning

Event contracts should be versioned carefully.

Guidelines:

- Prefer additive changes
- Avoid breaking changes
- Maintain backward compatibility

---

## Observability

Track:

- Events published
- Events processed
- Failed events
- Retry counts

Metrics should be available.

---

## Auditing

Audit:

- Event publication
- Event failures
- Event retries

---

## Anti-Patterns

Avoid:

Direct calls to another module's implementation service

```text
Leave -> EmployeeService
Recruitment -> DocumentService
```

An interface or read model deliberately exposed as a cross-module contract is permitted. The consuming module depends on the contract, not the providing module's implementation assembly.

Avoid:

Cross-module DbContext usage

Avoid:

Shared entities

Avoid:

Synchronous cross-module workflows

---

## AI Implementation Rules

AI-generated code must:

- Use integration events or explicit cross-module contracts where appropriate
- Respect module boundaries
- Make consumers idempotent when delivery can repeat
- Use an outbox only where the owning module has explicitly adopted one for a concrete reliability requirement

AI must not:

- Add module references
- Bypass event contracts
- Use direct service calls

---

## Acceptance Criteria

1. Modules communicate through events or explicit module-owned contracts.
2. Domain events remain module-local.
3. Integration events support cross-module communication.
4. Modules that adopt an outbox persist and dispatch it reliably; other modules are not required to implement one.
5. Consumers are idempotent.
6. Notifications are event-driven.
7. Reporting obtains data through module-owned read contracts; projections may use events where a report explicitly needs one.
8. Direct module dependencies are avoided.
