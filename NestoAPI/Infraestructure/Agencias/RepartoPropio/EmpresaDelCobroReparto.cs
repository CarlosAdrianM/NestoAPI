using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#801: a qué empresa va un cobro del reparto propio. <c>EnviosAgencia.Empresa</c> es SIEMPRE «1» (también en los pedidos
    /// de la empresa 3), así que no sirve: el 03/11/26 el efectivo del 5825 (pedido 929679, empresa 3) se contabilizó en la 1 y no
    /// liquidó la GB2601735. Las agencias de fuera (CTT/GLS) siguen con <c>envio.Empresa</c>, como lo hacía Nesto.
    /// </summary>
    public static class EmpresaDelCobroReparto
    {
        /// <summary>
        /// El reembolso de un envío (efectivo al entregar y enlace con tarjeta): la empresa del PEDIDO (el número de pedido es único
        /// entre empresas); si el pedido no existe, la del envío; si tampoco, la 1.
        /// </summary>
        public static string DeEnvio(string empresaPedido, string empresaEnvio)
            => Limpia(empresaPedido) ?? Limpia(empresaEnvio) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;

        /// <summary>
        /// Lo «a cuenta» de un cobro de deuda (F7), decisión de Carlos 04/11/26: la empresa de los efectos marcados si todos son de la
        /// misma (de dos empresas, la 1); sin ninguno marcado, la de la última factura pendiente del cliente; sin nada pendiente, la 1.
        /// </summary>
        public static string DeACuenta(IEnumerable<string> empresasEfectosMarcados, string empresaUltimaPendiente)
        {
            List<string> marcadas = (empresasEfectosMarcados ?? Enumerable.Empty<string>())
                .Select(Limpia).Where(e => e != null).Distinct().ToList();
            if (marcadas.Count == 1)
            {
                return marcadas[0];
            }
            if (marcadas.Count > 1)
            {
                return Constantes.Empresas.EMPRESA_POR_DEFECTO;
            }
            return Limpia(empresaUltimaPendiente) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
        }

        /// <summary>La empresa del pedido del envío (<c>CabPedidoVta</c> por <c>Número</c>), o null si no tiene pedido o no existe.</summary>
        public static async Task<string> EmpresaDelPedidoAsync(NVEntities db, EnviosAgencia envio)
        {
            if (envio?.Pedido == null)
            {
                return null;
            }
            int pedido = envio.Pedido.Value;
            List<string> empresas = await db.CabPedidoVtas
                .Where(c => c.Número == pedido)
                .Select(c => c.Empresa)
                .ToListAsync().ConfigureAwait(false);
            return empresas.Select(Limpia).FirstOrDefault(e => e != null);
        }

        /// <summary><see cref="DeEnvio"/> leyendo la empresa del pedido de la base de datos.</summary>
        public static async Task<string> DeEnvioAsync(NVEntities db, EnviosAgencia envio)
            => DeEnvio(await EmpresaDelPedidoAsync(db, envio).ConfigureAwait(false), envio?.Empresa);

        private static string Limpia(string empresa) => string.IsNullOrWhiteSpace(empresa) ? null : empresa.Trim();
    }
}
