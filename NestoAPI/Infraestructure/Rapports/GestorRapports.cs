using NestoAPI.Models.Rapports;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    public class GestorRapports
    {
        private IServicioRapports Servicio;
        public GestorRapports()
        {
            Servicio = new ServicioRapports();
        }
        public GestorRapports(IServicioRapports servicio)
        {
            Servicio = servicio;
        }
        public async Task<ICollection<CodigoPostalSeguimientoLookup>> CodigosPostalesSinVisitar(string vendedor, DateTime fechaDesde, DateTime fechaHasta)
        {
            return await Servicio.CodigosPostalesSinVisitar(vendedor, fechaDesde, fechaHasta);
        }

        public async Task<ICollection<ClienteSeguimientoLookup>> ClientesSinVisitar(string vendedor, string codigoPostal, DateTime fechaDesde, DateTime fechaHasta)
        {
            return await Servicio.ClientesSinVisitar(vendedor, codigoPostal, fechaDesde, fechaHasta);
        }
    }
}