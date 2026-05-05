using System;
using System.Threading.Tasks;

namespace DragonMud
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Spouštím Dragon MUD Server...");
            Server server = new Server(8888);
            await server.StartAsync();
        }
    }
}