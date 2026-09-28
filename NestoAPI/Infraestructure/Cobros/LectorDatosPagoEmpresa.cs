using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using System.Linq;

namespace NestoAPI.Infraestructure.Cobros
{
    /// <summary>
    /// El único sitio que decide a qué cuenta y a nombre de quién nos transfiere un cliente: el aviso
    /// de facturas vencidas (NestoAPI#544) y los datos de transferencia del pedido prepago
    /// (sugerencia 396 de Novedades) leen de aquí.
    /// </summary>
    public interface ILectorDatosPagoEmpresa
    {
        /// <summary>IBAN (tabla Bancos, varias cuentas en una frase) y titular (Empresas.Nombre) de la empresa.</summary>
        DatosPagoAviso Leer(string empresa);
    }

    public class LectorDatosPagoEmpresa : ILectorDatosPagoEmpresa
    {
        private readonly NVEntities db;
        private readonly ServicioFacturas servicioFacturas;

        public LectorDatosPagoEmpresa(NVEntities db, ServicioFacturas servicioFacturas = null)
        {
            this.db = db;
            this.servicioFacturas = servicioFacturas ?? new ServicioFacturas(db);
        }

        public DatosPagoAviso Leer(string empresa)
        {
            return new DatosPagoAviso
            {
                Iban = AvisosFacturasVencidasJobsService.NormalizarIban(servicioFacturas.CuentaBancoEmpresa(empresa)),
                Titular = db.Empresas.Where(e => e.Número == empresa).Select(e => e.Nombre).FirstOrDefault()?.Trim()
            };
        }
    }
}
