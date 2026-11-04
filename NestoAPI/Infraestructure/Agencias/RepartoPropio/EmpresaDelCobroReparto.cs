using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#807: a qué empresa va el cobro del reembolso de un envío del reparto propio y qué movimiento del extracto liquida.
    /// </summary>
    public class CobroEnvioReparto
    {
        /// <summary>La empresa del cobro (recortada). Nunca null.</summary>
        public string Empresa { get; set; }
        /// <summary>
        /// El movimiento pendiente (ExtractoCliente) que liquida el cobro, si la regla lo ha encontrado; null = se busca como siempre
        /// (<c>TramitacionEnviosService</c>, en <see cref="Empresa"/>).
        /// </summary>
        public ExtractoCliente Movimiento { get; set; }
    }

    /// <summary>NestoAPI#807: una factura de un pedido (LinPedidoVta con Nº Factura), con la empresa de la línea.</summary>
    public class FacturaPedidoReparto
    {
        public int Pedido { get; set; }
        public string Empresa { get; set; }
        public string Factura { get; set; }
    }

    /// <summary>
    /// NestoAPI#801/#807: a qué empresa va un cobro del reparto propio. <c>EnviosAgencia.Empresa</c> es SIEMPRE «1» y un pedido de la 1
    /// puede facturarse en la 3 (traspaso a la empresa espejo: factura GB en la 3 con el pedido en la 1), así que ni el envío ni el
    /// pedido valen: manda el EXTRACTO del cliente, dónde está la factura pendiente del pedido (Carlos 04/11/26). Las agencias de fuera
    /// (CTT/GLS) siguen con <c>envio.Empresa</c>, como lo hacía Nesto.
    /// </summary>
    public static class EmpresaDelCobroReparto
    {
        /// <summary>
        /// NestoAPI#807, la regla (pura) del reembolso de un envío (efectivo al entregar, enlace con tarjeta, enlace al entregar y
        /// <c>AdmiteTarjeta</c> de las paradas):
        /// <list type="number">
        /// <item>Las facturas del pedido (<paramref name="facturasPedido"/>, de LinPedidoVta).</item>
        /// <item>El movimiento del extracto del cliente con Nº Documento en esas facturas e ImportePdte distinto de 0, en CUALQUIER
        /// empresa: el de mayor pendiente y, si empatan, el más reciente. Su empresa es la del cobro y es el movimiento que se liquida
        /// (si su signo es el del reembolso).</item>
        /// <item>Si no hay: la empresa de la factura del pedido; si no tiene factura, el pendiente del cliente por importe = reembolso
        /// (cualquier empresa, el más reciente; también se liquida); si no, la empresa del pedido; si no, la del envío; si no, la 1.</item>
        /// </list>
        /// Las listas pueden traer datos de otros pedidos y clientes (la lectura de toda la lista de paradas): se filtran aquí.
        /// </summary>
        public static CobroEnvioReparto DeEnvio(EnviosAgencia envio, IEnumerable<FacturaPedidoReparto> facturasPedido,
            IEnumerable<ExtractoCliente> pendientesCliente, string empresaPedido)
        {
            int? pedido = envio?.Pedido;
            string cliente = envio?.Cliente?.Trim();
            decimal reembolso = envio?.Reembolso ?? 0m;

            List<FacturaPedidoReparto> facturas = pedido.HasValue
                ? (facturasPedido ?? Enumerable.Empty<FacturaPedidoReparto>())
                    .Where(f => f != null && f.Pedido == pedido.Value && Limpia(f.Factura) != null)
                    .ToList()
                : new List<FacturaPedidoReparto>();
            List<ExtractoCliente> pendientes = (pendientesCliente ?? Enumerable.Empty<ExtractoCliente>())
                .Where(e => e != null && e.ImportePdte != 0 && string.Equals(e.Número?.Trim(), cliente, StringComparison.Ordinal)
                    && Limpia(e.Empresa) != null)
                .ToList();

            // Paso 2: la factura pendiente del pedido, en cualquier empresa.
            var documentos = new HashSet<string>(facturas.Select(f => Limpia(f.Factura)), StringComparer.Ordinal);
            ExtractoCliente deLaFactura = pendientes
                .Where(e => documentos.Contains(Limpia(e.Nº_Documento) ?? string.Empty))
                .OrderByDescending(e => e.ImportePdte)
                .ThenByDescending(e => e.Fecha)
                .ThenByDescending(e => e.Nº_Orden)
                .FirstOrDefault();
            if (deLaFactura != null)
            {
                return new CobroEnvioReparto
                {
                    Empresa = Limpia(deLaFactura.Empresa),
                    Movimiento = MismoSigno(deLaFactura.ImportePdte, reembolso) ? deLaFactura : null
                };
            }

            // Paso 3: sin nada pendiente de sus facturas, la empresa de la factura del pedido.
            string empresaFactura = facturas
                .OrderByDescending(f => Limpia(f.Factura), StringComparer.Ordinal)
                .Select(f => Limpia(f.Empresa))
                .FirstOrDefault(e => e != null);
            if (empresaFactura != null)
            {
                return new CobroEnvioReparto { Empresa = empresaFactura };
            }

            // Aún sin facturar: lo pendiente del cliente justo por el reembolso (con el mismo filtro que el efectivo de siempre).
            if (reembolso != 0)
            {
                ExtractoCliente porImporte = pendientes
                    .Where(e => e.ImportePdte == reembolso && EsPendienteNormal(e))
                    .OrderByDescending(e => e.Fecha)
                    .ThenByDescending(e => e.Nº_Orden)
                    .FirstOrDefault();
                if (porImporte != null)
                {
                    return new CobroEnvioReparto { Empresa = Limpia(porImporte.Empresa), Movimiento = porImporte };
                }
            }

            return new CobroEnvioReparto { Empresa = SinExtracto(empresaPedido, envio?.Empresa) };
        }

        /// <summary>El último recurso: la empresa del pedido; si no, la del envío; si no, la 1.</summary>
        public static string SinExtracto(string empresaPedido, string empresaEnvio)
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

        /// <summary><see cref="DeEnvio"/> de un envío, leyendo la base de datos (las mismas tres consultas que la lista).</summary>
        public static async Task<CobroEnvioReparto> DeEnvioAsync(NVEntities db, EnviosAgencia envio)
        {
            if (envio == null)
            {
                return new CobroEnvioReparto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO };
            }
            Dictionary<int, CobroEnvioReparto> cobros = await DeEnviosAsync(db, new[] { envio }).ConfigureAwait(false);
            return cobros.TryGetValue(envio.Numero, out CobroEnvioReparto cobro) ? cobro : new CobroEnvioReparto { Empresa = SinExtracto(null, envio.Empresa) };
        }

        /// <summary>
        /// <see cref="DeEnvio"/> de varios envíos (la lista de paradas) con TRES consultas en total, sin N+1: las facturas de sus
        /// pedidos (LinPedidoVta), la empresa de los pedidos (CabPedidoVta) y lo pendiente de sus clientes que sea de esas facturas o
        /// por el importe de algún reembolso (ExtractoCliente, en cualquier empresa). Por número de envío.
        /// </summary>
        public static async Task<Dictionary<int, CobroEnvioReparto>> DeEnviosAsync(NVEntities db, IReadOnlyCollection<EnviosAgencia> envios)
        {
            var resultado = new Dictionary<int, CobroEnvioReparto>();
            List<EnviosAgencia> lista = (envios ?? new EnviosAgencia[0]).Where(e => e != null).ToList();
            if (lista.Count == 0)
            {
                return resultado;
            }

            List<int> pedidos = lista.Where(e => e.Pedido.HasValue).Select(e => e.Pedido.Value).Distinct().ToList();
            var facturas = new List<FacturaPedidoReparto>();
            var empresasPedido = new Dictionary<int, string>();
            if (pedidos.Count > 0)
            {
                facturas = (await db.LinPedidoVtas
                    .Where(l => pedidos.Contains(l.Número) && l.Nº_Factura != null && l.Nº_Factura != "")
                    .Select(l => new { l.Número, l.Empresa, l.Nº_Factura })
                    .Distinct()
                    .ToListAsync().ConfigureAwait(false))
                    .Select(l => new FacturaPedidoReparto { Pedido = l.Número, Empresa = l.Empresa, Factura = l.Nº_Factura })
                    .ToList();
                var cabeceras = await db.CabPedidoVtas
                    .Where(c => pedidos.Contains(c.Número))
                    .Select(c => new { c.Número, c.Empresa })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var cabecera in cabeceras.Where(c => Limpia(c.Empresa) != null))
                {
                    if (!empresasPedido.ContainsKey(cabecera.Número))
                    {
                        empresasPedido[cabecera.Número] = Limpia(cabecera.Empresa);
                    }
                }
            }

            // Amazon y la tienda online no se liquidan contra el extracto (van por canales externos): ni se leen.
            List<string> clientes = lista
                .Where(e => !string.IsNullOrWhiteSpace(e.Cliente) && !EsClienteEspecial(e.Cliente))
                .Select(e => e.Cliente).Distinct().ToList();
            List<string> documentos = facturas.Select(f => f.Factura).Distinct().ToList();
            List<decimal> importes = lista.Where(e => e.Reembolso != 0).Select(e => e.Reembolso).Distinct().ToList();
            var pendientes = new List<ExtractoCliente>();
            if (clientes.Count > 0 && (documentos.Count > 0 || importes.Count > 0))
            {
                pendientes = await db.ExtractosCliente
                    .Where(e => clientes.Contains(e.Número) && e.ImportePdte != 0
                        && (documentos.Contains(e.Nº_Documento) || importes.Contains(e.ImportePdte)))
                    .ToListAsync().ConfigureAwait(false);
            }

            foreach (EnviosAgencia envio in lista)
            {
                string empresaPedido = envio.Pedido.HasValue && empresasPedido.TryGetValue(envio.Pedido.Value, out string e) ? e : null;
                resultado[envio.Numero] = DeEnvio(envio, facturas,
                    EsClienteEspecial(envio.Cliente) ? Enumerable.Empty<ExtractoCliente>() : pendientes, empresaPedido);
            }
            return resultado;
        }

        private static bool EsClienteEspecial(string cliente)
        {
            string limpio = cliente?.Trim();
            return limpio == Constantes.ClientesEspeciales.AMAZON || limpio == Constantes.ClientesEspeciales.TIENDA_ONLINE;
        }

        /// <summary>Como el efectivo de siempre (TramitacionEnviosService.LeerExtractoPendienteAsync): estado normal y sin cursos (CV).</summary>
        private static bool EsPendienteNormal(ExtractoCliente e)
            => (e.Estado == null || e.Estado.Trim() == Constantes.ExtractosCliente.Estados.NORMAL)
                && !(e.Nº_Documento ?? string.Empty).StartsWith(Constantes.Series.SERIE_CURSOS, StringComparison.Ordinal);

        /// <summary>Un cobro (reembolso positivo) no liquida un abono, ni una devolución una factura. Sin reembolso, vale cualquiera.</summary>
        private static bool MismoSigno(decimal pendiente, decimal reembolso) => reembolso == 0 || Math.Sign(pendiente) == Math.Sign(reembolso);

        private static string Limpia(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
}
