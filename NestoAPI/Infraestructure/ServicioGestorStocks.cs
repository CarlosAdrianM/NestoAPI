﻿using NestoAPI.Models;
using System.Linq;

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
            int stockExtracto = db.ExtractosProducto
                .Where(e => e.Número == producto && Constantes.Sedes.ListaSedes.Contains(e.Almacén))
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
            int stockRepo = db.PreExtrProductos.Where(e => e.Producto.Número == producto && e.NºTraspaso != null && e.NºTraspaso > 0).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum();
            return stockExtracto + stockRepo;
        }
        public int Stock(string producto, string almacen)
        {
            return db.ExtractosProducto
                .Where(e => e.Almacén == almacen && e.Número == producto)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
        }

        public int UnidadesDisponiblesTodosLosAlmacenes(string producto)
        {
            int stock = db.ExtractosProducto
                .Where(e => e.Número == producto && Constantes.Sedes.ListaSedes.Contains(e.Almacén))
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
            int pendienteReposicion = db.PreExtrProductos
                .Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) &&
                    e.Producto.Número == producto && e.NºTraspaso != null && e.NºTraspaso > 0)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum();
            int pendientes = db.LinPedidoVtas
                .Where(l =>
                    l.Producto == producto && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE 
                    && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .Select(e => (int)e.Cantidad)
                .DefaultIfEmpty(0)
                .Sum(c => c);
            return stock - pendientes + pendienteReposicion;
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