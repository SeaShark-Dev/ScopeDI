using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using ScopeDI.Abstractions;

namespace ScopeDI.Core
{
    internal class ScopeCompiler
    {
        private static readonly ParameterExpression ScopeParam = Expression.Parameter(typeof(ExpressionScope), "scope");

        private static readonly FieldInfo InstancesField = typeof(ExpressionScope).GetField("Instances", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DisposablesField = typeof(ExpressionScope).GetField("Disposables", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo PushMethod = typeof(Stack<IDisposable>).GetMethod("Push");
        private static readonly MethodInfo ResolveMethod = typeof(IScope).GetMethod("Resolve");
        private static readonly PropertyInfo ParentProperty = typeof(IScope).GetProperty("Parent");

        private readonly IScopeBuilder _currentBuilder;

        // Tracks active resolution chain to detect circular dependencies at compile time
        private readonly Stack<Type> _buildStack = new();

        public ScopeCompiler(IScopeBuilder builder)
        {
            _currentBuilder = builder;
        }

        public Func<ExpressionScope, object>[] CompileAll()
        {
            var blueprints = GetBlueprints(_currentBuilder);
            var factories = new Func<ExpressionScope, object>[blueprints.Count];

            foreach (var blueprint in blueprints)
            {
                // Clear traversal stack before compiling each root service factory
                _buildStack.Clear();
                factories[blueprint.LocalId] = CompileRoot(blueprint);
            }
            return factories;
        }

        private Func<ExpressionScope, object> CompileRoot(Blueprint blueprint)
        {
            // 1. Build a flattened expression tree for the entire dependency subgraph
            Expression body = BuildExpression(blueprint);

            // 2. Box value types or cast reference types to object for the factory delegate signature
            if (body.Type != typeof(object))
            {
                body = Expression.Convert(body, typeof(object));
            }

            // 3. Compile once upfront using FastExpressionCompiler
            return Expression.Lambda<Func<ExpressionScope, object>>(body, ScopeParam).CompileFast();
        }

        // Recursive dependency graph expression builder
        private Expression BuildExpression(Blueprint blueprint)
        {
            if (_buildStack.Contains(blueprint.ServiceType))
            {
                var cycle = string.Join(" -> ", _buildStack.Reverse().Concat(new[] { blueprint.ServiceType }).Select(t => t.Name));
                throw new InvalidOperationException($"Circular dependency detected: {cycle}");
            }

            _buildStack.Push(blueprint.ServiceType);

            try
            {
                var ctor = blueprint.ImplementationType
                    .GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault() ?? throw new InvalidOperationException($"No public constructor for {blueprint.ImplementationType.Name}");

                // Recursively build expressions for all constructor parameters
                var args = ctor.GetParameters()
                    .Select(p => ResolveArgument(p.ParameterType))
                    .ToArray();

                Expression instantiation = Expression.New(ctor, args);

                // Ensure IDisposable instances are pushed to the cleanup stack without duplicate instantiation
                if (typeof(IDisposable).IsAssignableFrom(blueprint.ImplementationType))
                {
                    var instanceVar = Expression.Variable(blueprint.ImplementationType, "instance");
                    var assign = Expression.Assign(instanceVar, instantiation);
                    var pushCall = Expression.Call(
                        Expression.Field(ScopeParam, DisposablesField),
                        PushMethod,
                        Expression.Convert(instanceVar, typeof(IDisposable)));

                    instantiation = Expression.Block(
                        new[] { instanceVar },
                        assign,
                        pushCall,
                        instanceVar
                    );
                }

                if (blueprint.Lifetime == Lifetime.Transient)
                {
                    // Transient services return their inline instantiation expression directly
                    return instantiation;
                }
                else
                {
                    // Scoped/Singleton services cache the instance in the flat array:
                    // scope.Instances[id] ?? (scope.Instances[id] = new Service())
                    var indexExpr = Expression.Constant(blueprint.LocalId);
                    var arrayAccess = Expression.ArrayAccess(Expression.Field(ScopeParam, InstancesField), indexExpr);

                    var coalesce = Expression.Coalesce(
                        arrayAccess,
                        Expression.Assign(arrayAccess, Expression.Convert(instantiation, typeof(object)))
                    );

                    // Cast back to concrete implementation type for parent constructor injection
                    return Expression.Convert(coalesce, blueprint.ImplementationType);
                }
            }
            finally
            {
                _buildStack.Pop();
            }
        }

        private Expression ResolveArgument(Type type)
        {
            var targetBuilder = _currentBuilder;
            int depth = 0;
            Blueprint targetBlueprint = null;

            // Search for the service blueprint in the current scope or parent hierarchy
            while (targetBuilder != null)
            {
                targetBlueprint = GetBlueprints(targetBuilder).FirstOrDefault(b => b.ServiceType == type);
                if (targetBlueprint != null) break;

                targetBuilder = targetBuilder.ParentBuilder;
                depth++;
            }

            if (targetBlueprint == null)
                throw new InvalidOperationException($"Type {type.Name} is not registered in the scope hierarchy.");

            if (depth == 0)
            {
                // Dependency belongs to the current scope — inline its expression directly into the graph
                return BuildExpression(targetBlueprint);
            }
            else
            {
                // Dependency belongs to an ancestor scope — traverse Parent links to preserve lifetime boundaries
                Expression targetScope = ScopeParam;
                for (int i = 0; i < depth; i++)
                {
                    targetScope = Expression.Property(targetScope, ParentProperty);
                }

                var resolveCall = Expression.Call(targetScope, ResolveMethod, Expression.Constant(targetBlueprint.LocalId));
                return Expression.Convert(resolveCall, type);
            }
        }

        private static IReadOnlyList<Blueprint> GetBlueprints(IScopeBuilder builder)
        {
            var prop = builder.GetType().GetProperty("Blueprints", BindingFlags.Public | BindingFlags.Instance);
            return (IReadOnlyList<Blueprint>)prop.GetValue(builder);
        }
    }
}
