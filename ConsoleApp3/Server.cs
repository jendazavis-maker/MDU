using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace DragonMud
{
    public class Server
    {
        private TcpListener _listener;
        private bool _isRunning;
        private List<Player> _activePlayers = new List<Player>();
        private readonly string _logFile = "server_log.txt";

        public Server(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public async Task StartAsync()
        {
            GameWorld.LoadWorld(); // Načteme mapu
            _listener.Start();
            _isRunning = true;
            Log("Server byl spuštěn.");

            while (_isRunning)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync();
                Log($"Nové připojení z {client.Client.RemoteEndPoint}");
                _ = HandleClientAsync(client); // Fire and forget
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            Player currentPlayer = null;
            try
            {
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                await writer.WriteLineAsync("Vitej v Dрачиm Doupeti! Jak se jmenujes hrdino?");
                string name = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(name)) return;

                currentPlayer = new Player(name.Trim(), writer);
                _activePlayers.Add(currentPlayer);
                Log($"Hráč {currentPlayer.Name} vstoupil do hry.");

                await writer.WriteLineAsync($"Vitej, {currentPlayer.Name}. Napiš 'pomoc' pro seznam příkazů.");
                LookAround(currentPlayer); // Rozhlédne se po přihlášení

                while (client.Connected)
                {
                    string input = await reader.ReadLineAsync();
                    if (input == null) break;

                    ProcessCommand(currentPlayer, input.Trim().ToLower());
                }
            }
            catch (Exception ex)
            {
                Log($"Chyba s klientem: {ex.Message}");
            }
            finally
            {
                if (currentPlayer != null)
                {
                    _activePlayers.Remove(currentPlayer);
                    Log($"Hráč {currentPlayer.Name} se odpojil.");
                }
                client.Close();
            }
        }

        private void ProcessCommand(Player player, string command)
        {
            Log($"[{player.Name}] zadal: {command}");
            string[] parts = command.Split(' ');

            if (parts[0] == "pomoc")
            {
                player.Writer.WriteLine("Příkazy: jdi <směr>, prozkoumej, konec");
            }
            else if (parts[0] == "prozkoumej")
            {
                LookAround(player);
            }
            else if (parts[0] == "jdi" && parts.Length > 1)
            {
                MovePlayer(player, parts[1]);
            }
            else if (parts[0] == "konec")
            {
                player.Writer.WriteLine("Sbohem!");
                player.Writer.BaseStream.Close(); // Ukončí spojení
            }
            else
            {
                player.Writer.WriteLine("Neznámý příkaz.");
            }
        }

        private void MovePlayer(Player player, string direction)
        {
            Room currentRoom = GameWorld.GetRoom(player.CurrentRoomId);
            if (currentRoom != null && currentRoom.Exits.ContainsKey(direction))
            {
                player.CurrentRoomId = currentRoom.Exits[direction];
                player.Writer.WriteLine($"Jdeš na {direction}...");
                LookAround(player);
            }
            else
            {
                player.Writer.WriteLine("Tímto směrem jít nemůžeš.");
            }
        }

        private void LookAround(Player player)
        {
            Room room = GameWorld.GetRoom(player.CurrentRoomId);
            if (room == null) return;

            player.Writer.WriteLine($"\n--- {room.Name} ---");
            player.Writer.WriteLine(room.Description);
            player.Writer.WriteLine("Východy: " + string.Join(", ", room.Exits.Keys));

            if (room.Items.Count > 0)
                player.Writer.WriteLine("Předměty: " + string.Join(", ", room.Items));
            if (room.Npcs.Count > 0)
                player.Writer.WriteLine("Postavy: " + string.Join(", ", room.Npcs));
        }

        private void Log(string message)
        {
            string logMsg = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Console.WriteLine(logMsg);
            File.AppendAllText(_logFile, logMsg + Environment.NewLine);
        }
    }
}