using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Vendedores
{
    public interface IServicioVendedores
    {
        DateTime Fecha { get;set; }
        Task<List<VendedorDTO>> VendedoresEquipo(string empresa, string vendedor);
    }
}
