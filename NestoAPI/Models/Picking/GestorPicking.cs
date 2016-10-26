using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    public class GestorPicking
    {
        private ModulosPicking modulos;
        private List<PedidoPicking> candidatos;
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

        public List<PedidoPicking> PedidosEnPicking()
        {
            return candidatos;
        }

        private void Ejecutar()
        {

            List<StockProducto> stocks;
            List<LineaPedidoPicking> todasLasLineas;
            DateTime fechaPicking = DateTime.Today;

            stocks = modulos.rellenadorStocks.Rellenar(candidatos);

            todasLasLineas = modulos.rellenadorPicking.RellenarTodasLasLineas(candidatos);

            GestorReservasStock.Reservar(stocks, candidatos, todasLasLineas);

            GestorReservasStock.BorrarLineasEntregaFutura(candidatos, fechaPicking);

            // Recorrer Candidatos (quitamos los que no tienen que salir)
            for (int i = 0; i < candidatos.Count(); i++)
            {
                PedidoPicking pedido = candidatos[i];
                GestorStocks gestorStocks = new GestorStocks(pedido);
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

            candidatos.RemoveAll(c => c.Borrar);

            // Actualizar Pendientes
            GeneradorPendientes generadorPendientes = new GeneradorPendientes(db, candidatos);
            generadorPendientes.Ejecutar();

            // Asignar Picking
            AsignadorPicking asignadorPicking = new AsignadorPicking(db, candidatos);
            asignadorPicking.Ejecutar();

            // Finalizar Picking
            modulos.finalizador.Ejecutar(db);

        }
        
    }
}