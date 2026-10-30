namespace ScopeDI.Abstractions
{
    public interface IScope : IDisposable
    {
        IScope Parent { get; }

        object Resolve(int localId);

        bool TryCreateScope(int scopeTypeId, out IScope scope);

        bool TryGetId(Type type, out int id);

        bool TryGetScopeId(Type scopeType, out int scopeId);
    }
}