# 04. Event Architecture

## Overview

The platform uses an event-driven internal architecture to enable communication between modules while preserving strict module boundaries.

Modules never call each other directly.

Communication occurs through:

- Domain Events
- Integration Events

This architecture supports:

- Loose coupling
- Eventual consistency
- Future service extraction
- Outbox processing
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

The platform uses the Outbox Pattern.

Purpose:

- Reliable event delivery
- Prevent lost messages
- Support eventual consistency

---

## Outbox Flow

1. Business transaction completes.
2. Integration event stored in Outbox.
3. Transaction commits.
4. Hangfire processes Outbox.
5. Event dispatched.
6. Event marked processed.

---

## Outbox Table

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

Direct module service calls

```text
Leave -> EmployeeService
Recruitment -> DocumentService
```

Avoid:

Cross-module DbContext usage

Avoid:

Shared entities

Avoid:

Synchronous cross-module workflows

---

## AI Implementation Rules

AI-generated code must:

- Publish integration events
- Use outbox
- Respect module boundaries
- Use idempotent consumers

AI must not:

- Add module references
- Bypass event contracts
- Use direct service calls

---

## Acceptance Criteria

1. Modules communicate through events.
2. Domain events remain module-local.
3. Integration events support cross-module communication.
4. Outbox guarantees delivery.
5. Consumers are idempotent.
6. Notifications are event-driven.
7. Reporting updates are event-driven.
8. Direct module dependencies are avoided.
