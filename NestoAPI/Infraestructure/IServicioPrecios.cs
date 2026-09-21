using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure
{
    public interface IServicioPrecios
    {
        Producto BuscarProducto(string producto);
        List<OfertaPermitida> BuscarOfertasPermitidas(string producto);
        List<DescuentosProducto> BuscarDescuentosPermitidos(string numeroProducto, string numeroCliente, string contactoCliente);
        List<OfertaCombinada> BuscarOfertasCombinadas(string numeroProducto);
        List<OfertaEscalonada> BuscarOfertasEscalonadas(string numeroProducto);
        decimal CalcularImporteGrupo(PedidoVentaDTO pedido, string grupo, string subGrupo);
        List<LineaPedidoVentaDTO> FiltrarLineas(PedidoVentaDTO pedido, string filtroProducto, string familia);
        // Issue #289: como la anterior pero filtrando ademas por Grupo y/o Subgrupo del producto
        // (todos los criterios informados en AND). Se mantiene la sobrecarga corta para no tocar
        // los llamantes que no filtran por grupo (OfertasPermitidas).
        List<LineaPedidoVentaDTO> FiltrarLineas(PedidoVentaDTO pedido, string filtroProducto, string familia, string grupo, string subgrupo);
        List<RegaloImportePedido> BuscarRegaloPorImportePedido(string numeroProducto);
        /// <summary>NestoAPI#457 (corte 2): todos los regalos por importe vigentes hoy, para sugerirlos.</summary>
        List<RegaloImportePedido> BuscarRegalosPorImportePedidoVigentes();

        /// <summary>
        /// Obtiene los Ganavisiones activos para un producto (puntos de bonificación).
        /// Issue #94: Sistema Ganavisiones
        /// </summary>
        /// <param name="numeroProducto">ID del producto</param>
        /// <returns>Número de Ganavisiones del producto, o null si no tiene configurado</returns>
        int? BuscarGanavisionesProducto(string numeroProducto);

        /// <summary>
        /// Obtiene el stock disponible total (todas las sedes) de un producto.
        /// Issue #117: Validar stock de Ganavisiones al crear pedido
        /// Disponible = Stock - PendienteEntregar + PendienteRecibir + PendienteReposicion
        /// </summary>
        int BuscarStockDisponibleTotal(string numeroProducto);

        /// <summary>
        /// NestoAPI#501: familias que impiden vender <paramref name="familia"/>, cada una con su
        /// ventana en meses (0 = todo el historico). Vacio si la familia no esta condicionada.
        /// </summary>
        List<FamiliaIncompatibilidad> BuscarIncompatibilidadesFamilia(string familia);

        /// <summary>
        /// NestoAPI#501: fecha de la ultima linea de esa familia comprada por el cliente dentro de
        /// la ventana, o null si no ha comprado. Cuenta TODO menos los presupuestos (pendientes,
        /// en curso, albaranes y facturas), y mira el numero de cliente entero, no el contacto.
        /// </summary>
        DateTime? UltimaCompraDeFamilia(string cliente, string familia, int meses);
    }
}