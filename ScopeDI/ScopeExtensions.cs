using System;
using ScopeDI.Abstractions;

namespace ScopeDI
{
    public static class ScopeExtensions
    {
        public static T Resolve<T>(this IScope scope)
        {
            // 1. Lookup in the current scope's flat array
            if (scope.TryGetId(typeof(T), out int id))
            {
                return (T)scope.Resolve(id);
            }

            // 2. Fallback to parent scope hierarchy
            if (scope.Parent != null)
            {
                return scope.Parent.Resolve<T>();
            }

            throw new InvalidOperationException($"Type {typeof(T).Name} is not registered in this Scope hierarchy.");
        }

        public static IScope CreateChildScope<TChildScopeMarker>(this IScope scope) where TChildScopeMarker : IScope
        {
            // Map the scope marker type to its contiguous integer ScopeId
            if (scope.TryGetScopeId(typeof(TChildScopeMarker), out int scopeId))
            {
                // Instantiate child scope via direct integer index
                if (scope.TryCreateScope(scopeId, out IScope childScope))
                {
                    return childScope;
                }
            }
            throw new InvalidOperationException($"Child scope {typeof(TChildScopeMarker).Name} is not configured in this hierarchy.");
        }
    }
}
