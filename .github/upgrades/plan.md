# .NET 10 Upgrade Migration Plan

## Executive Summary

### Scenario
Upgrade the ChefKnifeStudios.PokerAttack solution from .NET 9.0 to .NET 10.0 (Preview).

### Scope
- **Total Projects**: 11 projects across client (Blazor WebAssembly) and server (ASP.NET Core Web API) components
- **Current State**: All projects targeting net9.0
- **Target State**: All projects will target net10.0

### Selected Strategy
**Big Bang Strategy** - All projects will be upgraded simultaneously in a single atomic operation.

**Rationale**: 
- Medium-sized solution (11 projects) with clear dependency structure
- All projects currently on .NET 9.0, making simultaneous upgrade feasible
- Total codebase ~6,500 LOC - manageable for atomic upgrade
- All required packages have compatible .NET 10 versions available
- Clean dependency graph without circular dependencies
- Blazor WebAssembly and ASP.NET Core projects benefit from unified framework upgrade

### Complexity Assessment
**Medium Complexity**

**Justification**:
- 16 NuGet package updates required across the solution
- Blazor WebAssembly project requires careful handling of client-side dependencies
- ASP.NET Core Web API with SignalR and Entity Framework Core
- .NET Aspire AppHost orchestration layer
- Dockerfiles need SDK version updates
- No security vulnerabilities detected (low risk)
- Preview framework version may have breaking changes

### Critical Issues
- ✅ **No security vulnerabilities** detected in current packages
- ⚠️ **.NET 10 is currently in Preview** - expect potential API changes and breaking changes
- ⚠️ **Aspire.Hosting.AppHost** major version jump (9.3.1 → 13.0.0) - review breaking changes
- ⚠️ **OpenTelemetry packages** upgrading to RC versions (1.12.0 → 1.14.0-rc.1)

### Recommended Approach
Big Bang migration with all projects upgraded atomically, followed by comprehensive build verification and testing.

---

## Migration Strategy

### 2.1 Approach Selection

**Chosen Strategy**: Big Bang Strategy

**Justification**:
- **Project Count**: 11 projects is within the optimal range for Big Bang (< 30 projects)
- **Framework Consistency**: All projects currently on net9.0 with no legacy .NET Framework
- **Codebase Size**: Total ~6,500 LOC is small-to-medium, manageable for atomic upgrade
- **Dependency Structure**: Clear, acyclic dependency graph with well-defined layers
- **Package Compatibility**: All 16 required package updates have .NET 10-compatible versions
- **Team Efficiency**: Single coordinated operation minimizes context switching
- **Testing**: Blazor WebAssembly nature allows comprehensive end-to-end testing post-upgrade

### 2.2 Dependency-Based Ordering

While Big Bang upgrades all projects simultaneously, understanding the dependency structure is critical for troubleshooting and validation:

**Dependency Layers** (bottom-up):

**Layer 1 - Foundation Libraries** (0 project dependencies):
- ChefKnifeStudios.PokerAttack.Shared
- ChefKnifeStudios.PokerAttack.Server.Data
- ChefKnifeStudios.PokerAttack.ServiceDefaults

**Layer 2 - Core Business Logic**:
- ChefKnifeStudios.PokerAttack.Server.Core (depends on: Shared)
- ChefKnifeStudios.PokerAttack.Client.Core (depends on: Shared)

**Layer 3 - Presentation & Infrastructure**:
- ChefKnifeStudios.PokerAttack.Server.Infrastructure (depends on: Server.Core)
- ChefKnifeStudios.PokerAttack.Server.BL (depends on: Server.Core, Server.Data)
- ChefKnifeStudios.PokerAttack.Client.Shared (depends on: Client.Core)

**Layer 4 - Application Entry Points**:
- ChefKnifeStudios.PokerAttack.Server.WebAPI (depends on: Server.Infrastructure, ServiceDefaults, Server.BL)
- ChefKnifeStudios.PokerAttack.Client.WebApp (depends on: Client.Shared)

**Layer 5 - Orchestration**:
- ChefKnifeStudios.PokerAttack.AppHost (depends on: Server.WebAPI, Client.WebApp)

**Critical Path**: Shared → Core projects → BL/Infrastructure → WebAPI/WebApp → AppHost

### 2.3 Parallel vs Sequential Execution

**Big Bang Strategy Approach**: All project files and package references are updated in a single coordinated operation. The dependency structure above guides **validation order**, not execution order.

**Validation Strategy**:
- Update all projects simultaneously
- Build in dependency order to isolate errors
- Layer 1 projects must build successfully before validating Layer 2, etc.

---

## Detailed Dependency Analysis

### 3.1 Dependency Graph Summary

**Migration Phases** (for Big Bang, these are validation checkpoints, not execution phases):

**Phase 0: Preparation**
- Verify .NET 10 SDK installation
- Update Dockerfiles to use .NET 10 SDK and runtime images

**Phase 1: Atomic Framework & Package Upgrade**
- Update all 11 project TargetFramework properties to net10.0
- Update all 16 NuGet package references across all projects
- Restore dependencies
- Build solution and fix compilation errors
- Verify: Solution builds with 0 errors

**Phase 2: Docker & Configuration Validation**
- Verify Dockerfiles build successfully with .NET 10 images
- Validate AppHost orchestration configuration
- Verify: Docker images build and run

**Phase 3: Runtime Testing**
- Run Blazor WebAssembly client application
- Test SignalR real-time communication
- Verify Entity Framework Core database operations
- Verify: All core functionality operational

### 3.2 Project Groupings

**Big Bang Atomic Update Group** (all projects updated simultaneously):

**Shared Libraries**:
- ChefKnifeStudios.PokerAttack.Shared (23 files, 280 LOC)

**Client Projects** (Blazor WebAssembly):
- ChefKnifeStudios.PokerAttack.Client.Core (14 files, 1,043 LOC)
- ChefKnifeStudios.PokerAttack.Client.Shared (116 files, 2,347 LOC)
- ChefKnifeStudios.PokerAttack.Client.WebApp (19 files, 291 LOC)

**Server Projects** (ASP.NET Core):
- ChefKnifeStudios.PokerAttack.Server.Core (16 files, 471 LOC)
- ChefKnifeStudios.PokerAttack.Server.Data (23 files, 1,422 LOC)
- ChefKnifeStudios.PokerAttack.Server.BL (8 files, 1,557 LOC)
- ChefKnifeStudios.PokerAttack.Server.Infrastructure (3 files, 196 LOC)
- ChefKnifeStudios.PokerAttack.Server.WebAPI (11 files, 767 LOC)

**Service Infrastructure**:
- ChefKnifeStudios.PokerAttack.ServiceDefaults (1 file, 127 LOC)

**Orchestration**:
- ChefKnifeStudios.PokerAttack.AppHost (1 file, 12 LOC)

**Total**: 235 files, ~6,513 LOC

---

## Project-by-Project Migration Plans

### Project: ChefKnifeStudios.PokerAttack.Shared

**Current State**
- Target Framework: net9.0
- Dependencies: 0 project dependencies
- Dependants: Client.Core, Server.Core
- Package Count: 0
- LOC: 280

**Target State**
- Target Framework: net10.0
- Updated Packages: None

**Migration Steps**

1. **Prerequisites**
   - None (foundation library, no dependencies)

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Shared.csproj` from `net9.0` to `net10.0`

3. **Package Updates**
   - No package updates required

4. **Expected Breaking Changes**
   - Low risk: Library contains only domain models and shared types
   - Potential BCL API changes if using advanced .NET types
   - Monitor for obsolete attribute warnings

5. **Code Modifications**
   - Review any uses of reflection or runtime type inspection
   - Validate serialization/deserialization patterns (JSON, etc.)

6. **Testing Strategy**
   - Build project to ensure no compilation errors
   - Dependent projects (Client.Core, Server.Core) will validate integration

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] No breaking API changes in BCL types used

---

### Project: ChefKnifeStudios.PokerAttack.Server.Data

**Current State**
- Target Framework: net9.0
- Dependencies: 0 project dependencies
- Dependants: Server.BL
- Package Count: 6
- LOC: 1,422

**Target State**
- Target Framework: net10.0
- Updated Packages: 4

**Migration Steps**

1. **Prerequisites**
   - None (foundation data access layer)

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Server.Data.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Microsoft.EntityFrameworkCore | 9.0.8 | 10.0.0 | Framework compatibility |
| Microsoft.EntityFrameworkCore.Design | 9.0.8 | 10.0.0 | Framework compatibility |
| Microsoft.EntityFrameworkCore.Tools | 9.0.8 | 10.0.0 | Framework compatibility |
| Microsoft.Extensions.Configuration.Json | 9.0.8 | 10.0.0 | Framework compatibility |
| Ardalis.Specification.EntityFrameworkCore | 9.3.1 | (no update) | ✅Compatible |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 | (no update) | ✅Compatible |

4. **Expected Breaking Changes**
   - **EF Core 10.0**: Review [Entity Framework Core 10.0 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)
   - Query translation changes
   - Migration tooling updates
   - Provider-specific behavior changes (Npgsql)
   - DbContext configuration changes

5. **Code Modifications**
   - Review DbContext OnConfiguring/OnModelCreating methods
   - Validate migration files still work with EF Core 10.0
   - Check for obsolete EF Core APIs
   - Update Dockerfile EF migration bundle tool to version 10.0.0

6. **Testing Strategy**
   - Build project successfully
   - Regenerate a test migration to ensure tooling works
   - Validate existing migrations are compatible
   - Test database connection and basic CRUD operations

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] EF Core migrations run successfully
   - [ ] DbContext can connect to database
   - [ ] Dockerfile builds with EF tools 10.0.0

---

### Project: ChefKnifeStudios.PokerAttack.ServiceDefaults

**Current State**
- Target Framework: net9.0
- Dependencies: 0 project dependencies
- Dependants: Server.WebAPI
- Package Count: 7
- LOC: 127

**Target State**
- Target Framework: net10.0
- Updated Packages: 4

**Migration Steps**

1. **Prerequisites**
   - None (foundation service defaults)

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.ServiceDefaults.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Microsoft.Extensions.Http.Resilience | 9.4.0 | 10.0.0 | Framework compatibility |
| Microsoft.Extensions.ServiceDiscovery | 9.3.1 | 10.0.0 | Framework compatibility |
| OpenTelemetry.Instrumentation.AspNetCore | 1.12.0 | 1.14.0-rc.1 | .NET 10 compatibility |
| OpenTelemetry.Instrumentation.Http | 1.12.0 | 1.14.0-rc.1 | .NET 10 compatibility |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.12.0 | (no update) | ✅Compatible |
| OpenTelemetry.Extensions.Hosting | 1.12.0 | (no update) | ✅Compatible |
| OpenTelemetry.Instrumentation.Runtime | 1.12.0 | (no update) | ✅Compatible |

4. **Expected Breaking Changes**
   - **OpenTelemetry RC packages**: API changes possible in instrumentation setup
   - Service Discovery configuration changes
   - Resilience handler policy updates
   - Review Extensions.cs for obsolete extension methods

5. **Code Modifications**
   - Review `Extensions.cs` ConfigureOpenTelemetry method
   - Validate AddStandardResilienceHandler configuration
   - Check AddServiceDiscovery setup
   - Update health check endpoint configuration if needed

6. **Testing Strategy**
   - Build project successfully
   - Validate extension methods compile correctly
   - Integration test with Server.WebAPI to ensure services register properly

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] Extension methods compatible with .NET 10
   - [ ] OpenTelemetry instrumentation initializes correctly

---

### Project: ChefKnifeStudios.PokerAttack.Server.Core

**Current State**
- Target Framework: net9.0
- Dependencies: Shared
- Dependants: Server.BL, Server.Infrastructure
- Package Count: 0
- LOC: 471

**Target State**
- Target Framework: net10.0
- Updated Packages: None

**Migration Steps**

1. **Prerequisites**
   - Shared project must build successfully

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Server.Core.csproj` from `net9.0` to `net10.0`

3. **Package Updates**
   - No package updates required

4. **Expected Breaking Changes**
   - Low risk: core domain logic and interfaces
   - Potential BCL changes if using System.* namespaces

5. **Code Modifications**
   - Review repository interfaces and domain services
   - Validate any async/await patterns for .NET 10 improvements

6. **Testing Strategy**
   - Build project successfully
   - Validate dependent projects (Server.BL, Server.Infrastructure) compile

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] Dependent projects reference correctly

---

### Project: ChefKnifeStudios.PokerAttack.Client.Core

**Current State**
- Target Framework: net9.0
- Dependencies: Shared
- Dependants: Client.Shared
- Package Count: 5
- LOC: 1,043

**Target State**
- Target Framework: net10.0
- Updated Packages: 3

**Migration Steps**

1. **Prerequisites**
   - Shared project must build successfully

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Client.Core.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Microsoft.AspNetCore.Components.WebAssembly | 9.0.8 | 10.0.0 | Framework compatibility |
| Microsoft.AspNetCore.Components.WebAssembly.Authentication | 9.0.8 | 10.0.0 | Framework compatibility |
| Microsoft.AspNetCore.SignalR.Client | 9.0.8 | 10.0.0 | Framework compatibility |
| Ardalis.Result | 10.1.0 | (no update) | ✅Compatible |
| AutoFixture | 4.18.1 | (no update) | ✅Compatible |

4. **Expected Breaking Changes**
   - **Blazor WebAssembly 10.0**: Component lifecycle changes, rendering behavior
   - **SignalR Client 10.0**: Connection API updates, automatic reconnection changes
   - Authentication flow updates for WebAssembly

5. **Code Modifications**
   - Review SignalR HubConnection setup and lifetime management
   - Validate WebAssembly authentication state provider
   - Check for obsolete Blazor component APIs
   - Review service endpoint services for HTTP client changes

6. **Testing Strategy**
   - Build project successfully
   - Integration test SignalR connection establishment
   - Validate authentication flows

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] SignalR client initializes correctly
   - [ ] Authentication services register properly

---

### Project: ChefKnifeStudios.PokerAttack.Server.BL

**Current State**
- Target Framework: net9.0
- Dependencies: Server.Core, Server.Data
- Dependants: Server.WebAPI
- Package Count: 1
- LOC: 1,557

**Target State**
- Target Framework: net10.0
- Updated Packages: 1

**Migration Steps**

1. **Prerequisites**
   - Server.Core must build successfully
   - Server.Data must build successfully

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Server.BL.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Microsoft.Extensions.Hosting | 9.0.10 | 10.0.0 | Framework compatibility |

4. **Expected Breaking Changes**
   - Hosted service lifecycle changes
   - Dependency injection container behavior updates

5. **Code Modifications**
   - Review business logic services and game state machines
   - Validate any background service implementations (IHostedService)
   - Check for DI registration changes

6. **Testing Strategy**
   - Build project successfully
   - Validate business logic compiles
   - Integration test with Server.WebAPI

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] Hosted services compatible with .NET 10

---

### Project: ChefKnifeStudios.PokerAttack.Server.Infrastructure

**Current State**
- Target Framework: net9.0
- Dependencies: Server.Core
- Dependants: Server.WebAPI
- Package Count: 1
- LOC: 196

**Target State**
- Target Framework: net10.0
- Updated Packages: None

**Migration Steps**

1. **Prerequisites**
   - Server.Core must build successfully

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Server.Infrastructure.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| StackExchange.Redis | 2.9.17 | (no update) | ✅Compatible |

4. **Expected Breaking Changes**
   - Low risk: Redis client is compatible

5. **Code Modifications**
   - Review Redis connection setup
   - Validate caching implementations

6. **Testing Strategy**
   - Build project successfully
   - Validate Redis connectivity

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] Redis client operations functional

---

### Project: ChefKnifeStudios.PokerAttack.Client.Shared

**Current State**
- Target Framework: net9.0
- Dependencies: Client.Core
- Dependants: Client.WebApp
- Package Count: 4
- LOC: 2,347

**Target State**
- Target Framework: net10.0
- Updated Packages: 1

**Migration Steps**

1. **Prerequisites**
   - Client.Core must build successfully

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Client.Shared.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Microsoft.AspNetCore.Components.Web | 9.0.8 | 10.0.0 | Framework compatibility |
| CommunityToolkit.Mvvm | 8.4.0 | (no update) | ✅Compatible |
| MatBlazor | 2.10.0 | (no update) | ✅Compatible |
| NameGenerator | 2.0.4 | (no update) | ✅Compatible |

4. **Expected Breaking Changes**
   - **Blazor Components.Web 10.0**: Event handling, JavaScript interop changes
   - Component rendering behavior updates
   - JS interop patterns (review CommonJsInterop.cs)

5. **Code Modifications**
   - Review all .razor components for lifecycle method changes
   - Validate JavaScript interop calls in CommonJsInterop.cs, InputJsInterop.cs, LobbyJsInterop.cs
   - Check ViewModels for Blazor-specific patterns
   - Review MatBlazor component usage for compatibility

6. **Testing Strategy**
   - Build project successfully
   - Visual testing of Blazor components
   - Test JavaScript interop functionality

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] JavaScript interop works correctly
   - [ ] Razor components render properly

---

### Project: ChefKnifeStudios.PokerAttack.Server.WebAPI

**Current State**
- Target Framework: net9.0
- Dependencies: Server.Infrastructure, ServiceDefaults, Server.BL
- Dependants: AppHost
- Package Count: 4
- LOC: 767

**Target State**
- Target Framework: net10.0
- Updated Packages: 2

**Migration Steps**

1. **Prerequisites**
   - Server.Infrastructure must build successfully
   - ServiceDefaults must build successfully
   - Server.BL must build successfully

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Server.WebAPI.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Microsoft.AspNetCore.OpenApi | 9.0.2 | 10.0.0 | Framework compatibility |
| Microsoft.EntityFrameworkCore.Design | 9.0.9 | 10.0.0 | Framework compatibility |
| Ardalis.Result | 10.1.0 | (no update) | ✅Compatible |
| Scalar.AspNetCore | 2.6.9 | (no update) | ✅Compatible |

4. **Expected Breaking Changes**
   - **ASP.NET Core 10.0**: Minimal API changes, middleware pipeline updates
   - **OpenAPI**: Schema generation changes
   - SignalR hub configuration updates
   - CORS policy changes

5. **Code Modifications**
   - Review Program.cs for ASP.NET Core 10.0 hosting model changes
   - Validate SignalR hub endpoints and configuration
   - Check OpenAPI/Swagger setup
   - Review middleware ordering (CORS, authentication, authorization)
   - Validate ServiceDefaults integration (health checks, telemetry)
   - Update Dockerfile to use mcr.microsoft.com/dotnet/sdk:10.0 and aspnet:10.0

6. **Testing Strategy**
   - Build project successfully
   - Run application and verify startup
   - Test API endpoints via Scalar UI
   - Validate SignalR connections
   - Test health check endpoints

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] Application starts successfully
   - [ ] API endpoints respond correctly
   - [ ] SignalR hubs accept connections
   - [ ] OpenAPI documentation generates
   - [ ] Dockerfile builds and runs

---

### Project: ChefKnifeStudios.PokerAttack.Client.WebApp

**Current State**
- Target Framework: net9.0
- Dependencies: Client.Shared
- Dependants: AppHost
- Package Count: 3
- LOC: 291

**Target State**
- Target Framework: net10.0
- Updated Packages: 3

**Migration Steps**

1. **Prerequisites**
   - Client.Shared must build successfully

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.Client.WebApp.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Microsoft.AspNetCore.Components.WebAssembly | 9.0.8 | 10.0.0 | Framework compatibility |
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 9.0.8 | 10.0.0 | Framework compatibility |
| Microsoft.Extensions.Http | 9.0.4 | 10.0.0 | Framework compatibility |

4. **Expected Breaking Changes**
   - **Blazor WebAssembly 10.0**: App startup, hosting model changes
   - HttpClient factory changes
   - Static asset handling updates

5. **Code Modifications**
   - Review Program.cs for WebAssembly host builder changes
   - Validate HttpClient configuration and named clients
   - Check App.razor routing setup
   - Review wwwroot/index.html for Blazor script changes

6. **Testing Strategy**
   - Build project successfully
   - Run application in browser
   - Test client-side routing
   - Validate HTTP communication with Server.WebAPI
   - Test SignalR real-time updates

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] Application loads in browser
   - [ ] Routing works correctly
   - [ ] API calls succeed
   - [ ] SignalR real-time communication works

---

### Project: ChefKnifeStudios.PokerAttack.AppHost

**Current State**
- Target Framework: net9.0
- Dependencies: Server.WebAPI, Client.WebApp
- Dependants: 0 (top-level orchestrator)
- Package Count: 1
- LOC: 12

**Target State**
- Target Framework: net10.0
- Updated Packages: 1

**Migration Steps**

1. **Prerequisites**
   - Server.WebAPI must build successfully
   - Client.WebApp must build successfully

2. **Framework Update**
   - Update `TargetFramework` in `ChefKnifeStudios.PokerAttack.AppHost.csproj` from `net9.0` to `net10.0`

3. **Package Updates**

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Aspire.Hosting.AppHost | 9.3.1 | 13.0.0 | ⚠️ MAJOR VERSION JUMP - Framework compatibility |

4. **Expected Breaking Changes**
   - **Aspire 13.0 MAJOR UPDATE**: Review [.NET Aspire breaking changes](https://learn.microsoft.com/en-us/dotnet/aspire/whats-new/)
   - AppHost builder API changes
   - Resource configuration changes
   - Service discovery integration updates
   - Dashboard updates

5. **Code Modifications**
   - Review Program.cs AppHost builder setup
   - Validate project resource AddProject calls
   - Check service orchestration and dependencies
   - Review environment variable passing
   - Update any Aspire-specific configurations

6. **Testing Strategy**
   - Build project successfully
   - Run AppHost and verify dashboard launches
   - Validate all services start correctly
   - Test service-to-service communication
   - Verify orchestration and health monitoring

7. **Validation Checklist**
   - [ ] Project builds without errors
   - [ ] Project builds without warnings
   - [ ] AppHost starts and launches dashboard
   - [ ] All services orchestrate correctly
   - [ ] Service discovery works
   - [ ] Health monitoring operational

---

## Package Update Reference

### Common Package Updates (affecting multiple projects)

| Package | Current | Target | Projects Affected | Update Reason |
|---------|---------|--------|-------------------|---------------|
| Microsoft.AspNetCore.Components.WebAssembly | 9.0.8 | 10.0.0 | 2 projects (Client.Core, Client.WebApp) | Framework compatibility |
| Microsoft.EntityFrameworkCore.Design | 9.0.8/9.0.9 | 10.0.0 | 2 projects (Server.Data, Server.WebAPI) | Framework compatibility |

### Category-Specific Updates

**Blazor WebAssembly Projects** (3 projects):
- Microsoft.AspNetCore.Components.Web: 9.0.8 → 10.0.0
- Microsoft.AspNetCore.Components.WebAssembly.Authentication: 9.0.8 → 10.0.0
- Microsoft.AspNetCore.Components.WebAssembly.DevServer: 9.0.8 → 10.0.0
- Microsoft.Extensions.Http: 9.0.4 → 10.0.0
- Microsoft.AspNetCore.SignalR.Client: 9.0.8 → 10.0.0

**Entity Framework Core Projects** (1 project):
- Microsoft.EntityFrameworkCore: 9.0.8 → 10.0.0
- Microsoft.EntityFrameworkCore.Design: 9.0.8 → 10.0.0
- Microsoft.EntityFrameworkCore.Tools: 9.0.8 → 10.0.0
- Microsoft.Extensions.Configuration.Json: 9.0.8 → 10.0.0

**Service Infrastructure Projects** (1 project):
- Microsoft.Extensions.Http.Resilience: 9.4.0 → 10.0.0
- Microsoft.Extensions.ServiceDiscovery: 9.3.1 → 10.0.0
- OpenTelemetry.Instrumentation.AspNetCore: 1.12.0 → 1.14.0-rc.1
- OpenTelemetry.Instrumentation.Http: 1.12.0 → 1.14.0-rc.1

**ASP.NET Core Web API Projects** (2 projects):
- Microsoft.AspNetCore.OpenApi: 9.0.2 → 10.0.0
- Microsoft.Extensions.Hosting: 9.0.10 → 10.0.0

### Project-Specific Major Updates

**AppHost** (⚠️ CRITICAL):
- Aspire.Hosting.AppHost: 9.3.1 → 13.0.0 (MAJOR VERSION JUMP)

---

## Breaking Changes Catalog

### .NET 10 Framework Breaking Changes

Review the official Microsoft documentation:
- [.NET 10 Breaking Changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0)
- [ASP.NET Core 10.0 Breaking Changes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [Entity Framework Core 10.0 Breaking Changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)

**Common Areas to Monitor**:

1. **BCL (Base Class Library)**
   - Obsolete API removals
   - Behavior changes in System.* namespaces
   - Serialization changes (System.Text.Json)

2. **ASP.NET Core**
   - Minimal API routing changes
   - Middleware pipeline modifications
   - Authentication/authorization updates
   - SignalR protocol changes
   - Blazor component lifecycle updates

3. **Entity Framework Core**
   - Query translation changes
   - Migration tooling updates
   - Provider behavior (Npgsql for PostgreSQL)
   - DbContext configuration

4. **Blazor WebAssembly**
   - Component rendering optimization
   - JavaScript interop API changes
   - Static asset handling
   - Hot reload improvements

### Package-Specific Breaking Changes

**Aspire.Hosting.AppHost (9.3.1 → 13.0.0)**
- ⚠️ **MAJOR VERSION JUMP** - expect significant API changes
- AppHost builder pattern changes
- Resource configuration API updates
- Dashboard and orchestration changes
- Review: https://learn.microsoft.com/en-us/dotnet/aspire/whats-new/

**OpenTelemetry Packages (1.12.0 → 1.14.0-rc.1)**
- ⚠️ **RC version** - API may still change
- Instrumentation setup changes
- Exporter configuration updates

**Blazor Components (9.0.8 → 10.0.0)**
- Component lifecycle method signatures
- Event handling patterns
- JavaScript interop promise handling
- Form validation updates

**SignalR Client (9.0.8 → 10.0.0)**
- HubConnection builder API
- Automatic reconnection logic
- Message serialization

**Entity Framework Core (9.0.8 → 10.0.0)**
- LINQ query provider updates
- Migration generation changes
- DbContext pooling behavior

---

## Detailed Execution Steps

### Step 1: Verify Prerequisites

**Check .NET 10 SDK Installation**:
- Verify .NET 10 SDK is installed: `dotnet --list-sdks`
- Expected: `10.0.xxx` or higher
- If not installed, download from: https://dotnet.microsoft.com/download/dotnet/10.0

**Check Docker Installation** (for Dockerfile updates):
- Verify Docker is available: `docker --version`

### Step 2: Update Project Files (All 11 Projects Simultaneously)

Update `TargetFramework` property in all project files from `net9.0` to `net10.0`:

**Projects to update**:
1. ChefKnifeStudios.PokerAttack.Shared\ChefKnifeStudios.PokerAttack.Shared.csproj
2. Server\ChefKnifeStudios.PokerAttack.Server.Data\ChefKnifeStudios.PokerAttack.Server.Data.csproj
3. ChefKnifeStudios.PokerAttack.ServiceDefaults\ChefKnifeStudios.PokerAttack.ServiceDefaults.csproj
4. Server\ChefKnifeStudios.PokerAttack.Server.Core\ChefKnifeStudios.PokerAttack.Server.Core.csproj
5. Client\ChefKnifeStudios.PokerAttack.Client.Core\ChefKnifeStudios.PokerAttack.Client.Core.csproj
6. Server\ChefKnifeStudios.PokerAttack.Server.BL\ChefKnifeStudios.PokerAttack.Server.BL.csproj
7. Server\ChefKnifeStudios.PokerAttack.Server.Infrastructure\ChefKnifeStudios.PokerAttack.Server.Infrastructure.csproj
8. Client\ChefKnifeStudios.PokerAttack.Client.Shared\ChefKnifeStudios.PokerAttack.Client.Shared.csproj
9. Server\ChefKnifeStudios.PokerAttack.Server.WebAPI\ChefKnifeStudios.PokerAttack.Server.WebAPI.csproj
10. Client\ChefKnifeStudios.PokerAttack.Client.WebApp\ChefKnifeStudios.PokerAttack.Client.WebApp.csproj
11. ChefKnifeStudios.PokerAttack.AppHost\ChefKnifeStudios.PokerAttack.AppHost.csproj

**Change**:
```xml
<TargetFramework>net9.0</TargetFramework>
```
**To**:
```xml
<TargetFramework>net10.0</TargetFramework>
```

### Step 3: Update Package References (All Projects Simultaneously)

Update all package references across projects. See [Package Update Reference](#package-update-reference) for complete matrix.

**Critical Package Updates**:

**Server.Data**:
- Microsoft.EntityFrameworkCore: 9.0.8 → 10.0.0
- Microsoft.EntityFrameworkCore.Design: 9.0.8 → 10.0.0
- Microsoft.EntityFrameworkCore.Tools: 9.0.8 → 10.0.0
- Microsoft.Extensions.Configuration.Json: 9.0.8 → 10.0.0

**ServiceDefaults**:
- Microsoft.Extensions.Http.Resilience: 9.4.0 → 10.0.0
- Microsoft.Extensions.ServiceDiscovery: 9.3.1 → 10.0.0
- OpenTelemetry.Instrumentation.AspNetCore: 1.12.0 → 1.14.0-rc.1
- OpenTelemetry.Instrumentation.Http: 1.12.0 → 1.14.0-rc.1

**Client.Core**:
- Microsoft.AspNetCore.Components.WebAssembly: 9.0.8 → 10.0.0
- Microsoft.AspNetCore.Components.WebAssembly.Authentication: 9.0.8 → 10.0.0
- Microsoft.AspNetCore.SignalR.Client: 9.0.8 → 10.0.0

**Client.Shared**:
- Microsoft.AspNetCore.Components.Web: 9.0.8 → 10.0.0

**Server.BL**:
- Microsoft.Extensions.Hosting: 9.0.10 → 10.0.0

**Server.WebAPI**:
- Microsoft.AspNetCore.OpenApi: 9.0.2 → 10.0.0
- Microsoft.EntityFrameworkCore.Design: 9.0.9 → 10.0.0

**Client.WebApp**:
- Microsoft.AspNetCore.Components.WebAssembly: 9.0.8 → 10.0.0
- Microsoft.AspNetCore.Components.WebAssembly.DevServer: 9.0.8 → 10.0.0
- Microsoft.Extensions.Http: 9.0.4 → 10.0.0

**AppHost** (⚠️ MAJOR UPDATE):
- Aspire.Hosting.AppHost: 9.3.1 → 13.0.0

### Step 4: Update Dockerfiles

**Server.Data Dockerfile**:
Update `Server\ChefKnifeStudios.PokerAttack.Server.Data\Dockerfile`:
- Change `FROM mcr.microsoft.com/dotnet/sdk:9.0` → `FROM mcr.microsoft.com/dotnet/sdk:10.0`
- Change `FROM mcr.microsoft.com/dotnet/runtime:9.0` → `FROM mcr.microsoft.com/dotnet/runtime:10.0`
- Update EF tools version: `dotnet tool install dotnet-ef -g --version 10.0.0`

**Server.WebAPI Dockerfile**:
Update `Server\ChefKnifeStudios.PokerAttack.Server.WebAPI\Dockerfile`:
- Change `FROM mcr.microsoft.com/dotnet/sdk:9.0` → `FROM mcr.microsoft.com/dotnet/sdk:10.0`
- Change `FROM mcr.microsoft.com/dotnet/aspnet:9.0` → `FROM mcr.microsoft.com/dotnet/aspnet:10.0`

### Step 5: Restore Dependencies and Build Solution

**Restore NuGet packages**:
```bash
cd C:\Projects\ChefKnifeStudios.PokerAttack\src
dotnet restore ChefKnifeStudios.PokerAttack.sln
```

**Build entire solution**:
```bash
dotnet build ChefKnifeStudios.PokerAttack.sln --configuration Release
```

**Expected outcome**: Solution builds successfully with 0 errors.

### Step 6: Fix Compilation Errors

**Build in dependency order to isolate issues**:

1. Build foundation layer:
   ```bash
   dotnet build ChefKnifeStudios.PokerAttack.Shared\ChefKnifeStudios.PokerAttack.Shared.csproj
   dotnet build Server\ChefKnifeStudios.PokerAttack.Server.Data\ChefKnifeStudios.PokerAttack.Server.Data.csproj
   dotnet build ChefKnifeStudios.PokerAttack.ServiceDefaults\ChefKnifeStudios.PokerAttack.ServiceDefaults.csproj
   ```

2. Build core layer:
   ```bash
   dotnet build Server\ChefKnifeStudios.PokerAttack.Server.Core\ChefKnifeStudios.PokerAttack.Server.Core.csproj
   dotnet build Client\ChefKnifeStudios.PokerAttack.Client.Core\ChefKnifeStudios.PokerAttack.Client.Core.csproj
   ```

3. Build business/presentation layer:
   ```bash
   dotnet build Server\ChefKnifeStudios.PokerAttack.Server.Infrastructure\ChefKnifeStudios.PokerAttack.Server.Infrastructure.csproj
   dotnet build Server\ChefKnifeStudios.PokerAttack.Server.BL\ChefKnifeStudios.PokerAttack.Server.BL.csproj
   dotnet build Client\ChefKnifeStudios.PokerAttack.Client.Shared\ChefKnifeStudios.PokerAttack.Client.Shared.csproj
   ```

4. Build application layer:
   ```bash
   dotnet build Server\ChefKnifeStudios.PokerAttack.Server.WebAPI\ChefKnifeStudios.PokerAttack.Server.WebAPI.csproj
   dotnet build Client\ChefKnifeStudios.PokerAttack.Client.WebApp\ChefKnifeStudios.PokerAttack.Client.WebApp.csproj
   ```

5. Build orchestration:
   ```bash
   dotnet build ChefKnifeStudios.PokerAttack.AppHost\ChefKnifeStudios.PokerAttack.AppHost.csproj
   ```

**Common Breaking Changes to Address**:

- **Obsolete API warnings**: Replace obsolete methods/properties with recommended alternatives
- **Aspire AppHost API changes**: Update builder calls based on Aspire 13.0 documentation
- **EF Core query changes**: Review LINQ queries for translation issues
- **Blazor lifecycle changes**: Update component OnInitialized/OnParametersSet overrides
- **SignalR connection setup**: Validate HubConnectionBuilder API calls
- **OpenTelemetry setup**: Update instrumentation configuration based on RC API

Refer to [Breaking Changes Catalog](#breaking-changes-catalog) for detailed guidance.

### Step 7: Verify Docker Builds

**Build Server.Data Docker image**:
```bash
cd C:\Projects\ChefKnifeStudios.PokerAttack
docker build -f src/Server/ChefKnifeStudios.PokerAttack.Server.Data/Dockerfile -t poker-attack-data-image .
```

**Build Server.WebAPI Docker image**:
```bash
docker build -f src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Dockerfile -t poker-attack-api-image .
```

**Expected outcome**: Both images build successfully.

### Step 8: Runtime Validation

**Run AppHost locally**:
```bash
cd C:\Projects\ChefKnifeStudios.PokerAttack\src\ChefKnifeStudios.PokerAttack.AppHost
dotnet run
```

**Validation**:
- Aspire dashboard launches
- Server.WebAPI starts and shows healthy
- Client.WebApp starts and is accessible
- Services can communicate

**Manual Testing**:
- Open browser to Client.WebApp URL
- Test core gameplay functionality
- Verify SignalR real-time updates work
- Test API endpoints via Scalar UI

---

## Risk Management

### High-Risk Changes

| Project | Risk | Mitigation |
|---------|------|------------|
| AppHost | ⚠️ **Aspire 9.3.1 → 13.0.0 major version jump** | Review Aspire 13.0 migration guide thoroughly; test orchestration extensively; have rollback plan |
| ServiceDefaults | ⚠️ **OpenTelemetry RC packages** | API may change before stable release; monitor for updates; be prepared to adjust instrumentation |
| Client.Shared | ⚠️ **Complex JavaScript interop** (2,347 LOC, 116 files) | Comprehensive testing of JS interop; validate all Blazor components visually |
| Server.WebAPI | ⚠️ **SignalR + EF Core + OpenAPI** integration | Test end-to-end real-time communication; validate database operations; check API documentation |
| Server.Data | ⚠️ **EF Core 10.0 migration tooling** | Test migration generation; validate existing migrations; backup database before applying |

### Risk Mitigation Strategies

**For Aspire Major Update**:
1. Review official migration guide: https://learn.microsoft.com/en-us/dotnet/aspire/whats-new/
2. Test AppHost in isolation before full solution test
3. Validate service discovery and orchestration thoroughly
4. Keep Aspire 9.3.1 documentation accessible for comparison

**For Preview Framework Version**:
1. Monitor .NET 10 preview release notes for breaking changes
2. Be prepared for API changes in future previews
3. Document any workarounds for preview-specific issues
4. Plan for re-testing with .NET 10 RC and RTM releases

**For Blazor WebAssembly**:
1. Test in multiple browsers (Chrome, Edge, Firefox)
2. Validate both development and production builds
3. Test JavaScript interop extensively
4. Monitor browser console for errors

**For Entity Framework Core**:
1. Backup database before running migrations
2. Test migrations on non-production database first
3. Validate query performance hasn't degraded

### Rollback Procedures

**Git Rollback**:
```bash
# Return to pre-upgrade state
git checkout main
git branch -D upgrade-to-NET10
```

**Partial Rollback** (if needed):
- Revert specific project files via Git
- Restore package versions in affected .csproj files
- Run `dotnet restore` and `dotnet build`

**Docker Rollback**:
- Rebuild images with .NET 9 SDK/runtime if needed
- Use previous working image tags

---

## Testing Strategy

### Build Validation

**Success Criteria**:
- All 11 projects build without errors
- All 11 projects build without warnings
- Solution restore completes successfully
- Docker images build successfully

### Project-Level Testing

**For Each Project**:
1. Verify project builds in isolation
2. Check for obsolete API warnings
3. Validate dependencies resolve correctly

### Integration Testing

**Server-Side**:
- ASP.NET Core Web API starts successfully
- Health check endpoints respond
- OpenAPI/Scalar documentation generates
- SignalR hubs accept connections
- Entity Framework Core connects to database
- Redis caching functional

**Client-Side**:
- Blazor WebAssembly application loads in browser
- Routing works correctly
- API calls to Server.WebAPI succeed
- SignalR real-time updates work
- JavaScript interop functions correctly

**Orchestration**:
- AppHost launches Aspire dashboard
- All services start and show healthy status
- Service-to-service communication works
- Telemetry and logging operational

### End-to-End Testing

**Core Workflows**:
1. User can access the web application
2. Lobby functionality works (create/join games)
3. Gameplay state machine functions correctly
4. Real-time card updates via SignalR
5. Player power mechanics work
6. Scoreboard updates correctly

### Performance Testing

**Benchmarks**:
- Application startup time (compare to .NET 9 baseline)
- API response times
- SignalR message latency
- Blazor rendering performance
- Entity Framework query performance

---

## Source Control Strategy

### Branching Strategy

**Upgrade Branch**: `upgrade-to-NET10` (already created from `main`)

**Workflow**:
1. All upgrade work happens on `upgrade-to-NET10` branch
2. Commit atomic upgrade changes
3. Test thoroughly before merging to `main`

### Commit Strategy

**Big Bang Approach - Single Atomic Commit** (Recommended):

After all project files, packages, and Dockerfiles are updated and solution builds successfully:

```bash
git add .
git commit -m "Upgrade solution from .NET 9 to .NET 10

- Update all 11 projects from net9.0 to net10.0
- Update 16 NuGet packages to .NET 10-compatible versions
- Update Dockerfiles to use .NET 10 SDK and runtime images
- Update Aspire.Hosting.AppHost 9.3.1 -> 13.0.0
- Update EF Core packages to 10.0.0
- Update Blazor WebAssembly packages to 10.0.0
- Update OpenTelemetry packages to 1.14.0-rc.1
- Solution builds successfully with 0 errors

Breaking changes addressed:
- [List any breaking changes fixed]

Tested:
- All projects build successfully
- Docker images build successfully
- AppHost orchestration functional
- Core application workflows validated"
```

**Alternative: Multiple Commits** (if iterative fixes needed):

1. **Initial Framework Update**:
   ```bash
   git add *.csproj
   git commit -m "Update all projects to net10.0 target framework"
   ```

2. **Package Updates**:
   ```bash
   git add *.csproj
   git commit -m "Update NuGet packages for .NET 10 compatibility"
   ```

3. **Docker Updates**:
   ```bash
   git add **/Dockerfile
   git commit -m "Update Dockerfiles to .NET 10 SDK and runtime images"
   ```

4. **Breaking Change Fixes** (as needed):
   ```bash
   git add <affected files>
   git commit -m "Fix breaking changes: [specific area]

   - [Detailed description of fixes]"
   ```

5. **Final Validation**:
   ```bash
   git commit -m "Final validation and cleanup for .NET 10 upgrade"
   ```

### Review and Merge Process

**Pre-Merge Checklist**:
- [ ] All projects build successfully
- [ ] All tests pass (if applicable)
- [ ] Docker images build and run
- [ ] AppHost orchestration works
- [ ] Core functionality validated
- [ ] No unresolved merge conflicts
- [ ] Breaking changes documented
- [ ] Commit messages are clear and descriptive

**Merge to Main**:
```bash
git checkout main
git merge upgrade-to-NET10 --no-ff
git push origin main
```

**Post-Merge**:
- Tag the release: `git tag -a v10.0-upgrade -m ".NET 10 upgrade"`
- Push tags: `git push origin --tags`
- Close the upgrade branch (optional): `git branch -d upgrade-to-NET10`

---

## Success Criteria

### Technical Success Criteria

- [x] All 11 projects migrated to net10.0 target framework
- [x] All 16 NuGet packages updated to .NET 10-compatible versions
- [x] Zero security vulnerabilities in dependencies
- [x] All projects build without errors (0 errors)
- [x] All projects build without warnings (0 warnings)
- [x] Docker images build successfully with .NET 10 SDK/runtime
- [x] Solution passes `dotnet restore` and `dotnet build` cleanly

### Quality Criteria

- [x] Code quality maintained (no degradation)
- [x] No regressions in core functionality
- [x] Application performance within acceptable thresholds (baseline: .NET 9)
- [x] Breaking changes documented and addressed
- [x] Commit history is clear and meaningful

### Functional Criteria

- [x] AppHost launches and orchestrates services correctly
- [x] Aspire dashboard operational
- [x] Server.WebAPI starts and responds to requests
- [x] Client.WebApp loads in browser
- [x] SignalR real-time communication functional
- [x] Entity Framework Core database operations work
- [x] OpenAPI documentation generates correctly
- [x] Health check endpoints respond
- [x] Telemetry and logging operational

### Big Bang Strategy Success

- [x] All projects updated simultaneously in atomic operation
- [x] Single coordinated package upgrade completed
- [x] Solution builds after atomic upgrade with no intermediate states
- [x] End-to-end testing validates entire solution post-upgrade

---

## Timeline and Effort Estimates

### Per-Project Complexity

| Project | Complexity | Estimated Time | Dependencies | Risk Level |
|---------|------------|---------------|--------------|------------|
| Shared | Low | 15 min | None | Low |
| Server.Data | Medium | 45 min | None | Medium |
| ServiceDefaults | Medium | 30 min | None | Medium |
| Server.Core | Low | 15 min | Shared | Low |
| Client.Core | Medium | 30 min | Shared | Medium |
| Server.Infrastructure | Low | 15 min | Server.Core | Low |
| Server.BL | Low | 20 min | Server.Core, Server.Data | Low |
| Client.Shared | High | 60 min | Client.Core | High |
| Server.WebAPI | High | 60 min | Server.Infrastructure, ServiceDefaults, Server.BL | High |
| Client.WebApp | Medium | 45 min | Client.Shared | Medium |
| AppHost | High | 60 min | Server.WebAPI, Client.WebApp | High |

### Phase Durations

**Phase 0: Preparation** - 30 minutes
- Verify .NET 10 SDK installation
- Review Aspire 13.0 breaking changes documentation
- Backup current state

**Phase 1: Atomic Upgrade** - 2-3 hours
- Update all project files (30 min)
- Update all package references (30 min)
- Update Dockerfiles (15 min)
- Restore dependencies (10 min)
- Build solution and fix compilation errors (60-90 min)
- Rebuild and verify (15 min)

**Phase 2: Docker & Configuration Validation** - 45 minutes
- Build Docker images (20 min)
- Test AppHost orchestration (25 min)

**Phase 3: Runtime Testing** - 1-2 hours
- End-to-end functionality testing (60-90 min)
- Performance validation (30 min)

**Total Estimated Time**: 4.5-6 hours

**Buffer Time**: +2 hours for unexpected breaking changes

**Total with Buffer**: 6.5-8 hours

### Resource Requirements

**Developer Skills Needed**:
- Strong .NET Core/ASP.NET Core experience
- Blazor WebAssembly knowledge
- Entity Framework Core proficiency
- Docker containerization experience
- .NET Aspire orchestration understanding
- Git version control

**Tools Required**:
- Visual Studio 2022 17.13+ or Rider 2024.3+
- .NET 10 SDK
- Docker Desktop
- Git client
- PostgreSQL (for database testing)
- Modern web browser (Chrome/Edge/Firefox)

---

## Additional Considerations

### .NET 10 Preview Notes

**Important**: .NET 10 is currently in **Preview** status.

**Implications**:
- API surface may change between preview releases
- Some packages may have limited preview/RC versions
- Production deployment should wait for .NET 10 RTM (November 2025)
- Monitor .NET blog for breaking change announcements: https://devblogs.microsoft.com/dotnet/

**Recommended Actions**:
- Subscribe to .NET announcements
- Re-test with each new preview release
- Document any preview-specific workarounds
- Plan for final validation with .NET 10 RC

### Aspire 13.0 Migration

**Critical**: Aspire.Hosting.AppHost is jumping from 9.3.1 to 13.0.0 (major version change).

**Required Reading**:
- .NET Aspire 13.0 What's New: https://learn.microsoft.com/en-us/dotnet/aspire/whats-new/
- Breaking changes and migration guide

**Key Areas to Review**:
- AppHost builder API changes
- Service discovery configuration
- Dashboard updates
- Resource management changes

### Blazor WebAssembly Considerations

**Client-Side Specifics**:
- Blazor WASM runs entirely in browser
- JavaScript interop is critical integration point
- Static asset handling may change
- Bundle size and load time should be monitored

**Testing Focus**:
- Browser compatibility (Chrome, Edge, Firefox, Safari)
- JavaScript interop calls (CommonJsInterop.cs, InputJsInterop.cs, LobbyJsInterop.cs)
- Network request handling
- Client-side routing

### Entity Framework Core 10.0

**Database Considerations**:
- PostgreSQL provider (Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4) is compatible
- Migration tooling updated to 10.0.0
- Test migrations in non-production environment first
- Backup database before applying migrations

**EF Core 10.0 Features to Explore**:
- Review new features: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew
- Performance improvements
- New LINQ operators

---

## Appendix

### Reference Links

**Official Documentation**:
- .NET 10 Download: https://dotnet.microsoft.com/download/dotnet/10.0
- .NET 10 Breaking Changes: https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0
- ASP.NET Core 10.0 What's New: https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0
- EF Core 10.0 What's New: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew
- Blazor What's New: https://learn.microsoft.com/en-us/aspnet/core/blazor/whats-new
- .NET Aspire Documentation: https://learn.microsoft.com/en-us/dotnet/aspire/

**NuGet Package Sources**:
- NuGet.org: https://www.nuget.org/
- .NET Preview Feeds: https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json

### Quick Reference Commands

**Check .NET SDKs**:
```bash
dotnet --list-sdks
dotnet --info
```

**Restore and Build**:
```bash
dotnet restore
dotnet build --configuration Release
dotnet build --no-restore
```

**Run Projects**:
```bash
dotnet run --project ChefKnifeStudios.PokerAttack.AppHost
dotnet run --project Server\ChefKnifeStudios.PokerAttack.Server.WebAPI
```

**Docker**:
```bash
docker build -f src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Dockerfile -t poker-attack-api .
docker run -d -p 5268:8080 --name poker-attack-api poker-attack-api
docker logs poker-attack-api
```

**Git**:
```bash
git status
git add .
git commit -m "message"
git log --oneline
git diff
```

---

## Plan Metadata

- **Plan Version**: 1.0
- **Created**: 2025-01-27
- **Target Framework**: .NET 10.0 (Preview)
- **Strategy**: Big Bang
- **Estimated Duration**: 6.5-8 hours
- **Risk Level**: Medium
- **Projects**: 11
- **Package Updates**: 16

---

**End of Migration Plan**