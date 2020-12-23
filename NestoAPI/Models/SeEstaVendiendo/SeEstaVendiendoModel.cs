using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.SeEstaVendiendo
{
    public class SeEstaVendiendoModel
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string RutaImagen { get; set; }
        public string RutaEnlace { get; set; }
    }
}