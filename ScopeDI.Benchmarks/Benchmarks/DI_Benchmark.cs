using BenchmarkDotNet.Attributes;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;
using ScopeDI;
using ScopeDI.Core;
using ScopeDI.Benchmarks.Types;
using System;

using IScope = ScopeDI.Abstractions.IScope;

namespace ScopeDI.Benchmarks.Benchmarks
{

    [MemoryDiagnoser]
    public class DI_Benchmark
    {
        // Microsoft DI providers
        private IServiceProvider _msDiTransient = null!;
        private IServiceProvider _msDiSingleton = null!;
        private IServiceProvider _msDiScopedProvider = null!;

        // ScopeDI scopes
        private IScope _myDiTransient = null!;
        private IScope _myDiSingleton = null!;
        private IScope _myDiRoot = null!;

        // DryIoc containers
        private IContainer _dryTransient = null!;
        private IContainer _drySingleton = null!;
        private IContainer _dryScopedContainer = null!;

        [GlobalSetup]
        public void Setup()
        {

            // ==========================================
            // 1. TRANSIENT (New instance per resolution)
            // ==========================================
            var msTransient = new ServiceCollection();
            msTransient.AddTransient<IPerson, Person>();
            msTransient.AddTransient<IUser, User>();
            _msDiTransient = msTransient.BuildServiceProvider();

            var myTransient = new ScopeBuilder<IRootScope>();
            myTransient.Register<IPerson, Person>(Lifetime.Transient);
            myTransient.Register<IUser, User>(Lifetime.Transient);
            _myDiTransient = myTransient.Build();

            var dryTransient = new Container();
            dryTransient.Register<IPerson, Person>(Reuse.Transient);
            dryTransient.Register<IUser, User>(Reuse.Transient);
            _dryTransient = dryTransient;

            // ==========================================
            // 2. SINGLETON (Single shared instance)
            // ==========================================
            var msSingleton = new ServiceCollection();
            msSingleton.AddSingleton<IPerson, Person>();
            msSingleton.AddSingleton<IUser, User>();
            _msDiSingleton = msSingleton.BuildServiceProvider();

            var mySingleton = new ScopeBuilder<IRootScope>();
            mySingleton.Register<IPerson, Person>(Lifetime.Scoped);
            mySingleton.Register<IUser, User>(Lifetime.Scoped);
            _myDiSingleton = mySingleton.Build();

            var drySingleton = new Container();
            drySingleton.Register<IPerson, Person>(Reuse.Singleton);
            drySingleton.Register<IUser, User>(Reuse.Singleton);
            _drySingleton = drySingleton;

            // ==========================================
            // 3. SCOPED (Child scope lifecycle & disposal)
            // ==========================================
            var msScoped = new ServiceCollection();
            msScoped.AddScoped<IPerson, Person>();
            msScoped.AddScoped<IUser, User>();
            _msDiScopedProvider = msScoped.BuildServiceProvider();

            var myRoot = new ScopeBuilder<IRootScope>();
            myRoot.ConfigureChild<ExpressionScope>(child =>
            {
                child.Register<IPerson, Person>(Lifetime.Scoped);
                child.Register<IUser, User>(Lifetime.Scoped);
            });
            _myDiRoot = myRoot.Build();

            var dryScoped = new Container();
            dryScoped.Register<IPerson, Person>(Reuse.Scoped);
            dryScoped.Register<IUser, User>(Reuse.Scoped);
            _dryScopedContainer = dryScoped;

            _myDiRoot.TryGetScopeId(typeof(ExpressionScope), out _testScopeId);

            using (var tempScope = _myDiRoot.CreateChildScope<ExpressionScope>())
            {
                tempScope.TryGetId(typeof(IUser), out _userId);
            }
        }

        // ==========================================
        // BENCHMARKS
        // ==========================================

        [Benchmark(Baseline = true)]
        public IUser DirectNew() => new User(new Person());

        [Benchmark]
        public IUser MsDi_Transient() => _msDiTransient.GetRequiredService<IUser>();

        [Benchmark]
        public IUser MyDi_Transient() => _myDiTransient.Resolve<IUser>();

        [Benchmark]
        public IUser DryIoc_Transient() => _dryTransient.Resolve<IUser>();

        [Benchmark]
        public IUser MsDi_Singleton() => _msDiSingleton.GetRequiredService<IUser>();

        [Benchmark]
        public IUser MyDi_Singleton() => _myDiSingleton.Resolve<IUser>();

        [Benchmark]
        public IUser DryIoc_Singleton() => _drySingleton.Resolve<IUser>();

        [Benchmark]
        public IUser MsDi_Scoped()
        {
            using var scope = _msDiScopedProvider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IUser>();
        }

        [Benchmark]
        public IUser MyDi_Scoped()
        {
            using var scope = _myDiRoot.CreateChildScope<ExpressionScope>();
            return scope.Resolve<IUser>();
        }

        [Benchmark]
        public IUser DryIoc_Scoped()
        {
            using var scope = _dryScopedContainer.OpenScope();
            return scope.Resolve<IUser>();
        }

        private int _testScopeId;
        private int _userId;

        [Benchmark]
        public IUser MyDi_Scoped_Direct()
        {
            _myDiRoot.TryCreateScope(_testScopeId, out IScope scope);
            var result = scope.Resolve(_userId);
            scope.Dispose();
            return (IUser)result;
        }

    }
}
