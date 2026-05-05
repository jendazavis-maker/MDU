using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DragonMud.Client
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "Dragon MUD - Klientská aplikace";

            // Podle zadání by IP a Port měly být v configu klienta, 
            // pro zjednodušení si je tu teď definujeme rovnou.
            string serverIp = "127.0.0.1"; // Localhost
            int port = 8888;

            Console.WriteLine($"Připojuji se k serveru {serverIp}:{port}...");

            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(serverIp, port);
                Console.WriteLine("Úspěšně připojeno k serveru!\n");

                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                // Spustíme dvě asynchronní úlohy: 
                // Jedna čte zprávy ze serveru, druhá odesílá příkazy od hráče.
                Task readTask = ReadFromServerAsync(reader);
                Task writeTask = WriteToServerAsync(writer);

                // Čekáme, dokud jedna z úloh neskončí (např. server se odpojí)
                await Task.WhenAny(readTask, writeTask);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nChyba připojení: {ex.Message}");
            }

            Console.WriteLine("Stiskni cokoliv pro ukončení...");
            Console.ReadKey();
        }

        static async Task ReadFromServerAsync(StreamReader reader)
        {
            try
            {
                while (true)
                {
                    string message = await reader.ReadLineAsync();
                    if (message == null)
                    {
                        Console.WriteLine("\n[Server ukončil spojení]");
                        break;
                    }
                    Console.WriteLine(message);
                }
            }
            catch
            {
                // Ignorujeme chyby při násilném odpojení
            }
        }

        static async Task WriteToServerAsync(StreamWriter writer)
        {
            try
            {
                while (true)
                {
                    string input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input)) continue;

                    await writer.WriteLineAsync(input);

                    if (input.ToLower() == "konec") break;
                }
            }
            catch
            {
                // Ignorujeme chyby při násilném odpojení
            }
        }
    }
}