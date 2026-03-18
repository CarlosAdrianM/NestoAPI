using System.Collections.Generic;

namespace NestoAPI.Models
{
    public class NotificacionPushDTO
    {
        public string Titulo { get; set; }
        public string Cuerpo { get; set; }
        public string Tipo { get; set; }
        public Dictionary<string, string> Datos { get; set; }
    }
}
