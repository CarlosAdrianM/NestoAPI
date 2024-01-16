using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Vendedores
{
    public interface IServicioVendedores
    {
        DateTime Fecha { get;set; }
        Task<VendedorDTO> JefeEquipo(string empresa, string vendedor);
        Task<List<VendedorDTO>> VendedoresEquipo(string empresa, string vendedor);
        Task<List<string>> VendedoresEquipoString(string empresa, string vendedor);
    }
}
