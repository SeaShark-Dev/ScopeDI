using ScopeDI.Abstractions;

namespace ScopeDI.Core
{
    public class ScopeBuilder<TScope> : IScopeBuilder where TScope : IScope
    {
        public IReadOnlyList<Blueprint> Blueprints => _blueprints;
        private readonly List<Blueprint> _blueprints = new();

        private int _idCounter = 0;
        private int _scopeIdCounter = 0;

        // Карта: Type -> (ScopeId, Builder) для сборки плоского массива фабрик
        private readonly Dictionary<Type, (int ScopeId, object Builder)> _childBuildersMap = new();

        public IScopeBuilder ParentBuilder { get; }

        public ScopeBuilder() : this(null) { }

        internal ScopeBuilder(IScopeBuilder parent)
        {
            ParentBuilder = parent;
        }

        public int? GetLocalId(Type serviceType)
        {
            return _blueprints.FirstOrDefault(b => b.ServiceType == serviceType)?.LocalId;
        }

        public void Register<TService, TImplementation>(Lifetime lifetime = Lifetime.Scoped)
            where TImplementation : TService
        {
            _blueprints.Add(new Blueprint
            {
                ServiceType = typeof(TService),
                ImplementationType = typeof(TImplementation),
                Lifetime = lifetime,
                LocalId = _idCounter++
            });
        }

        public void ConfigureChild<TChildScope>(Action<ScopeBuilder<TChildScope>> configure)
            where TChildScope : IScope
        {
            var childBuilder = new ScopeBuilder<TChildScope>(this);
            configure(childBuilder);

            int scopeId = _scopeIdCounter++;
            _childBuildersMap[typeof(TChildScope)] = (scopeId, childBuilder);
        }

        public Func<IScope, IScope> CompileScopeFactory()
        {
            var compiler = new ScopeCompiler(this);
            var componentFactories = compiler.CompileAll();
            var typeMap = _blueprints.ToDictionary(b => b.ServiceType, b => b.LocalId);

            var childScopeFactories = new Func<IScope, IScope>[_scopeIdCounter];
            var scopeTypeMap = new Dictionary<Type, int>();

            foreach (var kvp in _childBuildersMap)
            {
                Type childType = kvp.Key;
                int scopeId = kvp.Value.ScopeId;
                IScopeBuilder childBuilder = (IScopeBuilder)kvp.Value.Builder;

                childScopeFactories[scopeId] = childBuilder.CompileScopeFactory();
                scopeTypeMap[childType] = scopeId;
            }

            int capacity = _blueprints.Count;

            return (IScope parent) => new ExpressionScope(
                parent,
                componentFactories,
                capacity,
                typeMap,
                childScopeFactories,
                scopeTypeMap);
        }

        public IScope Build(IScope parentScope = null)
        {
            var factory = CompileScopeFactory();
            return factory(parentScope);
        }
    }
}