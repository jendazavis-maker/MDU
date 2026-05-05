using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DragonMud
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Spouštím Dragon MUD Server...");

            try
            {
                // Načtení portu z konfiguračního souboru
                string configJson = File.ReadAllText("server_config.json");
                using JsonDocument config = JsonDocument.Parse(configJson);
                int port = config.RootElement.GetProperty("Port").GetInt32();

                Console.WriteLine($"Konfigurace načtena. Startuji na portu {port}...");

                // Spuštění serveru s portem z configu
                Server server = new Server(port);
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kritická chyba při startu serveru: {ex.Message}");
            }
        }
    }
}