using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Domiciliaciones
{
    public class DomiciliacionesCliente
    {
        public DomiciliacionesCliente()
        {
            ListaEfectos = new List<EfectoDomiciliado>();
        }
        public string Correo { get; set; }
        public List<EfectoDomiciliado> ListaEfectos { get; }
    }
}