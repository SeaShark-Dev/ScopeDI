namespace ScopeDI.Core
{
    public class Blueprint
    {
        public Type ServiceType { get; set; }
        public Type ImplementationType { get; set; }
        public Lifetime Lifetime { get; set; }
        public int LocalId { get; set; }
    }
}
