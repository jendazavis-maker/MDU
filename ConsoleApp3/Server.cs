using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.Json;

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
            string saveFilePath = "";

            try
            {
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                await writer.WriteLineAsync("Vitej v Dracim Doupeti! Zadej sve jmeno:");
                string name = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();

                // Zajištění, že složka pro uložení existuje
                Directory.CreateDirectory("saves");
                saveFilePath = $"saves/{name.ToLower()}.json";
                PlayerSaveData saveData;

                // LOGIKA PŘIHLAŠOVÁNÍ A REGISTRACE
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
                        return; // Ukončí spojení
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

                // Vytvoření hráče v paměti na základě načtených/nových dat
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

                // Hlavní herní smyčka
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

                    // ULOŽENÍ STAVU PŘI ODPOJENÍ
                    SavePlayer(currentPlayer, saveFilePath);
                }
                client.Close();
            }
        }

        // Metoda pro bezpečné hashování hesla (bod I3 - hesla nesmí být v čistém textu)
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        // Metoda pro uložení stavu hráče do souboru
        private void SavePlayer(Player player, string filePath)
        {
            PlayerSaveData data = new PlayerSaveData
            {
                Name = player.Name,
                PasswordHash = player.PasswordHash,
                CurrentRoomId = player.CurrentRoomId,
                HP = player.HP,
                Attack = player.Attack,
                Inventory = player.Inventory
            };

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            Log($"Stav hráče {player.Name} byl úspěšně uložen.");
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

                // WIN CONDITION - Zabití draka
                if (npcName.ToLower() == "drak")
                {
                    player.Writer.WriteLine("\n===============================================");
                    player.Writer.WriteLine("🎉 GRATULUJI! Zabil jsi prastarého draka!");
                    player.Writer.WriteLine("Získal jsi obrovský poklad a DOKONČIL JSI HRU!");
                    player.Writer.WriteLine("===============================================\n");

                    // Uložení do statistik podle požadavku P1
                    File.AppendAllText("vyherci.txt", $"[{DateTime.Now:yyyy-MM-dd HH:mm}] Hráč {player.Name} úspěšně porazil draka a dohrál hru!\n");

                    player.Writer.BaseStream.Close(); // Odpojíme hráče (konec hry)
                    return; // Ukončíme metodu, aby se už nevypsal běžný text o boji
                }

                // Běžný boj s ostatními monstry (např. goblin)
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