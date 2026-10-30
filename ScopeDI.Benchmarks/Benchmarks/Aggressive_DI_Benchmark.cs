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
    // ТЕСТОВЫЕ КЛАССЫ (Сложный граф)
    // ==========================================
    public interface ICache { }
    public class MemoryCache : ICache { } // Будет Singleton / Root Scoped

    public interface ILogger : IDisposable { }
    public class ConsoleLogger : ILogger
    {
        public void Dispose() { /* Имитация очистки ресурсов */ }
    }

    public interface IRepository { }
    public class UserRepository : IRepository
    {
        // Смешивание Scoped и Singleton/Root
        public UserRepository(ICache cache, ILogger logger) { }
    }

    public interface IController { void Handle(); }
    public class UserController : IController
    {
        // Вершина графа
        public UserController(IRepository repo, ILogger logger) { }
        public void Handle() { /* Имитация работы */ }
    }

    // ==========================================
    // БЕНЧМАРК
    // ==========================================
    [MemoryDiagnoser]
    [ThreadingDiagnoser] // Покажет потоковые блокировки (Lock Contention)
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
            rootBuilder.Register<ICache, MemoryCache>(Lifetime.Scoped); // Твой аналог Singleton

            rootBuilder.ConfigureChild<ExpressionScope>(child =>
            {
                child.Register<ILogger, ConsoleLogger>(Lifetime.Transient);
                child.Register<IRepository, UserRepository>(Lifetime.Scoped);
                child.Register<IController, UserController>(Lifetime.Scoped);
            });

            _myDiRoot = rootBuilder.Build();

            // Кэшируем ID для максимальной скорости твоего DI
            _myDiRoot.TryGetScopeId(typeof(ExpressionScope), out _childScopeId);
            using var tempScope = _myDiRoot.CreateChildScope<ExpressionScope>();
            tempScope.TryGetId(typeof(IController), out _controllerId);
        }

        // ==========================================
        // ФАЗА 1: ПОСЛЕДОВАТЕЛЬНЫЙ СТРЕСС-ТЕСТ (Глубина и IDisposable)
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
                scope.Dispose(); // Отработка Stack<IDisposable>
            }
        }

        // ==========================================
        // ФАЗА 2: АГРЕССИВНЫЙ МНОГОПОТОЧНЫЙ ТЕСТ (Имитация Web-сервера)
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
