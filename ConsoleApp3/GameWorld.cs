using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DragonMud
{
    public static class GameWorld
    {
        public static Dictionary<string, Room> Rooms { get; private set; } = new Dictionary<string, Room>();

        public static void LoadWorld()
        {
            try
            {
                string jsonString = File.ReadAllText("rooms.json");
                var roomList = JsonSerializer.Deserialize<List<Room>>(jsonString);

                foreach (var room in roomList)
                {
                    Rooms[room.Id] = room;
                }
                Console.WriteLine("Herní svět úspěšně načten z JSONu.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chyba při načítání světa: {ex.Message}");
            }
        }

        public static Room GetRoom(string roomId)
        {
            if (Rooms.TryGetValue(roomId, out Room room))
                return room;
            return null;
        }
    }
}