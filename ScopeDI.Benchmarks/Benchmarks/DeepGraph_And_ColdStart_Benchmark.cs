using System;
using BenchmarkDotNet.Attributes;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;
using ScopeDI;
using ScopeDI.Core;

using IScope = ScopeDI.Abstractions.IScope;

namespace ScopeDI.Benchmarks.Benchmarks
{
    // ==========================================
    // ИМИТАЦИЯ СЛОЖНОГО ГРАФА ЗАВИСИМОСТЕЙ
    // ==========================================
    public interface IDatabaseConnection { }
    public class PostgresConnection : IDatabaseConnection { } // Root Scoped

    public interface IPlayerCache { }
    public class RedisPlayerCache : IPlayerCache { } // Root Scoped

    public interface IPlayerRepository { }
    public class PlayerRepository : IPlayerRepository
    {
        public PlayerRepository(IDatabaseConnection db, IPlayerCache cache) { }
    } // Scoped

    public interface IRatingCalculator { }
    public class EloRatingCalculator : IRatingCalculator
    {
        public EloRatingCalculator(IPlayerCache cache) { }
    } // Scoped

    public interface IMatchmakerLogic { }
    public class MatchmakerLogic : IMatchmakerLogic
    {
        public MatchmakerLogic(IPlayerRepository repo, IRatingCalculator rating) { }
    } // Scoped

    public interface IEventDispatcher { }
    public class EventDispatcher : IEventDispatcher { } // Transient

    public interface IMatchController { void StartMatch(); }
    public class MatchController : IMatchController
    {
        // Самая вершина графа: тянет логику, которая тянет репозитории, которые тянут БД
        public MatchController(IMatchmakerLogic logic, IEventDispatcher dispatcher) { }
        public void StartMatch() { }
    } // Scoped

    [MemoryDiagnoser]
    public class DeepGraph_And_ColdStart_Benchmark
    {
        private IServiceProvider _msDi = null!;
        private IContainer _dryIoc = null!;
        private IScope _myDiRoot = null!;

        private int _testScopeId;
        private int _controllerId;

        [GlobalSetup]
        public void Setup()
        {
            // Настройка MS DI
            var services = new ServiceCollection();
            services.AddSingleton<IDatabaseConnection, PostgresConnection>();
            services.AddSingleton<IPlayerCache, RedisPlayerCache>();
            services.AddScoped<IPlayerRepository, PlayerRepository>();
            services.AddScoped<IRatingCalculator, EloRatingCalculator>();
            services.AddScoped<IMatchmakerLogic, MatchmakerLogic>();
            services.AddTransient<IEventDispatcher, EventDispatcher>();
            services.AddScoped<IMatchController, MatchController>();
            _msDi = services.BuildServiceProvider();

            // Настройка DryIoc
            _dryIoc = new Container();
            _dryIoc.Register<IDatabaseConnection, PostgresConnection>(Reuse.Singleton);
            _dryIoc.Register<IPlayerCache, RedisPlayerCache>(Reuse.Singleton);
            _dryIoc.Register<IPlayerRepository, PlayerRepository>(Reuse.Scoped);
            _dryIoc.Register<IRatingCalculator, EloRatingCalculator>(Reuse.Scoped);
            _dryIoc.Register<IMatchmakerLogic, MatchmakerLogic>(Reuse.Scoped);
            _dryIoc.Register<IEventDispatcher, EventDispatcher>(Reuse.Transient);
            _dryIoc.Register<IMatchController, MatchController>(Reuse.Scoped);

            // Настройка ScopeDI
            var rootBuilder = new ScopeBuilder<IRootScope>();
            rootBuilder.Register<IDatabaseConnection, PostgresConnection>(Lifetime.Scoped);
            rootBuilder.Register<IPlayerCache, RedisPlayerCache>(Lifetime.Scoped);

            rootBuilder.ConfigureChild<ExpressionScope>(child =>
            {
                child.Register<IPlayerRepository, PlayerRepository>(Lifetime.Scoped);
                child.Register<IRatingCalculator, EloRatingCalculator>(Lifetime.Scoped);
                child.Register<IMatchmakerLogic, MatchmakerLogic>(Lifetime.Scoped);
                child.Register<IEventDispatcher, EventDispatcher>(Lifetime.Transient);
                child.Register<IMatchController, MatchController>(Lifetime.Scoped);
            });

            _myDiRoot = rootBuilder.Build();
            _myDiRoot.TryGetScopeId(typeof(ExpressionScope), out _testScopeId);

            using var tempScope = _myDiRoot.CreateChildScope<ExpressionScope>();
            tempScope.TryGetId(typeof(IMatchController), out _controllerId);
        }

        // ==========================================
        // ФАЗА 1: Сборка глубокого графа (Рантайм)
        // ==========================================
        [Benchmark]
        public void MsDi_DeepGraph_Resolve()
        {
            using var scope = _msDi.CreateScope();
            var controller = scope.ServiceProvider.GetRequiredService<IMatchController>();
            controller.StartMatch();
        }

        [Benchmark]
        public void DryIoc_DeepGraph_Resolve()
        {
            using var scope = _dryIoc.OpenScope();
            var controller = scope.Resolve<IMatchController>();
            controller.StartMatch();
        }

        [Benchmark]
        public void MyDi_DeepGraph_Resolve()
        {
            _myDiRoot.TryCreateScope(_testScopeId, out IScope scope);
            var controller = (IMatchController)scope.Resolve(_controllerId);
            controller.StartMatch();
            scope.Dispose();
        }

        // ==========================================
        // ФАЗА 2: Холодный старт (Компиляция контейнера)
        // ==========================================
        [Benchmark]
        public IServiceProvider MsDi_ColdStart()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IDatabaseConnection, PostgresConnection>();
            services.AddSingleton<IPlayerCache, RedisPlayerCache>();
            services.AddScoped<IPlayerRepository, PlayerRepository>();
            services.AddScoped<IRatingCalculator, EloRatingCalculator>();
            services.AddScoped<IMatchmakerLogic, MatchmakerLogic>();
            services.AddTransient<IEventDispatcher, EventDispatcher>();
            services.AddScoped<IMatchController, MatchController>();
            return services.BuildServiceProvider();
        }

        [Benchmark]
        public IContainer DryIoc_ColdStart()
        {
            var container = new Container();
            container.Register<IDatabaseConnection, PostgresConnection>(Reuse.Singleton);
            container.Register<IPlayerCache, RedisPlayerCache>(Reuse.Singleton);
            container.Register<IPlayerRepository, PlayerRepository>(Reuse.Scoped);
            container.Register<IRatingCalculator, EloRatingCalculator>(Reuse.Scoped);
            container.Register<IMatchmakerLogic, MatchmakerLogic>(Reuse.Scoped);
            container.Register<IEventDispatcher, EventDispatcher>(Reuse.Transient);
            container.Register<IMatchController, MatchController>(Reuse.Scoped);
            return container;
        }

        [Benchmark]
        public IScope MyDi_ColdStart()
        {
            var rootBuilder = new ScopeBuilder<IRootScope>();
            rootBuilder.Register<IDatabaseConnection, PostgresConnection>(Lifetime.Scoped);
            rootBuilder.Register<IPlayerCache, RedisPlayerCache>(Lifetime.Scoped);

            rootBuilder.ConfigureChild<ExpressionScope>(child =>
            {
                child.Register<IPlayerRepository, PlayerRepository>(Lifetime.Scoped);
                child.Register<IRatingCalculator, EloRatingCalculator>(Lifetime.Scoped);
                child.Register<IMatchmakerLogic, MatchmakerLogic>(Lifetime.Scoped);
                child.Register<IEventDispatcher, EventDispatcher>(Lifetime.Transient);
                child.Register<IMatchController, MatchController>(Lifetime.Scoped);
            });

            return rootBuilder.Build();
        }
    }
}
