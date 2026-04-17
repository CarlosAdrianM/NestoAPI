using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Informes;

namespace NestoAPI.Infraestructure.Informes
{
    public class InformesService : IInformesService
    {
        private readonly NVEntities db;

        public InformesService()
        {
            db = new NVEntities();
        }

        internal InformesService(NVEntities db)
        {
            this.db = db;
        }

        public async Task<List<ResumenVentasDTO>> LeerResumenVentasAsync(DateTime fechaDesde, DateTime fechaHasta, bool soloFacturas)
        {
            SqlParameter fechaDesdeParam = new SqlParameter("@FechaDesde", SqlDbType.DateTime)
            {
                Value = fechaDesde
            };
            SqlParameter fechaHastaParam = new SqlParameter("@FechaHasta", SqlDbType.DateTime)
            {
                Value = fechaHasta
            };
            SqlParameter soloFacturasParam = new SqlParameter("@soloFacturas", SqlDbType.Bit)
            {
                Value = soloFacturas
            };

            return await db.Database
                .SqlQuery<ResumenVentasDTO>(
                    "prdInformeResumenVentas @FechaDesde, @FechaHasta, @soloFacturas",
                    fechaDesdeParam, fechaHastaParam, soloFacturasParam)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<ControlPedidosDTO>> LeerControlPedidosAsync()
        {
            return await db.Database
                .SqlQuery<ControlPedidosDTO>("prdInformeControlPedidos")
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<DetalleRapportsDTO>> LeerDetalleRapportsAsync(DateTime fechaDesde, DateTime fechaHasta, string listaVendedores)
        {
            SqlParameter fechaDesdeParam = new SqlParameter("@FechaDesde", SqlDbType.DateTime)
            {
                Value = fechaDesde
            };
            SqlParameter fechaHastaParam = new SqlParameter("@FechaHasta", SqlDbType.DateTime)
            {
                Value = fechaHasta
            };
            SqlParameter listaVendedoresParam = new SqlParameter("@ListaVendedores", SqlDbType.NVarChar)
            {
                Value = listaVendedores ?? string.Empty
            };

            return await db.Database
                .SqlQuery<DetalleRapportsDTO>(
                    "prdInformeRapportEstado9 @FechaDesde, @FechaHasta, @ListaVendedores",
                    fechaDesdeParam, fechaHastaParam, listaVendedoresParam)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<ExtractoContableDTO>> LeerExtractoContableAsync(string empresa, string cuenta, DateTime fechaDesde, DateTime fechaHasta)
        {
            const string sql = @"
                SELECT
                    [Nº Orden] Id,
                    Empresa,
                    Delegación Delegacion,
                    FormaVenta,
                    Fecha,
                    [Nº Documento] Documento,
                    Concepto,
                    Debe,
                    Haber,
                    ISNULL((
                        SELECT SUM(Debe - Haber)
                        FROM Contabilidad
                        WHERE [Nº Cuenta] = @Cuenta
                        AND Fecha < @FechaDesde
                        AND Empresa = @Empresa
                    ), 0)
                    + SUM(Debe - Haber) OVER (ORDER BY Fecha, [Nº Orden]) AS Saldo
                FROM Contabilidad
                WHERE [Nº Cuenta] = @Cuenta
                AND Fecha >= @FechaDesde AND Fecha < DATEADD(dd, 1, @FechaHasta)
                AND Empresa = @Empresa";

            return await db.Database
                .SqlQuery<ExtractoContableDTO>(sql,
                    new SqlParameter("@Empresa", SqlDbType.NVarChar) { Value = empresa },
                    new SqlParameter("@Cuenta", SqlDbType.NVarChar) { Value = cuenta },
                    new SqlParameter("@FechaDesde", SqlDbType.DateTime) { Value = fechaDesde },
                    new SqlParameter("@FechaHasta", SqlDbType.DateTime) { Value = fechaHasta })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<UbicacionesInventarioDTO>> LeerUbicacionesInventarioAsync(string empresa)
        {
            const string sql = @"
                SELECT Pasillo, Fila, Columna, rtrim(p.número) Producto, p.codbarras CodigoBarras,
                    rtrim(p.nombre) Nombre, p.tamaño Tamanno, p.UnidadMedida, p.Familia
                FROM ubicaciones u INNER JOIN productos p
                    ON p.empresa = @Empresa AND u.numero = p.numero
                WHERE u.estado = 0
                GROUP BY pasillo, fila, columna, p.número, p.codbarras, p.nombre, p.tamaño, p.unidadmedida, p.familia
                ORDER BY pasillo, columna, fila, p.número, p.codbarras, p.nombre, p.tamaño, p.unidadmedida, p.familia";

            return await db.Database
                .SqlQuery<UbicacionesInventarioDTO>(sql,
                    new SqlParameter("@Empresa", SqlDbType.NVarChar) { Value = empresa ?? "1" })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<KitsQueSePuedenMontarDTO>> LeerKitsQueSePuedenMontarAsync(string empresa, string fecha, string almacen, string filtroRutas)
        {
            var lista = await db.Database
                .SqlQuery<KitsQueSePuedenMontarDTO>(
                    "prdInformeKitsQueSePuedenMontar @Empresa, @Fecha, @Almacen, @FiltroRutas",
                    new SqlParameter("@Empresa", empresa),
                    new SqlParameter("@Fecha", fecha),
                    new SqlParameter("@Almacen", almacen),
                    new SqlParameter("@FiltroRutas", filtroRutas))
                .ToListAsync()
                .ConfigureAwait(false);

            // Trim replica el comportamiento del Model original para no romper el RDLC.
            foreach (var k in lista)
            {
                k.Tipo = k.Tipo?.Trim();
                k.Kit = k.Kit?.Trim();
                k.Nombre = k.Nombre?.Trim();
                k.CodigoBarras = k.CodigoBarras?.Trim();
            }
            return lista;
        }

        public async Task<List<MontarKitProductosDTO>> LeerMontarKitProductosAsync(int traspaso)
        {
            const string sql = @"
                SELECT rtrim(p.Número) Producto, rtrim(p.Nombre) Nombre, p.Tamaño Tamanno,
                    rtrim(p.UnidadMedida) UnidadMedida, rtrim(p.familia) Familia, Cantidad,
                    rtrim(Pasillo) Pasillo, rtrim(Fila) Fila, rtrim(Columna) Columna,
                    rtrim(CodBarras) CodigoBarras
                FROM Ubicaciones u INNER JOIN Productos p
                    ON u.Empresa IN ('1', '3') AND p.Empresa = '1' AND u.Número = p.Número
                WHERE nºtraspaso = @Traspaso AND u.Estado = -102
                ORDER BY [nºorden]";

            return await db.Database
                .SqlQuery<MontarKitProductosDTO>(sql,
                    new SqlParameter("@Traspaso", SqlDbType.Int) { Value = traspaso })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<ManifiestoAgenciaDTO>> LeerManifiestoAgenciaAsync(string empresa, int agencia, DateTime fecha)
        {
            const string sql = @"
                SELECT rtrim(Cliente) Cliente, rtrim(Contacto) Contacto, rtrim(Nombre) Nombre,
                    rtrim(Direccion) Direccion, rtrim(CodPostal) CodigoPostal, rtrim(Poblacion) Poblacion,
                    rtrim(Provincia) Provincia, Bultos, Reembolso, rtrim(Telefono) TelefonoFijo,
                    rtrim(Movil) TelefonoMovil, rtrim(Observaciones) Observaciones
                FROM enviosagencia e
                WHERE e.empresa = @Empresa AND e.estado = 1 AND e.agencia = @Agencia
                    AND CAST(Fecha AS DATE) = CAST(@Fecha AS DATE)
                GROUP BY nombre, Direccion, CodPostal, Poblacion, Provincia, Telefono,
                    observaciones, bultos, reembolso, cliente, contacto, Movil";

            return await db.Database
                .SqlQuery<ManifiestoAgenciaDTO>(sql,
                    new SqlParameter("@Empresa", SqlDbType.NVarChar) { Value = empresa },
                    new SqlParameter("@Agencia", SqlDbType.Int) { Value = agencia },
                    new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = fecha.Date })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<PedidoCompraInformeDTO> LeerPedidoCompraAsync(string empresa, int pedido)
        {
            const string sqlCabecera = @"
                SELECT c.Número Id, rtrim(NºProveedor) Proveedor, rtrim(p.Nombre) Nombre,
                    rtrim(p.Dirección) Direccion, rtrim(p.CodPostal) CodigoPostal,
                    rtrim(p.Población) Poblacion, rtrim(p.Provincia) Provincia,
                    rtrim(p.Teléfono) Telefono, rtrim(p.[CIF/NIF]) Cif, c.Fecha, p.PedidoValorado
                FROM CabPedidoCmp c
                INNER JOIN Proveedores p
                    ON c.Empresa = p.Empresa AND c.NºProveedor = p.Número AND c.Contacto = p.Contacto
                WHERE c.Empresa = @Empresa AND c.Número = @Pedido";

            var cabeceras = await db.Database
                .SqlQuery<PedidoCompraInformeDTO>(sqlCabecera,
                    new SqlParameter("@Empresa", SqlDbType.NVarChar) { Value = empresa },
                    new SqlParameter("@Pedido", SqlDbType.Int) { Value = pedido })
                .ToListAsync()
                .ConfigureAwait(false);

            var pedidoCompra = cabeceras.FirstOrDefault();
            if (pedidoCompra == null) return null;

            const string sqlLineas = @"
                SELECT rtrim(p.ReferenciaProv) SuReferencia, rtrim(l.Producto) NuestraReferencia,
                    rtrim(l.Texto) Descripcion, d.Tamaño Tamanno, rtrim(d.UnidadMedida) UnidadMedida,
                    l.Cantidad, l.Precio PrecioUnitario, l.SumaDescuentos, l.BaseImponible
                FROM LinPedidoCmp l
                LEFT JOIN Productos d ON l.Empresa = d.Empresa AND l.Producto = d.Número
                LEFT JOIN ProveedoresProducto p
                    ON l.Empresa = p.Empresa AND l.Producto = p.[Nº Producto] AND p.[Nº Proveedor] = @Proveedor
                WHERE l.Empresa = @Empresa AND l.Número = @Pedido
                ORDER BY l.NºOrden";

            pedidoCompra.Lineas = await db.Database
                .SqlQuery<LineaPedidoCompraInformeDTO>(sqlLineas,
                    new SqlParameter("@Empresa", SqlDbType.NVarChar) { Value = empresa },
                    new SqlParameter("@Pedido", SqlDbType.Int) { Value = pedido },
                    new SqlParameter("@Proveedor", SqlDbType.NVarChar) { Value = pedidoCompra.Proveedor ?? string.Empty })
                .ToListAsync()
                .ConfigureAwait(false);

            // Regla de negocio heredada: si el pedido no está valorado, se ocultan precios.
            if (!pedidoCompra.PedidoValorado)
            {
                foreach (var linea in pedidoCompra.Lineas)
                {
                    linea.PrecioUnitario = 0;
                    linea.SumaDescuentos = 0;
                    linea.BaseImponible = 0;
                }
            }
            return pedidoCompra;
        }
    }
}
