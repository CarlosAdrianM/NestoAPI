using System;
using System.Collections.Generic;

namespace NestoAPI.Models
{
    /// <summary>
    /// Una notificación tal y como la ve la app en su buzón (#387). Los datos viajan ya
    /// deserializados para que el cliente navegue igual que al tocar la push del sistema.
    /// </summary>
    public class NotificacionBuzonDTO
    {
        public int Id { get; set; }
        public string Titulo { get; set; }
        public string Cuerpo { get; set; }
        public Dictionary<string, string> Datos { get; set; }
        public DateTime FechaCreacion { get; set; }
        public bool Leida { get; set; }
    }
}
