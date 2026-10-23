using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Una línea de pedido de venta pendiente de entregar (Nesto#552).</summary>
    public class LineaPendienteEntregarDTO
    {
        public int Pedido { get; set; }
        public int Linea { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string Vendedor { get; set; }
        public string Almacen { get; set; }
        public DateTime FechaEntrega { get; set; }
        public string Producto { get; set; }
        public string Texto { get; set; }
        public short Cantidad { get; set; }
        public decimal Bruto { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>
    /// Nesto#552 (sugerencia 717, Manuel): todas las líneas de producto pendientes de entregar (sin albarán, estado
    /// -1 o 1; sin presupuestos) de todos los almacenes. Solo lectura y en una única consulta (se usa en horario); al proyectar a un tipo anónimo EF no rastrea las entidades.
    /// </summary>
    public class ServicioPendientesEntregar
    {
        private readonly NVEntities db;

        public ServicioPendientesEntregar(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<LineaPendienteEntregarDTO>> Listar(string empresa, string almacen = null, string producto = null, string vendedor = null)
        {
            string empresaFiltro = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string almacenFiltro = string.IsNullOrWhiteSpace(almacen) ? null : almacen.Trim();
            string productoFiltro = string.IsNullOrWhiteSpace(producto) ? null : producto.Trim();
            string vendedorFiltro = string.IsNullOrWhiteSpace(vendedor) ? null : vendedor.Trim();
            const short PENDIENTE = Constantes.EstadosLineaVenta.PENDIENTE;
            const short EN_CURSO = Constantes.EstadosLineaVenta.EN_CURSO;
            const byte TIPO_PRODUCTO = Constantes.TiposLineaVenta.PRODUCTO;

            IQueryable<LinPedidoVta> lineas = db.LinPedidoVtas
                .Where(l => l.Empresa == empresaFiltro
                    && (l.Estado == PENDIENTE || l.Estado == EN_CURSO)
                    && l.TipoLinea == TIPO_PRODUCTO);
            if (almacenFiltro != null)
            {
                lineas = lineas.Where(l => l.Almacén == almacenFiltro);
            }
            if (productoFiltro != null)
            {
                lineas = lineas.Where(l => l.Producto == productoFiltro);
            }

            var consulta = from l in lineas
                           join c in db.CabPedidoVtas on new { l.Empresa, l.Número } equals new { c.Empresa, c.Número }
                           join cl in db.Clientes on new { l.Empresa, l.Nº_Cliente, l.Contacto } equals new { cl.Empresa, cl.Nº_Cliente, cl.Contacto } into clientes
                           from cl in clientes.DefaultIfEmpty()
                           select new
                           {
                               l.Número,
                               l.Nº_Orden,
                               l.Nº_Cliente,
                               l.Contacto,
                               Nombre = cl == null ? null : cl.Nombre,
                               c.Vendedor,
                               l.Almacén,
                               l.Fecha_Entrega,
                               l.Producto,
                               l.Texto,
                               l.Cantidad,
                               l.Bruto,
                               l.Usuario
                           };
            if (vendedorFiltro != null)
            {
                consulta = consulta.Where(x => x.Vendedor == vendedorFiltro);
            }

            var filas = await consulta.ToListAsync().ConfigureAwait(false);
            return filas
                .Select(x => new LineaPendienteEntregarDTO
                {
                    Pedido = x.Número,
                    Linea = x.Nº_Orden,
                    Cliente = x.Nº_Cliente?.Trim(),
                    Contacto = x.Contacto?.Trim(),
                    NombreCliente = x.Nombre?.Trim(),
                    Vendedor = x.Vendedor?.Trim(),
                    Almacen = x.Almacén?.Trim(),
                    FechaEntrega = x.Fecha_Entrega,
                    Producto = x.Producto?.Trim(),
                    Texto = x.Texto?.Trim(),
                    Cantidad = x.Cantidad ?? 0,
                    Bruto = x.Bruto,
                    Usuario = x.Usuario?.Trim()
                })
                .OrderBy(x => x.FechaEntrega)
                .ThenBy(x => x.Pedido)
                .ThenBy(x => x.Linea)
                .ToList();
        }
    }
}
