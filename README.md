# ScopeDI

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Benchmarks](https://img.shields.io/badge/BenchmarkDotNet-v0.15.8-green.svg)](https://benchmarkdotnet.org/)

**ScopeDI** is a lightweight, ultra-fast, expression-tree-compiled Dependency Injection container for .NET, engineered specifically for high-throughput scoped workloads (game servers, web APIs, per-session pipelines).

By shifting dependency graph analysis and resolution delegate generation to an upfront compilation phase (`Build()`), ScopeDI completely removes dictionary lookups and reflection from the hot path, achieving speeds close to handwritten `new` calls while cutting memory allocations by up to 50%.

---

## Key Architectural Principles

- **Pre-Compiled Resolution Graphs**: Uses [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler) during `Build()` to flatten and compile the entire dependency tree into direct factory delegates.
- **Hierarchical First-Class Scopes**: Native support for parent-to-child scope hierarchies with fallback resolution (`Root` -> `Session` -> `Request`).
- **Direct Array Indexing**: Services are mapped to contiguous integer IDs (`localId`). Instances in a scope reside in a pre-allocated flat array (`object[]`), eliminating hashtable and dictionary search overhead.
- **Zero Double-Instantiation IDisposable Management**: Custom expression-tree tracking ensures `IDisposable` instances are registered into the cleanup stack without duplicate constructor execution.
- **Direct ID Access Option**: Hot paths can bypass type checking entirely by resolving via precomputed `ScopeId` and `ServiceId`.

---

## Performance Highlights

Tested against **Microsoft.Extensions.DependencyInjection (MsDi)** and **DryIoc** with [BenchmarkDotNet v0.15.8](https://benchmarkdotnet.org/) on .NET 9:

- **Scoped Resolution**: **81.02 ns** vs **167.66 ns** (MsDi) — **2x faster**, with memory reduced from 344 B to **200 B**.
- **Direct Index Resolution (`MyDi_Scoped_Direct`)**: **61.73 ns** — **2.7x faster** than standard Microsoft DI.
- **Deep Graph Resolution**: **89.01 ns** vs **327.58 ns** (MsDi) and **197.21 ns** (DryIoc) — **3.7x faster**, 58% fewer memory allocations.
- **Disposables Massacre (5,000 requests, deep transient stack)**: **742 μs** vs **1,727 μs** (MsDi) — **2.3x faster** teardown and 26% lower GC pressure.

*See full benchmark breakdown and methodology in [BENCHMARK_RESULTS.md](BENCHMARK_RESULTS.md).*

---

## Quick Start

### 1. Define Scope Markers and Services

```csharp
using ScopeDI.Abstractions;
using ScopeDI.Core;

// Marker interfaces for scope hierarchy
public interface IRootScope : IScope { }
public interface IMatchScope : IScope { }

// Sample contracts & implementations
public interface IDatabase { void Query(); }
public class PostgresDatabase : IDatabase
{
    public void Query() => Console.WriteLine("Querying database...");
}

public interface IPlayerManager { void Join(); }
public class PlayerManager : IPlayerManager
{
    private readonly IDatabase _db;
    public PlayerManager(IDatabase db) => _db = db;
    public void Join() => _db.Query();
}
```

### 2. Configure and Compile the Container

```csharp
var rootBuilder = new ScopeBuilder<IRootScope>();

// Register root dependencies (e.g., Scoped to root or Transient)
rootBuilder.Register<IDatabase, PostgresDatabase>(Lifetime.Scoped);

// Configure child scopes
rootBuilder.ConfigureChild<IMatchScope>(match =>
{
    match.Register<IPlayerManager, PlayerManager>(Lifetime.Transient);
});

// Upfront compilation phase (compiles lambda trees with FastExpressionCompiler)
using var rootScope = rootBuilder.Build();
```

### 3. Create Child Scopes and Resolve Services

```csharp
// Standard type-safe resolution
using (var matchScope = rootScope.CreateChildScope<IMatchScope>())
{
    var playerManager = matchScope.Resolve<IPlayerManager>();
    playerManager.Join();
}
```

### 4. Direct Index Resolution (Ultra-Low Latency Mode)

For performance-critical code where every nanosecond counts:

```csharp
// Precompute IDs once during initialization
rootScope.TryGetScopeId(typeof(IMatchScope), out int matchScopeId);

using (var temp = rootScope.CreateChildScope<IMatchScope>())
{
    temp.TryGetId(typeof(IPlayerManager), out int managerId);
    
    // In hot loop: zero dictionary lookups, pure array access
    if (rootScope.TryCreateScope(matchScopeId, out var fastScope))
    {
        using (fastScope)
        {
            var manager = (IPlayerManager)fastScope.Resolve(managerId);
            manager.Join();
        }
    }
}
```

---

## Benchmarks Summary

### 1. Lifecycle and Scoped Teardown (`DI_Benchmark`)

| Method | Mean | Ratio | Allocated | Alloc Ratio |
| :--- | ---: | ---: | ---: | ---: |
| **DirectNew** | 14.58 ns | 1.00x | 56 B | 1.00x |
| **MsDi_Scoped** | 167.66 ns | 11.59x | 344 B | 6.14x |
| **DryIoc_Scoped** | 133.04 ns | 9.19x | 432 B | 7.71x |
| **ScopeDI (Generic)** | **81.02 ns** | **5.60x** | **200 B** | **3.57x** |
| **ScopeDI (Direct ID)** | **61.73 ns** | **4.27x** | **200 B** | **3.57x** |

### 2. Deep Graph Resolution (`DeepGraph_And_ColdStart_Benchmark`)

| Method | Mean | Allocated |
| :--- | ---: | ---: |
| **MsDi_DeepGraph_Resolve** | 327.58 ns | 688 B |
| **DryIoc_DeepGraph_Resolve** | 197.21 ns | 536 B |
| **ScopeDI_DeepGraph_Resolve** | **89.01 ns** | **288 B** |

### 3. Running the Benchmarks

Clone the repository and run:

```bash
dotnet run -c Release --project ScopeDI.Benchmarks
```

BenchmarkDotNet will display an interactive list of available benchmark suites to execute.

---

## Architecture Trade-offs

| Aspect | Standard Reflection DI (e.g. MS DI) | ScopeDI |
| :--- | :--- | :--- |
| **Cold Start / `Build()`** | Fast (~0.01 ms) | Deliberate upfront compilation (~1.1 ms) |
| **Runtime Resolution** | Dictionary lookups & reflection | Direct compiled delegates + array indexing |
| **Scoped Allocations** | Higher overhead per scope (344 B) | Minimal array pre-allocation (200 B) |
| **Scope Teardown** | Standard enumeration | Pre-tracked stack unwinding |

---

## Solution Structure

- `ScopeDI/` — Core DI container library (interfaces, compiler, expression scope, lifetimes).
- `ScopeDI.Benchmarks/` — Performance testing suite powered by BenchmarkDotNet comparing ScopeDI against Microsoft DI and DryIoc.

---

## License

This project is licensed under the [MIT License](LICENSE).
