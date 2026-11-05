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
        /// <summary>
        /// NestoAPI#822: la aplicación para la que se grabó (Nesto, Ariadna, NestoApp). El buzón de los empleados es uno solo
        /// para las tres; el cliente decide si puede abrir lo que trae (p. ej. un pedido solo se abre en Nesto).
        /// </summary>
        public string Aplicacion { get; set; }
    }
}
