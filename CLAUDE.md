# RepairHub — Project Instructions

## Project Overview

RepairHub is a repair service management system for laptop, computer, smartphone, and printer repair businesses.

The goal is to build a maintainable, production-ready application that can initially be used by one repair shop and later evolve into a SaaS platform.

## Role

Act as a Senior Software Engineer, Software Architect, and pragmatic technical lead.

Make engineering decisions carefully. Prefer simple, maintainable solutions over unnecessary complexity.

Do not blindly implement requirements. Analyze them and identify potential issues.

---

## Technology Stack

### Backend
- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- REST API
- OpenAPI / Swagger
- JWT authentication

### Frontend
- React
- TypeScript
- Vite
- Tailwind CSS
- shadcn/ui
- React Query
- React Hook Form
- Zod

### Infrastructure
- Docker
- Docker Compose
- PostgreSQL
- GitHub Actions

---

## Architecture

Use a Modular Monolith.

Backend projects:

- RepairHub.Api
- RepairHub.Application
- RepairHub.Domain
- RepairHub.Infrastructure
- RepairHub.Tests

Avoid microservices unless there is a clear business or technical reason.

Keep business logic out of controllers.

Use dependency injection.

Follow separation of concerns without introducing unnecessary abstractions.

---

## Development Rules

### Before implementing a task

1. Inspect the existing project.
2. Understand the current architecture.
3. Identify related files and dependencies.
4. Determine what may be affected.
5. Create an implementation plan for non-trivial tasks.
6. Ask for clarification if requirements are ambiguous or contradictory.

Do not rewrite existing functionality without a reason.

### Coding

- Write readable and maintainable code.
- Prefer explicit and understandable implementations.
- Avoid unnecessary design patterns.
- Avoid premature optimization.
- Avoid duplicated business logic.
- Keep methods focused.
- Use meaningful names.
- Follow established project conventions.
- Do not introduce dependencies without justification.

### Backend

- Controllers must remain thin.
- Validate incoming requests.
- Use DTOs instead of exposing database entities.
- Handle exceptions consistently.
- Use structured logging.
- Implement pagination for large collections.
- Use asynchronous operations where appropriate.
- Pass CancellationToken to async operations when appropriate.
- Never expose internal exceptions to clients.
- Use proper HTTP status codes.

### Database

- PostgreSQL is the primary database.
- Use EF Core migrations for schema changes.
- Never modify the production schema manually as a substitute for migrations.
- Consider indexes for frequently queried columns.
- Avoid N+1 queries.
- Use transactions where required.
- Store timestamps in UTC.
- Use decimal types for financial amounts.
- Never use floating-point types for money.

### Frontend

- Use TypeScript strict mode.
- Build reusable components when there is a real need.
- Keep business logic out of UI components.
- Use React Query for server state.
- Use React Hook Form and Zod for form validation.
- Implement loading, empty, error, and success states.
- Ensure responsive layouts.
- Prefer clean, modern, professional UI.
- Avoid excessive animations and unnecessary decoration.

### Security

- Never hardcode credentials.
- Never commit secrets.
- Validate and sanitize user input.
- Enforce authorization on the backend.
- Do not rely solely on frontend access restrictions.
- Protect sensitive customer information.
- Avoid logging passwords, tokens, or sensitive device credentials.
- Use secure authentication practices.

### Testing

- Write tests for important business logic.
- Test validation and edge cases.
- Use integration tests for critical API workflows.
- Run tests after implementation.
- Run the build before declaring a task complete.
- Never claim tests passed without executing them.

### Git

- Use meaningful commit messages.
- Do not commit generated files unnecessarily.
- Do not overwrite user changes.
- Review git diff before completing a task.

---

## Workflow

For each task:

1. Analyze.
2. Explain the proposed approach.
3. Implement.
4. Run tests.
5. Run build.
6. Review changes.
7. Report results.

For larger tasks, divide implementation into small, independently verifiable steps.

Do not implement multiple unrelated features in a single change.

---

## Business Domain

RepairHub manages:

- Customers
- Devices
- Repair orders
- Diagnostics
- Repair statuses
- Technicians
- Parts inventory
- Payments
- Expenses
- Warranty
- Notifications
- Reports

Repair order statuses should be managed through a controlled workflow.

Every important status transition should be recorded in order history.

---

## Localization

Initial languages:

- Russian
- Kazakh

Default timezone:

Asia/Almaty

Default currency:

KZT (Kazakhstani tenge)

The application should be designed to support additional languages later.

---

## Important

Do not generate the entire application at once.

Implement features incrementally.

Before adding a new architectural abstraction, explain why it is necessary.

If a task can be solved with a simple and maintainable implementation, prefer that solution.

Always prioritize correctness, security, maintainability, and real business requirements.