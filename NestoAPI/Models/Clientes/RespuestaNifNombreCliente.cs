using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Clientes
{
    public class RespuestaNifNombreCliente
    {
        public bool NifValidado { get; set; }
        public string NifFormateado { get; set; }
        public string NombreFormateado { get; set; }
        public bool ExisteElCliente { get; set; }
        public int EstadoCliente { get; set; }
    }
}