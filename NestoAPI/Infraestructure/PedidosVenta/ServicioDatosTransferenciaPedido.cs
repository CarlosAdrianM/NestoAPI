using NestoAPI.Infraestructure.Cobros;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    public interface IServicioDatosTransferenciaPedido
    {
        /// <summary>Null si el pedido no existe.</summary>
        Task<DatosTransferenciaPedidoDTO> Leer(string empresa, int numero);
    }

    /// <summary>
    /// Sugerencia 396 de Novedades (Paloma): en un pedido prepago por transferencia, los datos para que
    /// el cliente la haga (IBAN, beneficiario, concepto e importe), listos para copiar y pegar. El IBAN
    /// y el titular salen de <see cref="ILectorDatosPagoEmpresa"/>, igual que en el aviso de facturas
    /// vencidas; el concepto sigue el de ese aviso, pero con el pedido en vez de las facturas.
    /// </summary>
    public class ServicioDatosTransferenciaPedido : IServicioDatosTransferenciaPedido
    {
        private readonly NVEntities db;
        private readonly ILectorDatosPagoEmpresa lectorDatosPago;

        public ServicioDatosTransferenciaPedido(NVEntities db, ILectorDatosPagoEmpresa lectorDatosPago = null)
        {
            this.db = db;
            this.lectorDatosPago = lectorDatosPago ?? new LectorDatosPagoEmpresa(db);
        }

        public async Task<DatosTransferenciaPedidoDTO> Leer(string empresa, int numero)
        {
            var cabecera = await db.CabPedidoVtas
                .Where(c => c.Empresa == empresa && c.Número == numero)
                .Select(c => new { c.Empresa, c.Nº_Cliente })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (cabecera == null)
            {
                return null;
            }

            // El mismo total que enseña el pedido en Nesto (GestorPedidosVenta.LeerPedido: líneas con estado > -99)
            List<decimal> totalesLineas = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Número == numero && l.Estado > -99)
                .Select(l => l.Total)
                .ToListAsync().ConfigureAwait(false);
            decimal importe = RoundingHelper.DosDecimalesRound(totalesLineas.Sum());

            DatosPagoAviso datosPago = lectorDatosPago.Leer(empresa) ?? new DatosPagoAviso();
            string cliente = cabecera.Nº_Cliente?.Trim();
            DatosTransferenciaPedidoDTO datos = new DatosTransferenciaPedidoDTO
            {
                Empresa = cabecera.Empresa?.Trim(),
                Pedido = numero,
                Cliente = cliente,
                Iban = string.IsNullOrWhiteSpace(datosPago.Iban) ? null : datosPago.Iban.Trim(),
                Titular = string.IsNullOrWhiteSpace(datosPago.Titular) ? null : datosPago.Titular.Trim(),
                Concepto = Concepto(cliente, numero),
                Importe = importe
            };
            datos.Texto = Texto(datos);
            return datos;
        }

        /// <summary>«Cliente 29606 - Pedido 927160».</summary>
        public static string Concepto(string cliente, int pedido) => $"Cliente {cliente?.Trim()} - Pedido {pedido}";

        /// <summary>Una línea por dato, para pegar en un correo o un WhatsApp. Lo que falte no sale.</summary>
        public static string Texto(DatosTransferenciaPedidoDTO datos)
        {
            if (datos == null)
            {
                throw new ArgumentNullException(nameof(datos));
            }
            List<string> lineas = new List<string>();
            if (!string.IsNullOrWhiteSpace(datos.Iban))
            {
                lineas.Add($"IBAN: {datos.Iban}");
            }
            if (!string.IsNullOrWhiteSpace(datos.Titular))
            {
                lineas.Add($"Beneficiario: {datos.Titular}");
            }
            lineas.Add($"Concepto: {datos.Concepto}");
            if (datos.Importe > 0)
            {
                lineas.Add($"Importe: {PlantillaAvisoFacturaVencida.FormatearImporte(datos.Importe)}");
            }
            return string.Join(Environment.NewLine, lineas);
        }
    }
}
