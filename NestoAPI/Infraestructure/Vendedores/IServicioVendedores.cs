using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Vendedores
{
    public interface IServicioVendedores
    {
        Task<List<VendedorDTO>> VendedoresEquipo(string empresa, string vendedor);
    }
}
