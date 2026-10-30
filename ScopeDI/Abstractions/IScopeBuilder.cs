namespace ScopeDI.Abstractions
{
    public interface IScopeBuilder
    {
        IScopeBuilder ParentBuilder { get; }

        int? GetLocalId(Type serviceType);

        Func<IScope, IScope> CompileScopeFactory();
    }
}
