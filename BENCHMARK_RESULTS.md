# ScopeDI Benchmark Results & Performance Analysis

This document summarizes the performance benchmarks of the lightweight **ScopeDI (MyDi)** Dependency Injection container compared against **Microsoft.Extensions.DependencyInjection (MsDi)** and **DryIoc**.

All benchmarks were executed using [BenchmarkDotNet v0.15.8](https://benchmarkdotnet.org/).

---

## 1. Core Lifetimes & Scoped Resolution (`DI_Benchmark`)

* **Benchmark Date**: 16.10.2026
* **Environment**: Intel Core i5-14600KF 3.50GHz (20 logical / 14 physical cores), Windows 11 (25H2), .NET 9.0.21, RyuJIT x86-64-v3.
* **Scenario Description**:
  * `DirectNew`: Baseline handwritten constructor invocation (`new User(new Person())`).
  * `Transient`: Creating a fresh service graph instance on every request.
  * `Singleton`: Resolving a single shared instance from the root container.
  * `Scoped`: Opening a child scope (`CreateScope` / `CreateChildScope` / `OpenScope`), resolving `IUser`, and disposing the scope (`Dispose`).
  * `MyDi_Scoped_Direct`: Ultra-low-latency ScopeDI path — creating a child scope via precomputed `ScopeId` and resolving the service by integer `ServiceId`.

### Results Table

| Method | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Allocated | Alloc Ratio |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| **DirectNew** | 14.58 ns | 0.368 ns | 1.081 ns | 1.01 | 0.13 | 0.0045 | 56 B | 1.00 |
| **MsDi_Transient** | 31.75 ns | 0.399 ns | 0.354 ns | 2.19 | 0.24 | 0.0044 | 56 B | 1.00 |
| **MyDi_Transient** | 29.40 ns | 0.346 ns | 0.324 ns | 2.03 | 0.22 | 0.0044 | 56 B | 1.00 |
| **DryIoc_Transient** | 23.53 ns | 0.276 ns | 0.258 ns | 1.63 | 0.18 | 0.0044 | 56 B | 1.00 |
| **MsDi_Singleton** | 18.04 ns | 0.270 ns | 0.253 ns | 1.25 | 0.14 | - | 0 B | 0.00 |
| **MyDi_Singleton** | 18.25 ns | 0.438 ns | 0.537 ns | 1.26 | 0.14 | - | 0 B | 0.00 |
| **DryIoc_Singleton** | 10.36 ns | 0.285 ns | 0.426 ns | 0.72 | 0.08 | - | 0 B | 0.00 |
| **MsDi_Scoped** | 167.66 ns | 3.185 ns | 2.979 ns | 11.59 | 1.26 | 0.0274 | 344 B | 6.14 |
| **MyDi_Scoped** | **81.02 ns** | 0.870 ns | 0.814 ns | **5.60** | 0.60 | 0.0159 | **200 B** | **3.57** |
| **DryIoc_Scoped** | 133.04 ns | 1.549 ns | 1.293 ns | 9.19 | 0.99 | 0.0343 | 432 B | 7.71 |
| **MyDi_Scoped_Direct** | **61.73 ns** | 0.960 ns | 0.943 ns | **4.27** | 0.46 | 0.0159 | **200 B** | **3.57** |

### Key Takeaways:
1. **Scoped Lifecycle**: ScopeDI outperforms Microsoft DI by more than **2x** (81.02 ns vs 167.66 ns) and beats DryIoc by **40%** (133.04 ns).
2. **Memory Allocations**: ScopeDI allocates only **200 B** per full scope creation and disposal cycle (compared to 344 B in MS DI and 432 B in DryIoc).
3. **Direct Index Access (`MyDi_Scoped_Direct`)**: Resolving by precomputed `ScopeId` and `ServiceId` indices drops resolution time to **61.73 ns** (**2.7x faster** than MS DI).

---

## 2. Deep Dependency Graph & Cold Start (`DeepGraph_And_ColdStart_Benchmark`)

* **Benchmark Date**: 08.06.2026
* **Environment**: Intel Core i5-9400F 2.90GHz, Windows 10, .NET 9.0.14.
* **Scenario Description**:
  * `DeepGraph_Resolve`: Resolving a multi-layer deep dependency tree inside an already initialized container.
  * `ColdStart`: Container registration and initial compilation from scratch.

### Results Table

| Method | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| **MsDi_DeepGraph_Resolve** | 327.58 ns | 6.528 ns | 9.152 ns | 0.1459 | - | 688 B |
| **DryIoc_DeepGraph_Resolve** | 197.21 ns | 3.927 ns | 6.877 ns | 0.1137 | - | 536 B |
| **MyDi_DeepGraph_Resolve** | **89.01 ns** | 0.714 ns | 0.596 ns | 0.0612 | - | **288 B** |
| **MsDi_ColdStart** | 16,975.90 ns | 2,750.129 ns | 8,108.819 ns | 1.0014 | 0.2499 | 4 712 B |
| **DryIoc_ColdStart** | 689.40 ns | 8.748 ns | 7.755 ns | 0.2747 | 0.0010 | 1 296 B |
| **MyDi_ColdStart** | 1,106,375.03 ns (~1.1 ms) | 9,488.528 ns | 8,411.333 ns | 9.7656 | 7.8125 | 49 215 B |

### Key Takeaways:
1. **Deep Runtime Graphs**: ScopeDI resolves deep object graphs **3.7x faster than Microsoft DI** (89 ns vs 328 ns) and **2.2x faster than DryIoc** (197 ns), while cutting heap allocations by more than 2.3x (288 B vs 688 B in MS DI).
2. **Architectural Trade-off**: ScopeDI's cold start takes ~1.1 ms because the entire dependency graph is flattened and compiled via Expression Trees (`FastExpressionCompiler`) upfront during `Build()`.

---

## 3. Aggressive Sequential & Parallel Web Load (`Aggressive_DI_Benchmark`)

* **Benchmark Date**: 16.06.2026
* **Environment**: Intel Core i5-9400F 2.90GHz, Windows 10, .NET 9.0.14.
* **Scenario Description**:
  * 10,000 simulated web requests. Each request opens a child scope, resolves a controller with a mixed lifetime graph — Singleton (`ICache`), Scoped (`IRepository`), and Transient Disposable (`ILogger`) — invokes the handler, and disposes the scope.
  * `Sequential_Complex`: Sequential execution of 10,000 requests.
  * `Parallel_WebLoad`: Concurrent execution across threads via `Parallel.For`.

### Results Table

| Method | Mean | Error | StdDev | Gen0 | Gen1 | Allocated | Lock Contentions |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| **MsDi_Sequential_Complex** | 2,426.5 μs | 15.74 μs | 13.95 μs | 1101.5625 | - | 4.96 MB | - |
| **DryIoc_Sequential_Complex** | 1,432.1 μs | 15.34 μs | 12.81 μs | 900.3906 | - | 4.04 MB | - |
| **MyDi_Sequential_Complex** | **1,023.8 μs** | 14.74 μs | 13.06 μs | 748.0469 | - | **3.36 MB** | - |
| **MsDi_Parallel_WebLoad** | 726.8 μs | 12.98 μs | 11.50 μs | 1113.2813 | 21.4844 | 4.96 MB | - |
| **DryIoc_Parallel_WebLoad** | 522.4 μs | 10.37 μs | 22.10 μs | 918.9453 | 11.7188 | 4.05 MB | - |
| **MyDi_Parallel_WebLoad** | **624.7 μs** | 12.35 μs | 28.38 μs | 751.9531 | 10.7422 | **3.36 MB** | 0.0020 |

### Key Takeaways:
1. **Sequential Complex Workload**: ScopeDI is **2.4x faster** than MS DI and **28% faster** than DryIoc, saving 1.6 MB of heap allocations per 10,000 requests.
2. **Concurrent Multi-Threaded Workload**: ScopeDI completes faster than Microsoft DI (624 μs vs 726 μs) while maintaining the lowest memory footprint (3.36 MB vs 4.96 MB in MS DI and 4.05 MB in DryIoc).

---

## 4. Resource Teardown Stress Test (`ScopeDI_Massacre_Benchmark` / `Enterprise Nightmare`)

* **Benchmark Date**: 16.06.2026
* **Environment**: Intel Core i5-9400F 2.90GHz, Windows 10, .NET 9.0.14.
* **Scenario Description**:
  * 5,000 requests.
  * The service registry is intentionally polluted with unused scoped registrations (`Dummy1`...`Dummy10`).
  * The target endpoint resolves a cascade of 6 transient `IDisposable` dependencies (`IHeavyTask1`...`IHeavyTask6`), stress-testing the scope disposal cleanup stack.

### Results Table

| Method | Mean | Error | StdDev | Ratio | Gen0 | Gen1 | Allocated | Alloc Ratio |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| **MsDi_Massacre** | 1,727.9 μs | 5.82 μs | 4.86 μs | 1.00 | 824.2188 | - | 3.70 MB | 1.00 |
| **DryIoc_Massacre** | 571.2 μs | 0.54 μs | 0.45 μs | 0.33 | 390.6250 | - | 1.75 MB | 0.47 |
| **MyDi_Massacre** | **742.0 μs** | 2.35 μs | 2.09 μs | **0.43** | 611.3281 | 0.9766 | **2.75 MB** | **0.74** |

### Key Takeaways:
1. ScopeDI handles cascading `IDisposable` cleanup and polluted registries **2.3x faster than Microsoft DI** (742 μs vs 1,728 μs) while allocating 26% less memory.

---

## Summary of Architectural Advantages

1. **Designed for High-Frequency Scopes**: ScopeDI is purpose-built for workloads that rapidly create and tear down child scopes (web servers, game server rooms, per-connection network pipelines).
2. **Reduced GC Pressure**: Memory allocations during scoped resolution are 30–50% lower than standard Microsoft DI.
3. **Direct Array Index Resolution (`ScopeId` / `ServiceId`)**: Eliminates dictionary and hash-table lookups on hot paths.
4. **Upfront Compilation Trade-off**: Shifts graph analysis and delegate compilation to `Build()` (~1 ms cold start) so runtime resolutions execute at near-native `new` speeds.
