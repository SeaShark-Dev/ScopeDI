using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;
using ScopeDI;
using ScopeDI.Core;
using IScope = ScopeDI.Abstractions.IScope;

namespace ScopeDI.Benchmarks.Benchmarks
{
    // ==========================================
    // TEST SERVICE GRAPH (Mixed Lifetimes)
    // ==========================================
    public interface ICache { }
    public class MemoryCache : ICache { } // Singleton / Root Scoped

    public interface ILogger : IDisposable { }
    public class ConsoleLogger : ILogger
    {
        public void Dispose() { /* Simulated resource cleanup */ }
    }

    public interface IRepository { }
    public class UserRepository : IRepository
    {
        // Combines Scoped and Root Singleton dependencies
        public UserRepository(ICache cache, ILogger logger) { }
    }

    public interface IController { void Handle(); }
    public class UserController : IController
    {
        // Root of the request dependency graph
        public UserController(IRepository repo, ILogger logger) { }
        public void Handle() { /* Simulated request handling */ }
    }

    // ==========================================
    // BENCHMARK SUITE
    // ==========================================
    [MemoryDiagnoser]
    [ThreadingDiagnoser] // Measures lock contentions under parallel load
    public class Aggressive_DI_Benchmark
    {
        private IServiceProvider _msDi = null!;
        private IContainer _dryIoc = null!;
        private IScope _myDiRoot = null!;

        private int _childScopeId;
        private int _controllerId;

        private const int RequestsCount = 10_000;

        [GlobalSetup]
        public void Setup()
        {
            // 1. Microsoft DI
            var services = new ServiceCollection();
            services.AddSingleton<ICache, MemoryCache>();
            services.AddTransient<ILogger, ConsoleLogger>();
            services.AddScoped<IRepository, UserRepository>();
            services.AddScoped<IController, UserController>();
            _msDi = services.BuildServiceProvider();

            // 2. DryIoc
            _dryIoc = new Container(rules => rules.WithoutThrowOnRegisteringDisposableTransient());
            _dryIoc.Register<ICache, MemoryCache>(Reuse.Singleton);
            _dryIoc.Register<ILogger, ConsoleLogger>(Reuse.Transient);
            _dryIoc.Register<IRepository, UserRepository>(Reuse.Scoped);
            _dryIoc.Register<IController, UserController>(Reuse.Scoped);

            // 3. ScopeDI
            var rootBuilder = new ScopeBuilder<IRootScope>();
            rootBuilder.Register<ICache, MemoryCache>(Lifetime.Scoped); // Root-scoped singleton

            rootBuilder.ConfigureChild<ExpressionScope>(child =>
            {
                child.Register<ILogger, ConsoleLogger>(Lifetime.Transient);
                child.Register<IRepository, UserRepository>(Lifetime.Scoped);
                child.Register<IController, UserController>(Lifetime.Scoped);
            });

            _myDiRoot = rootBuilder.Build();

            // Precompute integer IDs for direct array access
            _myDiRoot.TryGetScopeId(typeof(ExpressionScope), out _childScopeId);
            using var tempScope = _myDiRoot.CreateChildScope<ExpressionScope>();
            tempScope.TryGetId(typeof(IController), out _controllerId);
        }

        // ==========================================
        // PHASE 1: SEQUENTIAL STRESS TEST (Graph Depth & IDisposable)
        // ==========================================
        [Benchmark]
        public void MsDi_Sequential_Complex()
        {
            for (int i = 0; i < RequestsCount; i++)
            {
                using var scope = _msDi.CreateScope();
                var controller = scope.ServiceProvider.GetRequiredService<IController>();
                controller.Handle();
            }
        }

        [Benchmark]
        public void DryIoc_Sequential_Complex()
        {
            for (int i = 0; i < RequestsCount; i++)
            {
                using var scope = _dryIoc.OpenScope();
                var controller = scope.Resolve<IController>();
                controller.Handle();
            }
        }

        [Benchmark]
        public void MyDi_Sequential_Complex()
        {
            for (int i = 0; i < RequestsCount; i++)
            {
                _myDiRoot.TryCreateScope(_childScopeId, out IScope scope);
                var controller = (IController)scope.Resolve(_controllerId);
                controller.Handle();
                scope.Dispose(); // Disposes tracked Stack<IDisposable>
            }
        }

        // ==========================================
        // PHASE 2: CONCURRENT MULTI-THREADED WEB LOAD
        // ==========================================
        [Benchmark]
        public void MsDi_Parallel_WebLoad()
        {
            Parallel.For(0, RequestsCount, i =>
            {
                using var scope = _msDi.CreateScope();
                var controller = scope.ServiceProvider.GetRequiredService<IController>();
                controller.Handle();
            });
        }

        [Benchmark]
        public void DryIoc_Parallel_WebLoad()
        {
            Parallel.For(0, RequestsCount, i =>
            {
                using var scope = _dryIoc.OpenScope();
                var controller = scope.Resolve<IController>();
                controller.Handle();
            });
        }

        [Benchmark]
        public void MyDi_Parallel_WebLoad()
        {
            Parallel.For(0, RequestsCount, i =>
            {
                _myDiRoot.TryCreateScope(_childScopeId, out IScope scope);
                var controller = (IController)scope.Resolve(_controllerId);
                controller.Handle();
                scope.Dispose();
            });
        }
    }
}
