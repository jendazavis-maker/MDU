using System.Collections.Generic;
using System.IO;

namespace DragonMud
{
    public class Room
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Exits { get; set; } = new Dictionary<string, string>();
        public List<string> Items { get; set; } = new List<string>();
        public List<string> Npcs { get; set; } = new List<string>();
    }

    public class Player
    {
        public string Name { get; set; }
        public string PasswordHash { get; set; }
        public string CurrentRoomId { get; set; }
        public StreamWriter Writer { get; set; }

        public int HP { get; set; } = 100;
        public int Attack { get; set; } = 15;
        public List<string> Inventory { get; set; } = new List<string>();

        public Player(string name, StreamWriter writer)
        {
            Name = name;
            Writer = writer;
            CurrentRoomId = "krcma_start";
        }
    }

    public class PlayerSaveData
    {
        public string Name { get; set; }
        public string PasswordHash { get; set; }
        public string CurrentRoomId { get; set; }
        public int HP { get; set; }
        public int Attack { get; set; }
        public List<string> Inventory { get; set; }
    }
}