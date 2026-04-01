using System.Collections.Generic;
using System.IO;

namespace DragonMud
{
    // Třída pro místnost (odpovídá struktuře v JSONu)
    public class Room
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Exits { get; set; } = new Dictionary<string, string>();
        public List<string> Items { get; set; } = new List<string>();
        public List<string> Npcs { get; set; } = new List<string>();
    }

    // Třída reprezentující připojeného hráče
    public class Player
    {
        public string Name { get; set; }
        public string CurrentRoomId { get; set; }
        public StreamWriter Writer { get; set; } // Přes toto mu posíláme text

        public Player(string name, StreamWriter writer)
        {
            Name = name;
            Writer = writer;
            CurrentRoomId = "krcma_start"; // Výchozí místnost
        }
    }
}