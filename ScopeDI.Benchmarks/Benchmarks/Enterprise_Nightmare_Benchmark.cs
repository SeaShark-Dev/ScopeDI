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
    // 1. ИМИТАЦИЯ "ТОЛСТОГО" СКОУПА (Реестр забит хламом)
    // ==========================================
    public interface IDummy1 { }
    public class Dummy1 : IDummy1 { }
    public interface IDummy2 { }
    public class Dummy2 : IDummy2 { }
    public interface IDummy3 { }
    public class Dummy3 : IDummy3 { }
    public interface IDummy4 { }
    public class Dummy4 : IDummy4 { }
    public interface IDummy5 { }
    public class Dummy5 : IDummy5 { }
    public interface IDummy6 { }
    public class Dummy6 : IDummy6 { }
    public interface IDummy7 { }
    public class Dummy7 : IDummy7 { }
    public interface IDummy8 { }
    public class Dummy8 : IDummy8 { }
    public interface IDummy9 { }
    public class Dummy9 : IDummy9 { }
    public interface IDummy10 { }
    public class Dummy10 : IDummy10 { }

    // ==========================================
    // 2. КАСКАД ТРАНЗИЕНТОВ (Ловушка для Stack<IDisposable>)
    // ==========================================
    public interface IHeavyTask1 : IDisposable { }
    public class HeavyTask1 : IHeavyTask1 { public void Dispose() { } }
    public interface IHeavyTask2 : IDisposable { }
    public class HeavyTask2 : IHeavyTask2 { public void Dispose() { } }
    public interface IHeavyTask3 : IDisposable { }
    public class HeavyTask3 : IHeavyTask3 { public void Dispose() { } }
    public interface IHeavyTask4 : IDisposable { }
    public class HeavyTask4 : IHeavyTask4 { public void Dispose() { } }
    public interface IHeavyTask5 : IDisposable { }
    public class HeavyTask5 : IHeavyTask5 { public void Dispose() { } }
    public interface IHeavyTask6 : IDisposable { }
    public class HeavyTask6 : IHeavyTask6 { public void Dispose() { } }

    // Целевой класс. Ему плевать на Dummy, но он тянет все HeavyTask
    public interface IGodEndpoint { void Execute(); }
    public class GodEndpoint : IGodEndpoint
    {
        public GodEndpoint(
            IHeavyTask1 t1, IHeavyTask2 t2, IHeavyTask3 t3,
            IHeavyTask4 t4, IHeavyTask5 t5, IHeavyTask6 t6)
        { }

        public void Execute() { }
    }

    [MemoryDiagnoser]
    public class ScopeDI_Massacre_Benchmark
    {
        private IServiceProvider _msDi = null!;
        private IContainer _dryIoc = null!;
        private IScope _myDiRoot = null!;

        private int _requestScopeId;
        private int _endpointId;

        // Эмуляция 5000 запросов
        private const int RequestsCount = 5000;

        [GlobalSetup]
        public void Setup()
        {
            // 1. MS DI
            var services = new ServiceCollection();
            RegisterAllMsDi(services);
            _msDi = services.BuildServiceProvider();

            // 2. DryIoc
            _dryIoc = new Container(rules => rules.WithoutThrowOnRegisteringDisposableTransient());
            RegisterAllDryIoc(_dryIoc);

            // 3. Твой ScopeDI
            var rootBuilder = new ScopeBuilder<IRootScope>();
            rootBuilder.ConfigureChild<ExpressionScope>(child =>
            {
                RegisterAllScopeDi(child);
            });

            _myDiRoot = rootBuilder.Build();
            _myDiRoot.TryGetScopeId(typeof(ExpressionScope), out _requestScopeId);

            using var tempScope = _myDiRoot.CreateChildScope<ExpressionScope>();
            tempScope.TryGetId(typeof(IGodEndpoint), out _endpointId);
        }

        private void RegisterAllMsDi(IServiceCollection s)
        {
            // Забиваем реестр мусором (MS DI это проигнорирует при резолве)
            s.AddScoped<IDummy1, Dummy1>(); s.AddScoped<IDummy2, Dummy2>();
            s.AddScoped<IDummy3, Dummy3>(); s.AddScoped<IDummy4, Dummy4>();
            s.AddScoped<IDummy5, Dummy5>(); s.AddScoped<IDummy6, Dummy6>();
            s.AddScoped<IDummy7, Dummy7>(); s.AddScoped<IDummy8, Dummy8>();
            s.AddScoped<IDummy9, Dummy9>(); s.AddScoped<IDummy10, Dummy10>();

            // Регистрируем транзиенты (MS DI имеет оптимизированный трекер)
            s.AddTransient<IHeavyTask1, HeavyTask1>(); s.AddTransient<IHeavyTask2, HeavyTask2>();
            s.AddTransient<IHeavyTask3, HeavyTask3>(); s.AddTransient<IHeavyTask4, HeavyTask4>();
            s.AddTransient<IHeavyTask5, HeavyTask5>(); s.AddTransient<IHeavyTask6, HeavyTask6>();

            s.AddScoped<IGodEndpoint, GodEndpoint>();
        }

        private void RegisterAllDryIoc(IContainer c)
        {
            c.Register<IDummy1, Dummy1>(Reuse.Scoped); c.Register<IDummy2, Dummy2>(Reuse.Scoped);
            c.Register<IDummy3, Dummy3>(Reuse.Scoped); c.Register<IDummy4, Dummy4>(Reuse.Scoped);
            c.Register<IDummy5, Dummy5>(Reuse.Scoped); c.Register<IDummy6, Dummy6>(Reuse.Scoped);
            c.Register<IDummy7, Dummy7>(Reuse.Scoped); c.Register<IDummy8, Dummy8>(Reuse.Scoped);
            c.Register<IDummy9, Dummy9>(Reuse.Scoped); c.Register<IDummy10, Dummy10>(Reuse.Scoped);

            c.Register<IHeavyTask1, HeavyTask1>(Reuse.Transient); c.Register<IHeavyTask2, HeavyTask2>(Reuse.Transient);
            c.Register<IHeavyTask3, HeavyTask3>(Reuse.Transient); c.Register<IHeavyTask4, HeavyTask4>(Reuse.Transient);
            c.Register<IHeavyTask5, HeavyTask5>(Reuse.Transient); c.Register<IHeavyTask6, HeavyTask6>(Reuse.Transient);

            c.Register<IGodEndpoint, GodEndpoint>(Reuse.Scoped);
        }

        private void RegisterAllScopeDi(ScopeBuilder<ExpressionScope> b)
        {
            // Из-за этих регистраций твой массив Instances будет огромным
            b.Register<IDummy1, Dummy1>(Lifetime.Scoped); b.Register<IDummy2, Dummy2>(Lifetime.Scoped);
            b.Register<IDummy3, Dummy3>(Lifetime.Scoped); b.Register<IDummy4, Dummy4>(Lifetime.Scoped);
            b.Register<IDummy5, Dummy5>(Lifetime.Scoped); b.Register<IDummy6, Dummy6>(Lifetime.Scoped);
            b.Register<IDummy7, Dummy7>(Lifetime.Scoped); b.Register<IDummy8, Dummy8>(Lifetime.Scoped);
            b.Register<IDummy9, Dummy9>(Lifetime.Scoped); b.Register<IDummy10, Dummy10>(Lifetime.Scoped);

            // А из-за этих стек Disposables будет постоянно реаллоцироваться
            b.Register<IHeavyTask1, HeavyTask1>(Lifetime.Transient); b.Register<IHeavyTask2, HeavyTask2>(Lifetime.Transient);
            b.Register<IHeavyTask3, HeavyTask3>(Lifetime.Transient); b.Register<IHeavyTask4, HeavyTask4>(Lifetime.Transient);
            b.Register<IHeavyTask5, HeavyTask5>(Lifetime.Transient); b.Register<IHeavyTask6, HeavyTask6>(Lifetime.Transient);

            b.Register<IGodEndpoint, GodEndpoint>(Lifetime.Scoped);
        }

        [Benchmark(Baseline = true)]
        public void MsDi_Massacre()
        {
            for (int i = 0; i < RequestsCount; i++)
            {
                using var scope = _msDi.CreateScope();
                var endpoint = scope.ServiceProvider.GetRequiredService<IGodEndpoint>();
                endpoint.Execute();
            }
        }

        [Benchmark]
        public void DryIoc_Massacre()
        {
            for (int i = 0; i < RequestsCount; i++)
            {
                using var scope = _dryIoc.OpenScope();
                var endpoint = scope.Resolve<IGodEndpoint>();
                endpoint.Execute();
            }
        }

        [Benchmark]
        public void MyDi_Massacre()
        {
            for (int i = 0; i < RequestsCount; i++)
            {
                _myDiRoot.TryCreateScope(_requestScopeId, out IScope scope);
                var endpoint = (IGodEndpoint)scope.Resolve(_endpointId);
                endpoint.Execute();
                scope.Dispose(); // Очистка стека и массивов
            }
        }
    }
}
