using System;
using ScopeDI.Abstractions;

namespace ScopeDI
{
    public static class ScopeExtensions
    {
        public static T Resolve<T>(this IScope scope)
        {
            // 1. Ищем в текущем массиве
            if (scope.TryGetId(typeof(T), out int id))
            {
                return (T)scope.Resolve(id);
            }

            // 2. Fallback на родителя
            if (scope.Parent != null)
            {
                return scope.Parent.Resolve<T>();
            }

            throw new InvalidOperationException($"Type {typeof(T).Name} is not registered in this Scope hierarchy.");
        }

        public static IScope CreateChildScope<TChildScopeMarker>(this IScope scope) where TChildScopeMarker : IScope
        {
            // Используем дженерик только для маппинга в ID
            if (scope.TryGetScopeId(typeof(TChildScopeMarker), out int scopeId))
            {
                // Передаем чистый int
                if (scope.TryCreateScope(scopeId, out IScope childScope))
                {
                    return childScope; // Возвращаем базовый интерфейс
                }
            }
            throw new InvalidOperationException($"Child scope {typeof(TChildScopeMarker).Name} is not configured in this hierarchy.");
        }
    }
}