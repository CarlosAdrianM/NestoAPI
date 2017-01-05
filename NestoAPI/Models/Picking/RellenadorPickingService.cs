using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class RellenadorPickingService : IRellenadorPickingService
    {
        // Esta clase no la testamos porque lee de la base de datos
        // Haremos las pruebas una vez esté creada la clase GestorPicking, ejecutando el programa completo

        private NVEntities db = new NVEntities();
        private List<CabPedidoVta> pedidos = new List<CabPedidoVta>();

        private void rellenarSinParametros()
        {
            // Ordenamos por fecha modificación para que se asignen antes los más viejos
            IQueryable<LinPedidoVta> numerosPedidos = db.LinPedidoVtas.Where(l =>
                (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO) &&
                l.Almacén == Constantes.Productos.ALMACEN_POR_DEFECTO &&
                (l.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || l.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)
            ).OrderBy(l => l.Fecha_Modificación).ThenBy(l => l.Nº_Orden);
            pedidos = db.CabPedidoVtas.Where(c => numerosPedidos.Any(n => n.Número == c.Número)).ToList();
        }

        private void rellenarConParametroRuta(List<Ruta> rutas)
        {
            this.rellenarSinParametros();
            pedidos = pedidos.Where(p => rutas.Any(r => r.Número == p.Ruta)).ToList();
        }
        
        public List<PedidoPicking> Rellenar(List<Ruta> rutas)
        {
            rellenarConParametroRuta(rutas);
            return Ejecutar();
        }

        public List<PedidoPicking> Rellenar()
        {
            List<Ruta> rutasPorDefecto;
            rutasPorDefecto = db.Rutas.Where(r => r.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && ( r.Número == "AT" || r.Número == "OT" || r.Número == "16" || r.Número == "00" || r.Número == "FW")).ToList();
            rellenarConParametroRuta(rutasPorDefecto);
            return Ejecutar();
        }

        public List<PedidoPicking> Rellenar(string empresa, int numeroPedido)
        {
            CabPedidoVta pedido = db.CabPedidoVtas.SingleOrDefault(p => p.Empresa == empresa && p.Número == numeroPedido);
            pedidos.Add(pedido);
            return Ejecutar();
        }

        public List<PedidoPicking> Rellenar(string cliente)
        {
            rellenarSinParametros();
            pedidos = pedidos.Where(p => p.Nº_Cliente != null && p.Nº_Cliente.Trim() == cliente.Trim()).ToList();

            return Ejecutar();
        }

        public List<LineaPedidoPicking> RellenarTodasLasLineas(List<PedidoPicking> candidatos)
        {
            // No ordenamos por Id porque el campo que determina la antigüedad es la fecha de modificacion (y ya viene ordenado por fecha de modificacion)
            IEnumerable<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas);
            var productos = lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO).GroupBy(g => g.Producto);

            IEnumerable<LineaPedidoPicking> lineasResultado = db.LinPedidoVtas.Where(l => (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO) && l.Almacén == Constantes.Productos.ALMACEN_POR_DEFECTO && (l.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || l.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) )
                .Select(l => new LineaPedidoPicking
                {
                    Id = l.Nº_Orden,
                    TipoLinea = (byte)l.TipoLinea,
                    Producto = l.Producto,
                    Cantidad = (int)l.Cantidad - l.Recoger,
                    BaseImponible = l.Base_Imponible,
                    CantidadReservada = 0,
                    FechaEntrega = l.Fecha_Entrega,
                    EsSobrePedido = l.EstadoProducto != Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO && !l.LineaParcial,
                    FechaModificacion = l.Fecha_Modificación
                });

            lineasResultado = lineasResultado.Where(l => productos.Any(p => p.Key == l.Producto));

            return lineasResultado.OrderBy(l => l.FechaModificacion).ThenBy(l => l.Id).ToList();
        }

        private List<PedidoPicking> Ejecutar()
        {
            return pedidos.Select(p => new PedidoPicking
            {
                Empresa = p.Empresa,
                Id = p.Número,
                ServirJunto = p.ServirJunto,
                EsTiendaOnline = p.LinPedidoVtas.FirstOrDefault(l => l.Forma_Venta == "QRU" || l.Forma_Venta == "WEB" || l.Forma_Venta == "STK") != null,
                EsNotaEntrega = p.NotaEntrega && p.LinPedidoVtas.FirstOrDefault(l => l.YaFacturado) != null,
                ImporteOriginalSobrePedido = p.LinPedidoVtas.Where(l => l.EstadoProducto != Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO && !l.LineaParcial).Sum(l => l.Base_Imponible),
                ImporteOriginalNoSobrePedido = p.LinPedidoVtas.Where(l => l.EstadoProducto == Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO || l.LineaParcial).Sum(l => l.Base_Imponible),
                CodigoPostal = p.Cliente.CodPostal,
                Ruta = p.Ruta,
                Lineas = p.LinPedidoVtas.Where(l => l.Almacén == Constantes.Productos.ALMACEN_POR_DEFECTO && l.Empresa == p.Empresa && l.Número == p.Número && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && (l.Picking == null || l.Picking == 0))
                .Select(l => new LineaPedidoPicking
                {
                    Id = l.Nº_Orden,
                    TipoLinea = (byte)l.TipoLinea,
                    Producto = l.Producto,
                    Cantidad = (int)l.Cantidad - l.Recoger,
                    BaseImponible = l.Base_Imponible,
                    CantidadReservada = 0,
                    FechaEntrega = l.Fecha_Entrega,
                    EsSobrePedido = l.EstadoProducto != Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO && !l.LineaParcial,
                    FechaModificacion = l.Fecha_Modificación
                }).ToList()
            }).ToList();
        }
    }
}