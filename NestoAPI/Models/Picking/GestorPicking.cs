using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    public class GestorPicking
    {
        private ModulosPicking modulos;
        private List<PedidoPicking> candidatos;
        private List<PedidoPicking> retenidosPorPrepago;
        private NVEntities db = new NVEntities();

        public GestorPicking(ModulosPicking modulos)
        {
            this.modulos = modulos;
        }
        public void SacarPicking()
        {

            candidatos = modulos.rellenadorPicking.Rellenar();
            Ejecutar();
        }

        public void SacarPicking(List<Ruta> rutas)
        {
            candidatos = modulos.rellenadorPicking.Rellenar(rutas);
            Ejecutar();
        }

        public void SacarPicking(string empresa, int numeroPedido)
        {
            candidatos = modulos.rellenadorPicking.Rellenar(empresa, numeroPedido);
            Ejecutar();
        }

        public void SacarPicking(string cliente)
        {
            candidatos = modulos.rellenadorPicking.Rellenar(cliente);
            Ejecutar();
        }


        public List<PedidoPicking> PedidosEnPicking()
        {
            return candidatos;
        }

        private void Ejecutar()
        {
            List<StockProducto> stocks;
            List<LineaPedidoPicking> todasLasLineas;
            DateTime fechaPicking = this.calcularFechaPicking(DateTime.Now);

            stocks = modulos.rellenadorStocks.Rellenar(candidatos);

            todasLasLineas = modulos.rellenadorPicking.RellenarTodasLasLineas(candidatos);

            GestorReservasStock.Reservar(stocks, candidatos, todasLasLineas);

            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, fechaPicking);

            // Recorrer Candidatos (quitamos los que no tienen que salir)
            for (int i = 0; i < candidatos.Count(); i++)
            {
                PedidoPicking pedido = candidatos[i];
                GestorStocksPicking gestorStocks = new GestorStocksPicking(pedido);
                if (!pedido.saleEnPicking() || pedido.Lineas.Count == 0 || !gestorStocks.HayStockDeAlgo())
                {
                    pedido.Borrar = true;
                }
                else
                {
                    if (pedido.hayQueSumarPortes())
                    {
                        GeneradorPortes generadorPortes = new GeneradorPortes(db, pedido);
                        generadorPortes.Ejecutar();
                    };
                }
            }

            // Actualizar Pendientes
            GeneradorPendientes generadorPendientes = new GeneradorPendientes(db, candidatos);
            generadorPendientes.Ejecutar();

            retenidosPorPrepago = candidatos.Where(c => c.RetenidoPorPrepago).ToList();
            candidatos.RemoveAll(c => c.Borrar);

            // Asignar Picking
            AsignadorPicking asignadorPicking = new AsignadorPicking(db, candidatos);
            asignadorPicking.Ejecutar();

            // Finalizar Picking
            modulos.finalizador.Ejecutar(db);

            // Si no se ha asignado picking a nada, damos error
            if (candidatos.Count == 0)
            {
                throw new Exception("No hay stock suficiente para asignar picking a ninguna línea");
            }

            // Mandamos el correo con los pedidos que van por debajo del margen
            GestorMargenes gestor = new GestorMargenes();
            gestor.Rellenar(asignadorPicking.numeroPicking);
            gestor.enviarCorreo();
            GestorPrepagos.EnviarCorreo(retenidosPorPrepago);
            
        }

        private DateTime calcularFechaPicking(DateTime fechaConHora)
        {
            DateTime fechaSinHora = new DateTime(fechaConHora.Year, fechaConHora.Month, fechaConHora.Day);
            
            // Si es antes de las 11h devuelve la fecha de hoy (sin hora)
            if (fechaConHora.Hour < Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS)
            {
                return fechaSinHora;
            }

            // Si es después de las 11h devolvemos el siguiente día laboral
            // Ahora mismo no tiene en cuenta los festivos, pero habrá que contemplarlos en el futuro
            if (fechaConHora.DayOfWeek == DayOfWeek.Friday)
            {
                return fechaSinHora.AddDays(3);
            }

            return fechaSinHora.AddDays(1);
        }
        
    }

    
}