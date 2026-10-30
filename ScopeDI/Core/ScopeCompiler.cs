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

        // Стек для защиты от StackOverflow при циклических зависимостях
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
                // Очищаем стек перед сборкой каждого корневого сервиса
                _buildStack.Clear();
                factories[blueprint.LocalId] = CompileRoot(blueprint);
            }
            return factories;
        }

        private Func<ExpressionScope, object> CompileRoot(Blueprint blueprint)
        {
            // 1. Строим огромное плоское выражение для всего графа
            Expression body = BuildExpression(blueprint);

            // 2. Оборачиваем результат в object (т.к. Func возвращает object)
            if (body.Type != typeof(object))
            {
                body = Expression.Convert(body, typeof(object));
            }

            // 3. Компилируем ОДИН РАЗ
            return Expression.Lambda<Func<ExpressionScope, object>>(body, ScopeParam).CompileFast();
        }

        // РЕКУРСИВНЫЙ МЕТОД ПОСТРОЕНИЯ ГРАФА
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

                // Рекурсивно собираем аргументы!
                var args = ctor.GetParameters()
                    .Select(p => ResolveArgument(p.ParameterType))
                    .ToArray();

                Expression instantiation = Expression.New(ctor, args);

                // ИСПРАВЛЕНИЕ БАГА IDisposable: используем локальную переменную, чтобы не вызывать new дважды
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
                        instanceVar // Возвращаем переменную
                    );
                }

                if (blueprint.Lifetime == Lifetime.Transient)
                {
                    // Для транзиентов просто возвращаем инлайн-создание
                    return instantiation;
                }
                else
                {
                    // Для Scoped/Singleton: scope.Instances[id] ?? (scope.Instances[id] = new Service())
                    var indexExpr = Expression.Constant(blueprint.LocalId);
                    var arrayAccess = Expression.ArrayAccess(Expression.Field(ScopeParam, InstancesField), indexExpr);

                    var coalesce = Expression.Coalesce(
                        arrayAccess,
                        Expression.Assign(arrayAccess, Expression.Convert(instantiation, typeof(object)))
                    );

                    // Кастуем обратно к типу сервиса, чтобы не сломать родительский конструктор
                    return Expression.Convert(coalesce, blueprint.ImplementationType);
                }
            }
            finally
            {
                // Убираем тип из стека после успешной сборки
                _buildStack.Pop();
            }
        }

        private Expression ResolveArgument(Type type)
        {
            var targetBuilder = _currentBuilder;
            int depth = 0;
            Blueprint targetBlueprint = null;

            // Ищем блюпринт в текущем скоупе или у родителей
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
                // Зависимость находится в ТЕКУЩЕМ скоупе. 
                // Безупречно расплющиваем её прямо в этот же граф!
                return BuildExpression(targetBlueprint);
            }
            else
            {
                // Зависимость находится в РОДИТЕЛЬСКОМ скоупе (например, Singleton запрошен из Scope).
                // Мы не можем её заинлайнить, иначе нарушим границы памяти. 
                // Здесь оставляем старый добрый виртуальный вызов Resolve.
                Expression targetScope = ScopeParam;
                for (int i = 0; i < depth; i++)
                {
                    targetScope = Expression.Property(targetScope, ParentProperty);
                }

                var resolveCall = Expression.Call(targetScope, ResolveMethod, Expression.Constant(targetBlueprint.LocalId));
                return Expression.Convert(resolveCall, type);
            }
        }

        // Хелпер для безопасного получения списка Blueprints (замена dynamic)
        private static IReadOnlyList<Blueprint> GetBlueprints(IScopeBuilder builder)
        {
            // Поскольку у нас дженерик ScopeBuilder<TScope>, достаем свойство через рефлексию
            var prop = builder.GetType().GetProperty("Blueprints", BindingFlags.Public | BindingFlags.Instance);
            return (IReadOnlyList<Blueprint>)prop.GetValue(builder);
        }
    }
}