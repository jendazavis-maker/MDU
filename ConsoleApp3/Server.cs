using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
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
            GameWorld.LoadWorld();
            _listener.Start();
            _isRunning = true;
            Log("Server byl spuštěn na portu " + ((IPEndPoint)_listener.LocalEndpoint).Port);

            while (_isRunning)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    Log($"Nové připojení z {client.Client.RemoteEndPoint}");
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex)
                {
                    Log($"Chyba při přijímání klienta: {ex.Message}");
                }
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

                await writer.WriteLineAsync("Vitej v Dracim Doupeti! Jak se jmenujes, hrdino?");
                string name = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(name)) return;

                currentPlayer = new Player(name.Trim(), writer);
                _activePlayers.Add(currentPlayer);
                Log($"Hráč {currentPlayer.Name} vstoupil do hry.");

                await writer.WriteLineAsync($"\nVitej, {currentPlayer.Name}. Napiš 'pomoc' pro seznam příkazů.");
                LookAround(currentPlayer);

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
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0) return;

            if (parts[0] == "pomoc")
            {
                player.Writer.WriteLine("Příkazy: jdi <směr>, prozkoumej, vezmi <předmět>, utoc <cíl>, inventar, konec");
            }
            else if (parts[0] == "prozkoumej")
            {
                LookAround(player);
            }
            else if (parts[0] == "jdi" && parts.Length > 1)
            {
                MovePlayer(player, parts[1]);
            }
            else if (parts[0] == "vezmi" && parts.Length > 1)
            {
                TakeItem(player, parts[1]);
            }
            else if (parts[0] == "inventar" || parts[0] == "i")
            {
                ShowInventory(player);
            }
            else if (parts[0] == "utoc" && parts.Length > 1)
            {
                AttackNpc(player, parts[1]);
            }
            else if (parts[0] == "konec")
            {
                player.Writer.WriteLine("Sbohem!");
                player.Writer.BaseStream.Close();
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
                player.Writer.WriteLine($"\nJdeš směr: {direction}...");
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
                player.Writer.WriteLine("Předměty na zemi: " + string.Join(", ", room.Items));
            if (room.Npcs.Count > 0)
                player.Writer.WriteLine("Postavy zde: " + string.Join(", ", room.Npcs));
        }

        private void TakeItem(Player player, string itemName)
        {
            Room room = GameWorld.GetRoom(player.CurrentRoomId);

            if (room != null && room.Items.Contains(itemName))
            {
                room.Items.Remove(itemName);
                player.Inventory.Add(itemName);
                player.Writer.WriteLine($"Zvedl jsi ze země: {itemName}");
            }
            else
            {
                player.Writer.WriteLine("Takový předmět tady není.");
            }
        }

        private void ShowInventory(Player player)
        {
            player.Writer.WriteLine($"\n--- INVENTÁŘ ({player.HP} HP) ---");
            if (player.Inventory.Count == 0)
            {
                player.Writer.WriteLine("Máš prázdné kapsy.");
            }
            else
            {
                player.Writer.WriteLine(string.Join(", ", player.Inventory));
            }
        }

        private void AttackNpc(Player player, string npcName)
        {
            Room room = GameWorld.GetRoom(player.CurrentRoomId);

            if (room != null && room.Npcs.Contains(npcName))
            {
                player.Writer.WriteLine($"Útočíš na {npcName}!");

                // Zjednodušená bojová logika
                player.HP -= 10;
                room.Npcs.Remove(npcName);

                player.Writer.WriteLine($"Porazil jsi {npcName}, ale ztratil jsi 10 HP.");
                player.Writer.WriteLine($"Zbývá ti {player.HP} HP.");

                if (player.HP <= 0)
                {
                    player.Writer.WriteLine("ZEMŘEL JSI! Tvoje cesta končí.");
                    player.Writer.BaseStream.Close();
                }
            }
            else
            {
                player.Writer.WriteLine("Nikdo takový tu není.");
            }
        }

        private void Log(string message)
        {
            string logMsg = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Console.WriteLine(logMsg);
            File.AppendAllText(_logFile, logMsg + Environment.NewLine);
        }
    }
}