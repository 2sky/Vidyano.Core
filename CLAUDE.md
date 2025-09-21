# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Restore NuGet packages
dotnet restore

# Build all target frameworks (Debug)
dotnet build

# Build with Release configuration (automatically generates NuGet package)
dotnet build --configuration Release

# Clean build artifacts
dotnet clean
```

## Architecture Overview

Vidyano.Core is a portable .NET client library for Vidyano backend applications following MVVM architecture:

### Core Components
- **Client.cs**: Central hub for backend communication and session management. Contains extensive internationalization support and handles all server interactions.
- **NotifyableBase.cs**: Foundation for property change notifications via INotifyPropertyChanged
- **ViewModelBase.cs**: Base class for view models, extending NotifyableBase with additional functionality
- **PersistentObject.cs**: Represents data entities with server synchronization capabilities
- **Query.cs**: Manages data queries and result sets from the backend

### Command System
Actions follow a hierarchical command pattern:
- **ActionBase.cs**: Abstract base for all actions
- **QueryAction.cs**: Query-specific operations
- Specialized actions in `ViewModel/Actions/` handle CRUD operations

### Key Architectural Patterns
1. **Observable Pattern**: All view models inherit from NotifyableBase for data binding
2. **Async-First Design**: Extensive use of async/await throughout Client operations
3. **Strong Typing**: Generic implementations for type safety (e.g., PersistentObject<T>)
4. **Immutable Collections**: Query results use immutable collections for thread safety

## Multi-Target Framework Support

The project targets multiple frameworks - ensure compatibility when adding features:
- .NET Standard 2.0 (for maximum compatibility)
- .NET 8.0 (modern target)

Use conditional compilation when necessary:
```csharp
#if NETSTANDARD2_0
// .NET Standard 2.0 implementation
#else
// .NET 8.0 implementation
#endif
```

## Code Standards

- **Language Version**: C# 13
- **Async/Await**: ConfigureAwait(false) is mandatory (enforced at error level)
- **Code Analysis**: Follows Microsoft.Managed.Recommended.Rules ruleset
- **Naming**: Follow existing patterns - public members use PascalCase, private fields use camelCase

## Version Management

When updating versions:
1. Update `<Version>` in Vidyano.Core.csproj
2. Version format: Major.Minor.Patch (currently 5.51.0)

## Development Notes

### Demo Application
The solution includes a Demo console application that connects to https://demo.vidyano.com. Use this to:
- Test library functionality
- Verify API changes work correctly
- Demonstrate usage patterns to developers

To run the demo:
```bash
cd Demo
dotnet run
```

### No Test Project
This codebase currently lacks automated tests. When implementing new features:
- Ensure backward compatibility across all target frameworks
- Test manually against different .NET runtimes
- Consider impact on existing client applications
- Use the Demo app to verify basic functionality

### Internationalization
Client.cs contains translations for 30+ languages. When modifying error messages or user-facing strings, maintain consistency across all language dictionaries in the Client constructor.

### NuGet Package Generation
The project automatically generates NuGet packages on Release builds. Package metadata is defined in the .csproj file.