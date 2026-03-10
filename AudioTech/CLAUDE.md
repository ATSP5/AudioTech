# AudioTech — Claude Code Guide

## Project Overview

**AudioTech** is an Avalonia UI desktop application for audio file analysis.
- **Framework:** Avalonia UI (net8.0) with CommunityToolkit.Mvvm
- **Architecture:** Clean Architecture + DDD + CQRS
- **DI:** Microsoft.Extensions.DependencyInjection (registered in `App.axaml.cs`)

---

## Architecture

### Layer Diagram

```
┌─────────────────────────────────────────┐
│         Presentation (Views/ViewModels) │  ← Avalonia UI, MVVM
├─────────────────────────────────────────┤
│         Application (CQRS)             │  ← Commands, Queries, Handlers
├─────────────────────────────────────────┤
│         Domain (DDD)                   │  ← Entities, Value Objects, Events
├─────────────────────────────────────────┤
│         Infrastructure                 │  ← Repos, Services, Dispatchers
└─────────────────────────────────────────┘
```

### Dependency Rule
Dependencies flow **inward only**:
`Infrastructure` → `Application` → `Domain`
`Presentation` → `Application` (via dispatchers only, never directly to Domain/Infrastructure)

---

## Folder Structure

```
AudioTech/
├── Domain/                     # Core business logic — no external dependencies
│   ├── Common/
│   │   ├── AggregateRoot.cs   # Base for aggregate roots (holds domain events)
│   │   ├── Entity.cs          # Base entity with Guid Id + equality
│   │   ├── ValueObject.cs     # Structural equality base
│   │   └── IDomainEvent.cs    # Marker interface for domain events
│   ├── Entities/
│   │   └── AudioFile.cs       # Aggregate root
│   ├── ValueObjects/
│   │   ├── AudioFilePath.cs   # Validated file path (supported formats)
│   │   └── FrequencyRange.cs  # Hz range with named presets
│   ├── Events/
│   │   ├── AudioFileLoadedEvent.cs
│   │   └── AudioAnalysisCompletedEvent.cs
│   └── Repositories/
│       └── IAudioFileRepository.cs  # Interface only — impl in Infrastructure
│
├── Application/                # Use cases — depends only on Domain
│   ├── Abstractions/
│   │   ├── ICommand.cs / ICommandHandler.cs
│   │   ├── IQuery.cs / IQueryHandler.cs
│   │   ├── ICommandDispatcher.cs
│   │   └── IQueryDispatcher.cs
│   ├── Commands/
│   │   ├── LoadAudioFile/     # Command + Handler per feature folder
│   │   └── AnalyseAudio/
│   ├── Queries/
│   │   ├── GetAudioAnalysis/  # Query + Result + Handler per feature folder
│   │   └── GetAudioFiles/
│   └── Services/
│       └── IAudioAnalysisService.cs  # Interface for audio processing
│
├── Infrastructure/             # Implements Application interfaces
│   ├── Dispatchers/
│   │   ├── CommandDispatcher.cs
│   │   └── QueryDispatcher.cs
│   ├── Repositories/
│   │   └── InMemoryAudioFileRepository.cs  # Replace with real persistence
│   ├── Services/
│   │   └── AudioAnalysisService.cs  # TODO: integrate NAudio / FFMpegCore
│   └── DependencyInjection.cs  # AddInfrastructure() extension method
│
├── ViewModels/                 # Presentation — uses dispatchers only
│   ├── ViewModelBase.cs        # Extends ObservableObject (CommunityToolkit)
│   └── MainViewModel.cs        # Injects ICommandDispatcher + IQueryDispatcher
│
├── Views/                      # Avalonia AXAML + code-behind
│   ├── MainWindow.axaml(.cs)
│   └── MainView.axaml(.cs)
│
├── App.axaml.cs               # DI container setup via ConfigureServices()
└── AudioTech.csproj
```

---

## CQRS Conventions

### Commands
- Mutate state. Never return domain objects — return only primitive IDs or `void`.
- Named: `<Verb><Noun>Command` (e.g., `LoadAudioFileCommand`)
- One handler per command: `<CommandName>Handler`
- Folder: `Application/Commands/<FeatureName>/`

### Queries
- Read-only. Return DTOs/records — never domain entities.
- Named: `Get<Noun>Query` (e.g., `GetAudioFilesQuery`)
- Result record in the same folder: `<Feature>Result` or `<Noun>ListItem`
- Folder: `Application/Queries/<FeatureName>/`

### Dispatchers
- ViewModels only interact with `ICommandDispatcher` and `IQueryDispatcher`.
- Never inject repository or service interfaces directly into ViewModels.

---

## DDD Conventions

### Aggregates
- Inherit `AggregateRoot`. All state changes go through public methods on the root.
- Raise `IDomainEvent`s inside aggregate methods via `RaiseDomainEvent()`.
- Domain events are cleared after handling.

### Value Objects
- Inherit `ValueObject`. Implement `GetEqualityComponents()`.
- All validation in the static `Create()` factory — throw `ArgumentException` on invalid input.
- Immutable: no setters, `private` constructors.

### Entities
- Inherit `Entity`. Identity by `Guid Id`.
- No public setters on domain properties; state changes via domain methods only.

---

## DI Registration

All registrations live in `Infrastructure/DependencyInjection.cs` (`AddInfrastructure()`).
ViewModels are registered in `App.axaml.cs`.

Lifetimes:
- **Singleton**: Repositories, Dispatchers, Infrastructure services
- **Transient**: Command handlers, Query handlers, ViewModels

---

## Coding Standards

- **C# 12 / .NET 8** — use primary constructors, collection expressions `[]`, `record` for DTOs/events.
- **Nullable reference types** enabled — always handle nullability explicitly.
- No business logic in ViewModels — delegate to Application layer via dispatchers.
- No domain logic in handlers — delegate to aggregate methods.
- Prefer `sealed` on classes that are not designed for inheritance.
- Use `CancellationToken` on all async methods.

---

## Adding a New Feature Checklist

1. **Domain**: Does it need a new Entity / Value Object / Event? Add to `Domain/`.
2. **Repository**: Does it need persistence? Add method to the interface in `Domain/Repositories/`.
3. **Command**: If it mutates state → add `<Feature>Command` + Handler in `Application/Commands/<Feature>/`.
4. **Query**: If it reads state → add `<Feature>Query` + Result + Handler in `Application/Queries/<Feature>/`.
5. **Infrastructure**: Implement any new repository method or service in `Infrastructure/`.
6. **DI**: Register new handlers/services in `Infrastructure/DependencyInjection.cs`.
7. **ViewModel**: Wire up via dispatcher. Add `[RelayCommand]` method.

---

## TODO / Next Steps

- [ ] Replace `InMemoryAudioFileRepository` with a real persistence layer (SQLite via EF Core or LiteDB)
- [ ] Implement `AudioAnalysisService` using NAudio or FFMpegCore
- [ ] Add domain event dispatching pipeline
- [ ] Add `AnalyseAudioCommand` wiring in ViewModel
- [ ] Build out `MainView.axaml` UI (file picker, audio file list, analysis display)
- [ ] Consider splitting into multiple projects for stricter layer enforcement
