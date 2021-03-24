using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Infraestructure
{
    public class ServicioGestorStocks : IServicioGestorStocks
    {
        private NVEntities db;

        public ServicioGestorStocks()
        {
            db = new NVEntities();
        }

        public int Stock(string producto)
        {
            return db.ExtractosProducto.Where(e => e.Número == producto)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
        }
        public int Stock(string producto, string almacen)
        {
            return db.ExtractosProducto.Where(e => e.Almacén == almacen && e.Número == producto)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
        }

        public int UnidadesDisponiblesTodosLosAlmacenes(string producto)
        {
            int stock = db.ExtractosProducto.Where(e => e.Número == producto)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
            int pendientes = db.LinPedidoVtas
                .Where(l =>
                    l.Producto == producto && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE 
                    && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
            return stock - pendientes;
        }

        public int UnidadesPendientesEntregar(string producto)
        {
            return (int)db.LinPedidoVtas.Where(l =>
            l.Producto == producto &&
            l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
            .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
        }
        public int UnidadesPendientesEntregarAlmacen(string producto, string almacen)
        {
            return (int)db.LinPedidoVtas.Where(l =>
            l.Almacén == almacen && l.Producto == producto &&
            l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
            .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
        }
    }
}