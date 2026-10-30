namespace ScopeDI.Benchmarks.Types
{
    public class User : IUser
    {
        private IPerson _person;

        private static int _counter = 0;

        private int _id;

        public User(IPerson person)
        {
            _person = person;
            _id = _counter;
            _counter++;
        }

        public IPerson Person()
        {
            return _person;
        }

        public void PrintStats()
        {
            Console.WriteLine($"<User:{_id }, {_person.Name }> : Привет из Expression!");
        }
    }
}

