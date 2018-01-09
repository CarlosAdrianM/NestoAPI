using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure
{
    public class ServicioPrecios : IServicioPrecios
    {
        public Producto BuscarProducto(string producto)
        {
            using (NVEntities db = new NVEntities())
            {
                if (producto == null || producto.Trim() == "")
                {
                    return null;
                }
                
                return db.Productos.Single(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto.Trim());
            }
        }

        public List<OfertaPermitida> BuscarOfertasPermitidas(string numeroProducto)
        {
            using (NVEntities db = new NVEntities())
            {
                if (numeroProducto == null || numeroProducto.Trim() == "")
                {
                    return null;
                }

                Producto producto = db.Productos.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == numeroProducto);

                if (producto == null)
                {
                    return null;
                }

                return db.OfertasPermitidas.Where(o => o.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && (o.Número == numeroProducto.Trim() || o.Familia == producto.Familia)).ToList();
            }
        }

        public List<DescuentosProducto> BuscarDescuentosPermitidos(string numeroProducto, string numeroCliente, string contactoCliente)
        {
            using (NVEntities db = new NVEntities())
            {
                if (numeroProducto == null || numeroProducto.Trim() == "")
                {
                    return null;
                }

                Producto producto = db.Productos.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == numeroProducto);

                if (producto == null)
                {
                    return null;
                }

                return db.DescuentosProductoes.Where(d => d.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && 
                    d.NºProveedor == null &&
                    (d.Nº_Producto == numeroProducto.Trim() || d.Familia == producto.Familia || d.GrupoProducto == producto.Grupo) &&
                    (d.Nº_Cliente == null || d.Nº_Cliente == numeroCliente) &&
                    (d.Contacto == null || (d.Nº_Cliente == numeroCliente && d.Contacto == contactoCliente))
                    ).ToList();
            }
        }

        public List<OfertaCombinada> BuscarOfertasCombinadas(string numeroProducto)
        {
            //TODO: comprobar con el SQL Profiler que hace lo que queremos
            using (NVEntities db = new NVEntities())
            {
                IQueryable<OfertaCombinada> listaOfertas = db.OfertasCombinadas.Include("OfertasCombinadasDetalles")
                    .Where(o => 
                        o.OfertasCombinadasDetalles.Any(d => d.Producto == numeroProducto) &&
                        (o.FechaHasta == null || o.FechaHasta >= DateTime.Today) &&
                        (o.FechaDesde == null || o.FechaDesde <= DateTime.Today)
                    );

                return listaOfertas.ToList();
            }
            
        }

        public decimal CalcularImporteGrupo(PedidoVentaDTO pedido, string grupo, string subGrupo)
        {
            using (NVEntities db = new NVEntities())
            {
                var listaProductosPedido = pedido.LineasPedido.Select(l=>l.producto);
                var importeGrupo = db.Productos.Where(p=>listaProductosPedido.Contains(p.Número))
                    .AsEnumerable()
                    .Join(pedido.LineasPedido,
                        prod => prod.Número,
                        lin => lin.producto,
                        (prod, lin) => new { Producto = prod, LineaPedidoVentaDTO = lin})
                    .Where(r => r.Producto.Empresa == pedido.empresa
                        && r.Producto.Número == r.LineaPedidoVentaDTO.producto
                        && r.Producto.Grupo == grupo
                        && r.Producto.SubGrupo == subGrupo
                    );

                return (decimal)importeGrupo.Sum(r => r.Producto.PVP * r.LineaPedidoVentaDTO.cantidad);
            }
        }

        public List<LineaPedidoVentaDTO> FiltrarLineas(PedidoVentaDTO pedido, string filtroProducto, string familia)
        {
            using (NVEntities db = new NVEntities())
            {
                var listaProductosPedido = pedido.LineasPedido.Select(l => l.producto);
                var consultaFiltrados = db.Productos.Where(p => 
                    p.Empresa == pedido.empresa
                    && listaProductosPedido.Contains(p.Número)
                    && p.Nombre.StartsWith(filtroProducto)
                );
                IQueryable<string> productosFiltrados;
                if (familia!= null)
                {
                    productosFiltrados = consultaFiltrados.Where(p => p.Familia == familia).Select(p => p.Número);
                } else
                {
                    productosFiltrados = consultaFiltrados.Select(p => p.Número);
                }
                
                var lineas = pedido.LineasPedido.Where(l => productosFiltrados.Contains(l.producto)).ToList();
                return lineas;
            }
        }
    }
}