namespace ScopeDI.Benchmarks.Types
{
    public class Person : IPerson
    {
        private string _name;

        public string Name { get => _name; }


        private static string[] _names = ["Gohan","Goku","Goten"];


        private static Random _random = new Random();

        public Person()
        {
            _name = _names[_random.Next(0, _names.Length)];
        }

        public void PrintStats()
        {
            Console.WriteLine($"Name:{Name}");
        }
    }
}

