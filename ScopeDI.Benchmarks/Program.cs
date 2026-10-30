using BenchmarkDotNet.Running;
using ScopeDI.Abstractions;

namespace ScopeDI.Benchmarks
{
    public interface IRootScope : IScope { }
    public interface IMatchScope : IScope { }

    public interface IDatabase { void Query(); }
    public class PostgresDatabase : IDatabase
    {
        public PostgresDatabase() => Console.WriteLine("  [new] PostgresDatabase (Root)");
        public void Query() => Console.WriteLine("  -> Querying DB...");
    }

    public interface IMatchState { string MatchId { get; } }
    public class MatchState : IMatchState
    {
        public string MatchId { get; } = Guid.NewGuid().ToString().Substring(0, 8);
        public MatchState() => Console.WriteLine($"  [new] MatchState created: {MatchId}");
    }

    public interface IPlayerManager { void Join(); }
    public class PlayerManager : IPlayerManager
    {
        private readonly IDatabase _db;
        private readonly IMatchState _state;

        public PlayerManager(IDatabase db, IMatchState state)
        {
            _db = db;
            _state = state;
            Console.WriteLine("  [new] PlayerManager initialized.");
        }

        public void Join()
        {
            Console.WriteLine($"  -> Player joined match {_state.MatchId}");
            _db.Query();
        }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
