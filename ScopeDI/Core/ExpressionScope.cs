using System;
using System.Collections.Generic;
using ScopeDI.Abstractions;

namespace ScopeDI.Core
{
    public sealed class ExpressionScope : IScope
    {
        public IScope Parent { get; }

        internal readonly object[] Instances;
        internal readonly Stack<IDisposable> Disposables;

        private readonly Func<ExpressionScope, object>[] _factories;
        private readonly IReadOnlyDictionary<Type, int> _typeMap;

        private readonly Func<IScope, IScope>[] _childFactories;
        private readonly IReadOnlyDictionary<Type, int> _scopeTypeMap;

        internal ExpressionScope(
            IScope parent,
            Func<ExpressionScope, object>[] factories,
            int instancesCapacity,
            IReadOnlyDictionary<Type, int> typeMap,
            Func<IScope, IScope>[] childFactories,
            IReadOnlyDictionary<Type, int> scopeTypeMap)
        {
            Parent = parent;
            _factories = factories;
            Instances = new object[instancesCapacity];
            Disposables = new Stack<IDisposable>();
            _typeMap = typeMap;
            _childFactories = childFactories;
            _scopeTypeMap = scopeTypeMap;
        }

        public object Resolve(int localId) => _factories[localId](this);

        public bool TryGetId(Type type, out int id) => _typeMap.TryGetValue(type, out id);

        public bool TryGetScopeId(Type scopeType, out int scopeId) => _scopeTypeMap.TryGetValue(scopeType, out scopeId);

        public bool TryCreateScope(int scopeTypeId, out IScope scope)
        {
            if (scopeTypeId >= 0 && scopeTypeId < _childFactories.Length)
            {
                var factory = _childFactories[scopeTypeId];
                if (factory != null)
                {
                    scope = factory(this);
                    return true;
                }
            }

            scope = null;
            return false;
        }

        public void Dispose()
        {
            while (Disposables.Count > 0) Disposables.Pop().Dispose();
            Array.Clear(Instances, 0, Instances.Length);
        }
    }
}