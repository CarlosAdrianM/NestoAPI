using NestoAPI.Models.Rapports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    public interface IServicioRapports
    {
        Task<ICollection<CodigoPostalSeguimientoLookup>> CodigosPostalesSinVisitar(string vendedor, DateTime fechaDesde, DateTime fechaHasta);
        Task<ICollection<ClienteSeguimientoLookup>> ClientesSinVisitar(string vendedor, string codigoPostal, DateTime fechaDesde, DateTime fechaHasta);
    }
}
