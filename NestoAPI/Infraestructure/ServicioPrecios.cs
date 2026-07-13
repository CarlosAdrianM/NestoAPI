using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

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
                    (d.Nº_Producto == numeroProducto.Trim() || d.Familia == producto.Familia || d.GrupoProducto == producto.Grupo || (d.Familia == producto.Familia && producto.Nombre.StartsWith(d.FiltroProducto))))
                    .Where(FiltroClienteContacto(numeroCliente, contactoCliente))
                    .ToList();
            }
        }

        // Issue #278: una fila GLOBAL (Nº_Cliente == null) se aplica a todos los clientes
        // independientemente del Contacto, igual que hace el cálculo del precio
        // (GestorPrecios.calcularDescuentoProducto, que en las filas globales ignora el Contacto).
        // El filtro anterior exigía Nº_Cliente == numeroCliente cuando el Contacto no era null, así
        // que una fila global con un Contacto espurio (dato mal introducido, p. ej. Contacto='0' con
        // Nº_Cliente NULL) se usaba para CALCULAR el precio pero se descartaba al VALIDARLo → un pedido
        // correcto se denegaba con "No se encuentra autorizado el descuento". Se extrae a un método para
        // poder testearlo en memoria (la consulta real instancia NVEntities y no es unit-testable).
        internal static System.Linq.Expressions.Expression<Func<DescuentosProducto, bool>> FiltroClienteContacto(string numeroCliente, string contactoCliente)
        {
            return d =>
                (d.Nº_Cliente == null || d.Nº_Cliente == numeroCliente) &&
                (d.Nº_Cliente == null || d.Contacto == null || d.Contacto == contactoCliente);
        }

        public List<OfertaCombinada> BuscarOfertasCombinadas(string numeroProducto)
        {
            //TODO: comprobar con el SQL Profiler que hace lo que queremos
            using (NVEntities db = new NVEntities())
            {
                // Issue #282: además de las ofertas que llevan el producto concreto en el detalle,
                // hay que traer las que lo casan por FILTRO (fila con Producto NULL + Familia y/o
                // prefijo de nombre). Las candidatas con filtro se traen a memoria (las ofertas
                // activas son pocas) y se casan contra la familia/nombre del producto.
                List<OfertaCombinada> listaOfertas = db.OfertasCombinadas.Include("OfertasCombinadasDetalles")
                    .Where(o =>
                        o.OfertasCombinadasDetalles.Any(d => d.Producto == numeroProducto || d.Producto == null) &&
                        (o.FechaHasta == null || o.FechaHasta >= DateTime.Today) &&
                        (o.FechaDesde == null || o.FechaDesde <= DateTime.Today)
                    ).ToList();

                if (!listaOfertas.Any(o => o.OfertasCombinadasDetalles.Any(d => d.Producto == null)))
                {
                    return listaOfertas;
                }

                Producto producto = db.Productos.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == numeroProducto.Trim());

                return listaOfertas.Where(o => o.OfertasCombinadasDetalles.Any(d =>
                    (d.Producto != null && d.Producto.Trim() == numeroProducto.Trim())
                    || DetalleFiltroCasaConProducto(d, producto)
                )).ToList();
            }

        }

        // Issue #282: ¿la fila de filtro (Producto NULL) casa con este producto? Mismo matching que
        // FiltrarLineas: familia igual (si la fila la exige) y nombre que empiece por el prefijo.
        // Issue #289: y ademas grupo/subgrupo iguales, si la fila los exige.
        internal static bool DetalleFiltroCasaConProducto(OfertaCombinadaDetalle detalle, Producto producto)
        {
            if (detalle.Producto != null || producto == null)
            {
                return false;
            }
            bool familiaOk = detalle.Familia == null
                || (producto.Familia != null && producto.Familia.Trim().Equals(detalle.Familia.Trim(), StringComparison.OrdinalIgnoreCase));
            bool filtroOk = detalle.FiltroProducto == null
                || (producto.Nombre != null && producto.Nombre.StartsWith(detalle.FiltroProducto, StringComparison.OrdinalIgnoreCase));
            bool grupoOk = detalle.Grupo == null
                || (producto.Grupo != null && producto.Grupo.Trim().Equals(detalle.Grupo.Trim(), StringComparison.OrdinalIgnoreCase));
            bool subgrupoOk = detalle.Subgrupo == null
                || (producto.SubGrupo != null && producto.SubGrupo.Trim().Equals(detalle.Subgrupo.Trim(), StringComparison.OrdinalIgnoreCase));
            return familiaOk && filtroOk && grupoOk && subgrupoOk;
        }

        public List<OfertaEscalonada> BuscarOfertasEscalonadas(string numeroProducto)
        {
            using (NVEntities db = new NVEntities())
            {
                return db.OfertasEscalonadas
                    .Include("OfertasEscalonadasProductos")
                    .Include("OfertasEscalonadasTramos")
                    .Where(o =>
                        o.OfertasEscalonadasProductos.Any(p => p.Producto == numeroProducto) &&
                        (o.FechaHasta == null || o.FechaHasta >= DateTime.Today) &&
                        (o.FechaDesde == null || o.FechaDesde <= DateTime.Today)
                    ).ToList();
            }
        }

        public decimal CalcularImporteGrupo(PedidoVentaDTO pedido, string grupo, string subGrupo)
        {
            using (NVEntities db = new NVEntities())
            {
                var listaProductosPedido = pedido.Lineas.Select(l => l.Producto);
                var importeGrupo = db.Productos.Where(p=>listaProductosPedido.Contains(p.Número))
                    .AsEnumerable()
                    .Join(pedido.Lineas,
                        prod => prod.Número?.Trim(),
                        lin => lin.Producto?.Trim(),
                        (prod, lin) => new { Producto = prod, LineaPedidoVentaDTO = lin})
                    .Where(r => r.Producto.Empresa?.Trim() == pedido.empresa
                        && r.Producto.Número?.Trim() == r.LineaPedidoVentaDTO.Producto
                        && r.Producto.Grupo == grupo
                        && r.Producto.SubGrupo == subGrupo
                        && (r.LineaPedidoVentaDTO.BaseImponible == 0 || r.LineaPedidoVentaDTO.BaseImponible != r.Producto.PVP * r.LineaPedidoVentaDTO.Cantidad)
                    );

                return (decimal)importeGrupo.Sum(r => r.Producto.PVP * r.LineaPedidoVentaDTO.Cantidad);
            }
        }

        public List<LineaPedidoVentaDTO> FiltrarLineas(PedidoVentaDTO pedido, string filtroProducto, string familia)
        {
            return FiltrarLineas(pedido, filtroProducto, familia, null, null);
        }

        // Issue #289: los criterios informados (prefijo del nombre, familia, grupo, subgrupo) se
        // combinan en AND; los que van a null no filtran, con lo que la sobrecarga corta se
        // comporta exactamente igual que antes.
        public List<LineaPedidoVentaDTO> FiltrarLineas(PedidoVentaDTO pedido, string filtroProducto, string familia, string grupo, string subgrupo)
        {
            using (NVEntities db = new NVEntities())
            {
                var listaProductosPedido = pedido.Lineas.Select(l => l.Producto);
                var consultaFiltrados = db.Productos.Where(p =>
                    p.Empresa == pedido.empresa
                    && listaProductosPedido.Contains(p.Número)
                    && p.Nombre.StartsWith(filtroProducto)
                );
                if (familia != null)
                {
                    consultaFiltrados = consultaFiltrados.Where(p => p.Familia == familia);
                }
                if (grupo != null)
                {
                    consultaFiltrados = consultaFiltrados.Where(p => p.Grupo == grupo);
                }
                if (subgrupo != null)
                {
                    consultaFiltrados = consultaFiltrados.Where(p => p.SubGrupo == subgrupo);
                }
                IQueryable<string> productosFiltrados = consultaFiltrados.Select(p => p.Número);

                var lineas = pedido.Lineas.Where(l => productosFiltrados.Contains(l.Producto)).ToList();
                return lineas;
            }
        }

        public List<RegaloImportePedido> BuscarRegaloPorImportePedido(string numeroProducto)
        {
            using (NVEntities db = new NVEntities())
            {
                return db.RegalosImportePedido.Where(
                    r => r.Producto == numeroProducto && (r.FechaInicio == null || r.FechaInicio < DateTime.Now) &&
                    (r.FechaFin == null || r.FechaFin > DateTime.Now)
                ).ToList();
            }
        }

        /// <summary>
        /// Obtiene el stock disponible total (todas las sedes) de un producto.
        /// Issue #117: Validar stock de Ganavisiones al crear pedido
        /// </summary>
        public int BuscarStockDisponibleTotal(string numeroProducto)
        {
            if (string.IsNullOrWhiteSpace(numeroProducto))
            {
                return 0;
            }

            using (NVEntities db = new NVEntities())
            {
                string productoTrimmed = numeroProducto.Trim();
                string[] empresas = new[] { Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO };

                int stock = db.ExtractosProducto
                    .Where(e => empresas.Contains(e.Empresa) && Constantes.Sedes.ListaSedes.Contains(e.Almacén) && e.Número == productoTrimmed)
                    .Select(e => (int)e.Cantidad)
                    .DefaultIfEmpty(0)
                    .Sum();

                int pendienteEntregar = db.LinPedidoVtas
                    .Where(e => empresas.Contains(e.Empresa) && Constantes.Sedes.ListaSedes.Contains(e.Almacén) && e.Producto == productoTrimmed
                        && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE))
                    .Select(e => (int)e.Cantidad)
                    .DefaultIfEmpty(0)
                    .Sum();

                int pendienteRecibir = db.LinPedidoCmps
                    .Where(e => empresas.Contains(e.Empresa) && Constantes.Sedes.ListaSedes.Contains(e.Almacén) && e.Producto == productoTrimmed
                        && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && e.Enviado == true)
                    .Select(e => (int)e.Cantidad)
                    .DefaultIfEmpty(0)
                    .Sum();

                int pendienteReposicion = db.PreExtrProductos
                    .Where(e => empresas.Contains(e.Empresa) && Constantes.Sedes.ListaSedes.Contains(e.Almacén) && e.Producto.Número == productoTrimmed
                        && e.NºTraspaso != null && e.NºTraspaso > 0)
                    .Select(e => (int)e.Cantidad)
                    .DefaultIfEmpty(0)
                    .Sum();

                return stock - pendienteEntregar + pendienteRecibir + pendienteReposicion;
            }
        }

        /// <summary>
        /// Obtiene los Ganavisiones activos para un producto.
        /// Issue #94: Sistema Ganavisiones
        /// </summary>
        public int? BuscarGanavisionesProducto(string numeroProducto)
        {
            if (string.IsNullOrWhiteSpace(numeroProducto))
            {
                return null;
            }

            using (NVEntities db = new NVEntities())
            {
                var hoy = DateTime.Today;
                var ganavision = db.Ganavisiones
                    .Where(g => g.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                                g.ProductoId == numeroProducto.Trim() &&
                                g.FechaDesde <= hoy &&
                                (g.FechaHasta == null || g.FechaHasta >= hoy))
                    .OrderByDescending(g => g.FechaDesde)
                    .FirstOrDefault();

                return ganavision?.Ganavisiones;
            }
        }
    }
}