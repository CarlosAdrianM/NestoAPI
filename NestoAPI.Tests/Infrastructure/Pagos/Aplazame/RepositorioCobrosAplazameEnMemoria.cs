using NestoAPI.Infraestructure.Pagos.Aplazame;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos.Aplazame
{
    /// <summary>NestoAPI#663: PagosTPV y líneas de pedido en memoria, con las mismas reglas que el repositorio de verdad.</summary>
    internal class RepositorioCobrosAplazameEnMemoria : IRepositorioCobrosAplazame
    {
        public Dictionary<int, PagoTPV> Pagos { get; } = new Dictionary<int, PagoTPV>();
        public Dictionary<int, List<LineaPedidoServida>> Lineas { get; } = new Dictionary<int, List<LineaPedidoServida>>();
        private int siguienteId = 1000;

        public PagoTPV Anadir(PagoTPV pago)
        {
            Pagos[pago.Id] = pago;
            return pago;
        }

        private static PagoTPV Copia(PagoTPV p) => p == null ? null : (PagoTPV)p.GetType().GetMethod("MemberwiseClone",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(p, null);

        public Task<PagoTPV> Leer(int idPago) => Task.FromResult(Pagos.TryGetValue(idPago, out PagoTPV p) ? Copia(p) : null);

        public Task<PagoTPV> LeerPorMid(string mid) => Task.FromResult(Copia(Pagos.Values.FirstOrDefault(p =>
            p.NumeroOrden == mid && p.Tipo == Constantes.TiposPagoTPV.CARRITO_APLAZAME)));

        public Task<int> Crear(PagoTPV pago)
        {
            pago.Id = ++siguienteId;
            Pagos[pago.Id] = pago;
            return Task.FromResult(pago.Id);
        }

        public Task GuardarReferencias(int idPago, string mid, string checkoutId)
        {
            if (mid != null)
            {
                Pagos[idPago].NumeroOrden = mid;
            }
            if (checkoutId != null)
            {
                Pagos[idPago].ReferenciaExterna = checkoutId;
            }
            return Task.CompletedTask;
        }

        public Task<bool> CambiarEstado(int idPago, IEnumerable<string> desde, string a, DateTime? fechaConfirmacion = null)
        {
            if (!Pagos.TryGetValue(idPago, out PagoTPV pago) || !desde.Contains(pago.Estado))
            {
                return Task.FromResult(false);
            }
            pago.Estado = a;
            pago.FechaConfirmacion = pago.FechaConfirmacion ?? fechaConfirmacion;
            return Task.FromResult(true);
        }

        public Task GuardarCaptura(int idPago, DateTime fecha, decimal importe)
        {
            Pagos[idPago].FechaCaptura = fecha;
            Pagos[idPago].ImporteCapturado = importe;
            return Task.CompletedTask;
        }

        public Task MarcarAvisoCaptura(int idPago, DateTime fecha)
        {
            Pagos[idPago].FechaAvisoCaptura = fecha;
            return Task.CompletedTask;
        }

        public Task<List<PagoTPV>> LeerConfirmadosSinCapturar() => Task.FromResult(Pagos.Values
            .Where(p => p.Tipo == Constantes.TiposPagoTPV.CARRITO_APLAZAME && p.Estado == Constantes.EstadosPagoTPV.AUTORIZADO
                && p.FechaCaptura == null && int.TryParse(p.Documento?.Trim(), out int n) && n > 0)
            .Select(Copia)
            .ToList());

        public Task<List<LineaPedidoServida>> LeerLineasPedido(string empresa, int pedido) =>
            Task.FromResult(Lineas.TryGetValue(pedido, out List<LineaPedidoServida> l) ? l : new List<LineaPedidoServida>());
    }
}
