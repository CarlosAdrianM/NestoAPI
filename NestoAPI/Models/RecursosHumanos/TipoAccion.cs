using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class TipoAccion
    {
        public TipoAccion(int Id, string Nombre, bool EsJornadaLaboral)
        {
            this.Id = Id;
            this.Nombre = Nombre;
            this.EsJornadaLaboral = EsJornadaLaboral;
        }

        public int Id { get; set; }
        public string Nombre { get; set; }
        public bool EsJornadaLaboral { get; set; }
    }
}