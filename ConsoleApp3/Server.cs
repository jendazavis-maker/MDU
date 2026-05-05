using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

                await writer.WriteLineAsync("Vitej v Dracim Doupeti! Zadej sve jmeno:");
                string name = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();

                Directory.CreateDirectory("saves");
                string saveFilePath = $"saves/{name.ToLower()}.json";
                PlayerSaveData saveData;

                if (File.Exists(saveFilePath))
                {
                    await writer.WriteLineAsync("Ucet nalezen. Zadej heslo:");
                    string password = await reader.ReadLineAsync();
                    string hashedInput = HashPassword(password);

                    string json = File.ReadAllText(saveFilePath);
                    saveData = JsonSerializer.Deserialize<PlayerSaveData>(json);

                    if (saveData.PasswordHash != hashedInput)
                    {
                        await writer.WriteLineAsync("Spatne heslo! Spojeni ukonceno.");
                        return;
                    }
                    await writer.WriteLineAsync("Uspesne prihlasen! Vitej zpet.");
                }
                else
                {
                    await writer.WriteLineAsync("Novy hrac! Zadej nove heslo pro vytvoreni uctu:");
                    string password = await reader.ReadLineAsync();

                    saveData = new PlayerSaveData
                    {
                        Name = name,
                        PasswordHash = HashPassword(password),
                        CurrentRoomId = "krcma_start",
                        HP = 100,
                        Attack = 15,
                        Inventory = new List<string>()
                    };
                    await writer.WriteLineAsync("Ucet vytvoren. Vitej ve hre!");
                }

                currentPlayer = new Player(saveData.Name, writer)
                {
                    PasswordHash = saveData.PasswordHash,
                    CurrentRoomId = saveData.CurrentRoomId,
                    HP = saveData.HP,
                    Attack = saveData.Attack,
                    Inventory = saveData.Inventory
                };

                _activePlayers.Add(currentPlayer);
                Log($"Hráč {currentPlayer.Name} vstoupil do hry.");

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
                    SavePlayer(currentPlayer);
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
                player.Writer.WriteLine("Příkazy: jdi <směr>, prozkoumej, vezmi <předmět>, pouzij <předmět>, utoc <cíl>, inventar, rekni <zprava>, krik <zprava>, uloz, konec");
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
            else if (parts[0] == "pouzij" && parts.Length > 1)
            {
                UseItem(player, parts[1]);
            }
            else if (parts[0] == "inventar" || parts[0] == "i")
            {
                ShowInventory(player);
            }
            else if (parts[0] == "utoc" && parts.Length > 1)
            {
                AttackTarget(player, parts[1]);
            }
            else if (parts[0] == "rekni" && parts.Length > 1)
            {
                string message = command.Substring(command.IndexOf(' ') + 1);
                Say(player, message);
            }
            else if (parts[0] == "krik" && parts.Length > 1)
            {
                string message = command.Substring(command.IndexOf(' ') + 1);
                Shout(player, message);
            }
            else if (parts[0] == "uloz")
            {
                SavePlayer(player);
                player.Writer.WriteLine("Hra byla úspěšně uložena.");
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
                string oldRoomId = player.CurrentRoomId;
                string newRoomId = currentRoom.Exits[direction];

                // M11 – Mechanika zamčených místností
                if (newRoomId == "draci_doupe" && !player.Inventory.Contains("klic_k_drakovi"))
                {
                    player.Writer.WriteLine("\n[!] Dveře do doupěte jsou zamčené. Potřebuješ 'klic_k_drakovi'!");
                    return;
                }

                NotifyRoom(oldRoomId, player, $"{player.Name} odešel směrem na {direction}.");

                player.CurrentRoomId = newRoomId;
                player.Writer.WriteLine($"\nJdeš směr: {direction}...");
                LookAround(player);

                NotifyRoom(newRoomId, player, $"{player.Name} právě přišel.");
                SavePlayer(player);
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

            List<string> otherPlayersInRoom = new List<string>();
            foreach (var p in _activePlayers)
            {
                if (p.CurrentRoomId == player.CurrentRoomId && p.Name != player.Name)
                {
                    otherPlayersInRoom.Add(p.Name);
                }
            }

            if (otherPlayersInRoom.Count > 0)
            {
                player.Writer.WriteLine("Ostatní hráči zde: " + string.Join(", ", otherPlayersInRoom));
            }
        }

        private void TakeItem(Player player, string itemName)
        {
            Room room = GameWorld.GetRoom(player.CurrentRoomId);

            if (room != null && room.Items.Contains(itemName))
            {
                room.Items.Remove(itemName);
                player.Inventory.Add(itemName);
                player.Writer.WriteLine($"Zvedl jsi ze země: {itemName}");
                SavePlayer(player);
            }
            else
            {
                player.Writer.WriteLine("Takový předmět tady není.");
            }
        }

        // M8 – Mechanika používání předmětů
        private void UseItem(Player player, string itemName)
        {
            if (!player.Inventory.Contains(itemName))
            {
                player.Writer.WriteLine("Takový předmět v inventáři nemáš.");
                return;
            }

            if (itemName == "maly_lektvar")
            {
                player.HP = Math.Min(100, player.HP + 40);
                player.Inventory.Remove(itemName);
                player.Writer.WriteLine($"Vypil jsi lektvar. Tvé zdraví je nyní {player.HP} HP.");
                SavePlayer(player);
            }
            else if (itemName == "klic_k_drakovi")
            {
                player.Writer.WriteLine("Tento klíč se používá automaticky u zamčených dveří.");
            }
            else
            {
                player.Writer.WriteLine("Tento předmět neumíš použít.");
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

        // M2 & M3 – Mechanika souboje (NPC i Hráči)
        private void AttackTarget(Player player, string targetName)
        {
            Room room = GameWorld.GetRoom(player.CurrentRoomId);

            // Nejdřív zkusíme, jestli útočí na jiného HRÁČE (M3)
            Player targetPlayer = _activePlayers.Find(p => p.Name.ToLower() == targetName.ToLower() && p.CurrentRoomId == player.CurrentRoomId);

            if (targetPlayer != null && targetPlayer != player)
            {
                player.Writer.WriteLine($"Útočíš na hráče {targetPlayer.Name}!");
                targetPlayer.Writer.WriteLine($"\n[!!!] {player.Name} na tebe ZAÚTOČIL!");

                targetPlayer.HP -= player.Attack;
                player.HP -= (targetPlayer.Attack / 2); // Protiútok hráče

                player.Writer.WriteLine($"Způsobil jsi mu zranění. Máš {player.HP} HP, on má {targetPlayer.HP} HP.");
                targetPlayer.Writer.WriteLine($"Ztratil jsi životy. Máš {targetPlayer.HP} HP.");

                if (targetPlayer.HP <= 0)
                {
                    targetPlayer.Writer.WriteLine("ZEMŘEL JSI v souboji! Resetuji tvou pozici do krčmy.");
                    player.Writer.WriteLine($"Porazil jsi hráče {targetPlayer.Name}!");
                    targetPlayer.HP = 100;
                    targetPlayer.CurrentRoomId = "krcma_start";
                }
                SavePlayer(player);
                SavePlayer(targetPlayer);
                return;
            }

            // Pokud to není hráč, zkusíme NPC (M2)
            if (room != null && room.Npcs.Contains(targetName))
            {
                player.Writer.WriteLine($"Útočíš na {targetName}!");
                player.HP -= 10;
                room.Npcs.Remove(targetName);

                if (targetName.ToLower() == "drak")
                {
                    player.Writer.WriteLine("\n===============================================");
                    player.Writer.WriteLine("🎉 GRATULUJI! Zabil jsi prastarého draka!");
                    player.Writer.WriteLine("Získal jsi obrovský poklad a DOKONČIL JSI HRU!");
                    player.Writer.WriteLine("===============================================\n");
                    File.AppendAllText("vyherci.txt", $"[{DateTime.Now:yyyy-MM-dd HH:mm}] Hráč {player.Name} vyhrál!\n");
                    player.Writer.BaseStream.Close();
                    return;
                }

                player.Writer.WriteLine($"Porazil jsi {targetName}, ale ztratil jsi 10 HP.");
                SavePlayer(player);
            }
            else
            {
                player.Writer.WriteLine("Nikdo takový tu není.");
            }
        }

        private void Say(Player sender, string message)
        {
            sender.Writer.WriteLine($"Říkáš: {message}");
            foreach (var p in _activePlayers)
            {
                if (p.CurrentRoomId == sender.CurrentRoomId && p.Name != sender.Name)
                {
                    p.Writer.WriteLine($"\n[{sender.Name} říká]: {message}");
                }
            }
        }

        private void Shout(Player sender, string message)
        {
            sender.Writer.WriteLine($"Křičíš: {message}");
            foreach (var p in _activePlayers)
            {
                if (p.Name != sender.Name)
                {
                    p.Writer.WriteLine($"\n[{sender.Name} KŘIČÍ]: {message}");
                }
            }
        }

        private void NotifyRoom(string roomId, Player excludePlayer, string message)
        {
            foreach (var p in _activePlayers)
            {
                if (p.CurrentRoomId == roomId && p.Name != excludePlayer.Name)
                {
                    p.Writer.WriteLine($"\n*** {message} ***");
                }
            }
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                    builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private void SavePlayer(Player player)
        {
            string filePath = $"saves/{player.Name.ToLower()}.json";
            Directory.CreateDirectory("saves");
            PlayerSaveData data = new PlayerSaveData
            {
                Name = player.Name,
                PasswordHash = player.PasswordHash,
                CurrentRoomId = player.CurrentRoomId,
                HP = player.HP,
                Attack = player.Attack,
                Inventory = player.Inventory
            };
            File.WriteAllText(filePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void Log(string message)
        {
            string logMsg = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Console.WriteLine(logMsg);
            File.AppendAllText(_logFile, logMsg + Environment.NewLine);
        }
    }
}