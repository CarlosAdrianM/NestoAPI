using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#815: LA REGLA «una factura contra reembolso cuyo pedido aún no se ha entregado no es deuda vencida».
    /// Con el reembolso diferido (#742) el reembolso se contabiliza cuando la agencia entrega, así que hasta entonces la
    /// factura figura pendiente (y vencida, si era de contado). De una lista de facturas devuelve las que son de un pedido
    /// con un envío cuyo reembolso diferido sigue sin contabilizar (<c>EnviosAgenciaReembolsosAlEntregar</c> con Asiento
    /// NULL → <c>EnviosAgencia.Pedido</c> → <c>LinPedidoVta.Nº_Factura</c> = <c>ExtractoCliente.Nº_Documento</c>), con su
    /// pedido. Sin la tabla de #742 (o sin envíos pendientes) no hay ninguna y nada cambia. Dos consultas como mucho.
    /// <para>La usan el prepago del picking (<c>RellenadorPrepagosService</c>) y la cartera vencida de
    /// <c>PlazosPagoController</c> (plazos y formas de pago que se ofrecen).</para>
    /// </summary>
    public interface IFacturasConReembolsoPendienteDeEntrega
    {
        /// <summary>Factura (sin espacios) → pedido del envío con el reembolso pendiente. Nunca null.</summary>
        IDictionary<string, int> Leer(IEnumerable<string> facturas);
    }

    public class FacturasConReembolsoPendienteDeEntrega : IFacturasConReembolsoPendienteDeEntrega
    {
        private readonly NVEntities db;
        private readonly IRegistroReembolsosAlEntregar registro;

        public FacturasConReembolsoPendienteDeEntrega(NVEntities db) : this(db, new RegistroReembolsosAlEntregar(db))
        {
        }

        public FacturasConReembolsoPendienteDeEntrega(NVEntities db, IRegistroReembolsosAlEntregar registro)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.registro = registro ?? throw new ArgumentNullException(nameof(registro));
        }

        /// <summary>Atajo: <c>new FacturasConReembolsoPendienteDeEntrega(db).Leer(facturas)</c>.</summary>
        public static IDictionary<string, int> Leer(NVEntities db, IEnumerable<string> facturas)
            => new FacturasConReembolsoPendienteDeEntrega(db).Leer(facturas);

        public IDictionary<string, int> Leer(IEnumerable<string> facturas)
        {
            var resultado = new Dictionary<string, int>();
            List<string> lista = (facturas ?? Enumerable.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct()
                .ToList();
            if (!lista.Any())
            {
                return resultado;
            }
            // LeerPendientesAsync espera con ConfigureAwait(false): esperarla aquí no bloquea el contexto de la petición.
            HashSet<int> envios = registro.LeerPendientesAsync().GetAwaiter().GetResult();
            if (envios == null || !envios.Any())
            {
                return resultado;
            }
            List<int> listaEnvios = envios.ToList();
            var facturasPedidos = (from e in db.EnviosAgencias
                                   where listaEnvios.Contains(e.Numero) && e.Pedido != null
                                   join l in db.LinPedidoVtas
                                       on new { Empresa = e.Empresa, Pedido = e.Pedido.Value } equals new { Empresa = l.Empresa, Pedido = l.Número }
                                   where l.Nº_Factura != null && lista.Contains(l.Nº_Factura)
                                   select new { Factura = l.Nº_Factura, Pedido = l.Número })
                                  .Distinct()
                                  .ToList();
            foreach (var fp in facturasPedidos)
            {
                string factura = fp.Factura.Trim();
                if (!resultado.ContainsKey(factura))
                {
                    resultado.Add(factura, fp.Pedido);
                }
            }
            return resultado;
        }
    }
}
