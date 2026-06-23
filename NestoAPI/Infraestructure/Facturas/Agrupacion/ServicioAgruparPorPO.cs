using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 3): implementación del orquestador de agrupación por PO.
    /// </summary>
    public class ServicioAgruparPorPO : IServicioAgruparPorPO
    {
        private readonly NVEntities db;
        private readonly IEstrategiaAgrupacionPO estrategia;
        private readonly IMotorAgrupacionPedidos motor;
        private readonly IServicioFacturas servicioFacturas;

        public ServicioAgruparPorPO(
            NVEntities db,
            IEstrategiaAgrupacionPO estrategia,
            IMotorAgrupacionPedidos motor,
            IServicioFacturas servicioFacturas)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.estrategia = estrategia ?? throw new ArgumentNullException(nameof(estrategia));
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));
            this.servicioFacturas = servicioFacturas ?? throw new ArgumentNullException(nameof(servicioFacturas));
        }

        public async Task<ResultadoAgrupacionPO> EvaluarYProcesar(string empresa, string usuario)
        {
            var resultado = new ResultadoAgrupacionPO();

            foreach (GrupoPedidosPO grupo in estrategia.SeleccionarGrupos(empresa).ToList())
            {
                // Solo agrupamos grupos con más de un pedido: un único pedido con PO se factura
                // por el flujo normal (el gate de Fase 1 ya no lo bloquea al no tener hermanos).
                if (grupo.Pedidos == null || grupo.Pedidos.Count < 2)
                {
                    continue;
                }

                try
                {
                    CabPedidoVta destino = estrategia.ElegirDestino(grupo);

                    // Mueve las líneas de los hermanos al destino y marca el destino Agrupada.
                    _ = motor.Agrupar(grupo.Pedidos, destino);
                    _ = db.SaveChanges();

                    // Único punto que toca el SP de facturación. El destino conserva su SuPedido,
                    // que prdCrearFacturaVta copia a CabFacturaVta.SuPedido.
                    CrearFacturaResponseDTO factura =
                        await servicioFacturas.CrearFactura(empresa, destino.Número, usuario);
                    resultado.Facturas.Add(factura);
                }
                catch (Exception ex)
                {
                    // Aislamos el fallo: un PO que falle no impide procesar el resto.
                    resultado.Errores.Add(new ErrorAgrupacionPO
                    {
                        Empresa = grupo.Empresa,
                        Cliente = grupo.Cliente,
                        SuPedido = grupo.SuPedido,
                        Mensaje = ex.Message
                    });
                }
            }

            return resultado;
        }
    }
}
