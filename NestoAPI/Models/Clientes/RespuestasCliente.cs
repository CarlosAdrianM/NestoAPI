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
        public string NumeroCliente { get; set; }
        public int EstadoCliente { get; set; }
    }

    public class RespuestaDatosGeneralesClientes
    {
        public string CodigoPostal { get; set; }
        public string DireccionFormateada { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Ruta { get; set; }
        public string TelefonoFormateado { get; set; }
        public string VendedorEstetica { get; set; }
        public string VendedorPeluqueria { get; set; }
    }

    public class RespuestaDatosBancoCliente
    {
        public bool DatosPagoValidos { get; set; }
        public bool IbanValido { get; set; }
        public string Iban { get; set; }
        public string IbanFormateado { get; set; }
    }
}