# Evidence Management Framework

EMF is a provider-neutral platform for traceable, protected, and
review-gated evidence workflows.

The solution targets .NET 10 and separates platform services, security,
persistence, intelligence providers, orchestration, presentation, and
Domain Extensions through explicit architectural boundaries.

## Build and Test

Build with `dotnet build EMF.sln`.

Test with `dotnet test EMF.sln`.

## Console

Display command help with
`dotnet run --project src/EMF.Console -- help`.

## Documentation

- [Architecture decisions](docs/DECISIONS.md)
- [EMF terminology](docs/EMF-Terminology.md)
- [Azure OpenAI operations](docs/AZURE_OPENAI_OPERATIONS.md)
- [Daily close checklist](docs/checkpoints/DAILY_CLOSE_CHECKLIST.md)
- [Intelligence boundary](docs/architecture/ADR-026-intelligence-services-agent-boundary.md)
- [Production provider](docs/architecture/ADR-027-initial-production-intelligence-provider.md)
