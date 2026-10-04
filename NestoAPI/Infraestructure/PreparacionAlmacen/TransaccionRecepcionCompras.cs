using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models;
using NestoAPI.Models.PedidosCompra;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#559: las escrituras de «Terminar recepción» de compras, sobre la transacción abierta por
    /// RepositorioRecepcionCompras.EnTransaccion. SQL directo, como el resto de PreparacionAlmacen.
    ///
    /// <para>Respeta lo que hace la base de datos por su cuenta: el trigger trgLinPedidoCmpUpd recalcula
    /// SumaDescuentos (al insertar) y marca la rotura de stock del proveedor al pasar una línea a estado negativo
    /// (lo mismo que hacía el -99 de siempre); los importes (Bruto, ImporteDto, BaseImponible, IVA, RE y Total)
    /// NO los calcula nadie, así que se calculan aquí con <see cref="LineaPedidoCompraDTO"/>, como en el resto de
    /// la API.</para>
    /// </summary>
    public class TransaccionRecepcionComprasSql : ITransaccionRecepcionCompra
    {
        private readonly NVEntities db;
        private readonly IPedidosCompraService pedidosCompra;

        internal TransaccionRecepcionComprasSql(NVEntities db, IPedidosCompraService pedidosCompra)
        {
            this.db = db;
            this.pedidosCompra = pedidosCompra;
        }

        internal const string SQL_LINEAS_BLOQUEANDO = @"
SELECT l.Estado AS Estado, l.[Número] AS Pedido, CAST(ISNULL(c.Fecha, l.[FechaRecepción]) AS datetime) AS FechaPedido, l.[NºOrden] AS NumeroOrden,
       RTRIM(l.Producto) AS Producto, CAST(l.Cantidad AS int) AS Cantidad,
       CAST(ISNULL(l.[FechaRecepción], GETDATE()) AS datetime) AS FechaRecepcion,
       l.VistoBueno AS VistoBueno, CAST(ISNULL(pr.ControlPendientes, 1) AS bit) AS ControlPendientes,
       CAST(l.Precio AS decimal(19, 4)) AS Precio, l.DescuentoProveedor, l.DescuentoProducto, l.Descuento, l.DescuentoPP,
       l.AplicarDto, l.PorcentajeIVA, l.PorcentajeRE
FROM LinPedidoCmp l WITH (UPDLOCK, ROWLOCK)
     JOIN CabPedidoCmp c ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
     LEFT JOIN Proveedores pr ON pr.Empresa = l.Empresa AND pr.[Número] = l.[NºProveedor] AND pr.Contacto = l.Contacto
WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND l.[NºProveedor] = @p2
  AND ((" + RepositorioRecepcionCompras.FILTRO_LINEAS_DE_PRODUCTO + @") OR (" + RepositorioRecepcionCompras.FILTRO_RECUPERABLES + @"))";

        // Copia de una línea con otra cantidad, fecha, estado y visto bueno (NºOrden es identidad)
        internal const string SQL_COPIAR_LINEA = @"
INSERT INTO LinPedidoCmp (Empresa, [Número], [NºProveedor], Contacto, [TipoLínea], Producto, [Almacén], [FechaRecepción], Texto, Cantidad,
       Precio, Bruto, IVA, PorcentajeIVA, PorcentajeRE, DescuentoProveedor, DescuentoProducto, Descuento, DescuentoPP, SumaDescuentos,
       ImporteDto, BaseImponible, ImporteIVA, ImporteRE, Total, AplicarDto, [Delegación], FormaVenta, Estado, Grupo, Subgrupo, [NºOferta],
       VistoBueno, Enviado, Coste, CentroCoste, PrecioTarifa, YaFacturado, Reponer, Departamento, [% IRPF], EstadoProducto,
       Usuario, [Fecha Modificación])
SELECT Empresa, [Número], [NºProveedor], Contacto, [TipoLínea], Producto, [Almacén], @p2, Texto, @p3,
       Precio, @p4, IVA, PorcentajeIVA, PorcentajeRE, DescuentoProveedor, DescuentoProducto, Descuento, DescuentoPP, SumaDescuentos,
       @p5, @p6, @p7, @p8, @p9, AplicarDto, [Delegación], FormaVenta, @p10, Grupo, Subgrupo, [NºOferta],
       @p11, Enviado, Coste, CentroCoste, PrecioTarifa, YaFacturado, Reponer, Departamento, [% IRPF], EstadoProducto,
       @p12, GETDATE()
FROM LinPedidoCmp
WHERE Empresa = @p0 AND [NºOrden] = @p1 AND Estado = 1";

        internal const string SQL_RECIBIR_LINEA = @"
UPDATE LinPedidoCmp
SET Cantidad = @p2, [FechaRecepción] = @p3, Bruto = @p4, ImporteDto = @p5, BaseImponible = @p6, ImporteIVA = @p7, ImporteRE = @p8,
    Total = @p9, VistoBueno = @p10, Usuario = @p11, [Fecha Modificación] = GETDATE()
WHERE Empresa = @p0 AND [NºOrden] = @p1 AND Estado = 1";

        internal const string SQL_ANULAR = @"
UPDATE LinPedidoCmp SET Estado = -99, Usuario = @p2, [Fecha Modificación] = GETDATE()
WHERE Empresa = @p0 AND [NºOrden] = @p1 AND Estado = 1";

        internal const string SQL_APLAZAR = @"
UPDATE LinPedidoCmp SET [FechaRecepción] = @p2 WHERE Empresa = @p0 AND [NºOrden] = @p1 AND Estado = 1";

        public Task<bool> YaRegistrada(string empresa, IEnumerable<Guid> idsEvidencia)
        {
            return EvidenciasRecepcionSql.YaRegistrada(db, idsEvidencia);
        }

        public Task<List<LineaCompraPendiente>> LeerLineasBloqueando(string empresa, string almacen, string proveedor)
        {
            return db.Database.SqlQuery<LineaCompraPendiente>(SQL_LINEAS_BLOQUEANDO, empresa, almacen, proveedor).ToListAsync();
        }

        public async Task RecibirLinea(string empresa, LineaCompraPendiente linea, LineaRecibida recibida, DateTime hoy, string usuario)
        {
            string auditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            if (recibida.Resto > 0)
            {
                // Primero la copia (lee la línea tal cual está) y después se deja lo recibido en la original
                Importes resto = Calcular(linea, recibida.Resto);
                await Ejecutar(SQL_COPIAR_LINEA, $"partir la línea {linea.NumeroOrden}", empresa, linea.NumeroOrden, recibida.FechaResto.Date,
                    (short)recibida.Resto, resto.Bruto, resto.ImporteDto, resto.BaseImponible, resto.ImporteIva, resto.ImporteRe, resto.Total,
                    recibida.EstadoResto, linea.VistoBueno, auditoria).ConfigureAwait(false);
            }
            Importes recibido = Calcular(linea, recibida.Recibido);
            await Ejecutar(SQL_RECIBIR_LINEA, $"recibir la línea {linea.NumeroOrden}", empresa, linea.NumeroOrden, (short)recibida.Recibido,
                hoy.Date, recibido.Bruto, recibido.ImporteDto, recibido.BaseImponible, recibido.ImporteIva, recibido.ImporteRe, recibido.Total,
                recibida.VistoBueno, auditoria).ConfigureAwait(false);
        }

        // Vuelve a estado 1 (no a -1 como prdDeshacerAlbaránCmp: aquí se recibe ya, y prdCrearAlbaránCmp solo coge estado 1)
        internal const string SQL_REACTIVAR = @"
UPDATE LinPedidoCmp SET Estado = 1, Usuario = @p2, [Fecha Modificación] = GETDATE()
WHERE Empresa = @p0 AND [NºOrden] = @p1 AND Estado = -99";

        public Task Reactivar(string empresa, int numeroOrden, string usuario)
        {
            return Ejecutar(SQL_REACTIVAR, $"recuperar la línea {numeroOrden}", empresa, numeroOrden, UsuarioAuditoriaHelper.ParaAuditoria(usuario));
        }

        public Task Anular(string empresa, int numeroOrden, string usuario)
        {
            return Ejecutar(SQL_ANULAR, $"anular la línea {numeroOrden}", empresa, numeroOrden, UsuarioAuditoriaHelper.ParaAuditoria(usuario));
        }

        public Task CrearExceso(string empresa, LineaCompraPendiente copiaDe, ExcesoRecepcion exceso, DateTime hoy, string usuario)
        {
            Importes importes = Calcular(copiaDe, exceso.Cantidad);
            return Ejecutar(SQL_COPIAR_LINEA, $"añadir el exceso del producto {exceso.Producto}", empresa, copiaDe.NumeroOrden, hoy.Date,
                (short)exceso.Cantidad, importes.Bruto, importes.ImporteDto, importes.BaseImponible, importes.ImporteIva, importes.ImporteRe,
                importes.Total, PlanificadorRecepcionCompra.ESTADO_PENDIENTE, exceso.VistoBueno, UsuarioAuditoriaHelper.ParaAuditoria(usuario));
        }

        public async Task Aplazar(string empresa, IEnumerable<int> numerosOrden, DateTime fechaRecepcion)
        {
            foreach (int numeroOrden in numerosOrden ?? Enumerable.Empty<int>())
            {
                await Ejecutar(SQL_APLAZAR, $"pasar a mañana la línea {numeroOrden}", empresa, numeroOrden, fechaRecepcion.Date)
                    .ConfigureAwait(false);
            }
        }

        public async Task<int> CrearAlbaran(int pedido, string usuario)
        {
            int albaran = await pedidosCompra.CrearAlbaran(pedido, db, UsuarioAuditoriaHelper.ParaAuditoria(usuario)).ConfigureAwait(false);
            if (albaran <= 0)
            {
                throw new NestoBusinessException($"No se ha podido crear el albarán del pedido de compra {pedido} (resultado {albaran}).");
            }
            return albaran;
        }

        public Task RegistrarEvidencia(string empresa, IEnumerable<EvidenciaRecepcion> filas)
        {
            return EvidenciasRecepcionSql.Registrar(db, empresa, OrigenRecepcionCompras.TIPO, filas);
        }

        // Ensayo: las líneas de los pedidos del proveedor (todas, también las que se crean al partir) y los albaranes nuevos
        internal const string SQL_FOTO_COMPRAS = @"
SELECT 'LinPedidoCmp' AS Tabla, CAST(l.[NºOrden] AS varchar(20)) AS Clave,
       CONCAT('Pedido=', l.[Número], '; Producto=', RTRIM(l.Producto), '; Cantidad=', l.Cantidad, '; Estado=', l.Estado,
              '; VistoBueno=', l.VistoBueno, '; Recepción=', CONVERT(varchar(10), l.[FechaRecepción], 120), '; Albarán=', l.[NºAlbarán],
              '; Base=', l.BaseImponible, '; Total=', l.Total, '; Usuario=', RTRIM(l.Usuario)) AS Datos
FROM LinPedidoCmp l WHERE l.Empresa = @p0 AND l.[Número] IN ({LISTA})
UNION ALL
SELECT 'CabAlbaránCmp', CAST(c.[Número] AS varchar(20)),
       CONCAT('Proveedor=', RTRIM(c.[NºProveedor]), '; Fecha=', CONVERT(varchar(19), c.Fecha, 120), '; Usuario=', RTRIM(c.Usuario))
FROM [CabAlbaránCmp] c WHERE c.Empresa = @p0 AND c.[Número] > @p4";

        internal const string UBICACIONES_DE_COMPRAS = @"u.Empresa = @p0 AND u.Estado = 2
    AND u.[NºOrdenCmp] IN (SELECT x.[NºOrden] FROM LinPedidoCmp x WHERE x.Empresa = @p0 AND x.[Número] IN ({LISTA}))";

        public Task<Func<Task<List<FilaEnsayoDTO>>>> PrepararFoto(string empresa, IReadOnlyCollection<int> pedidos)
        {
            // prdCrearAlbaránCmp pasa las líneas al extracto por el diario _ALBCOMP
            return FotosEnsayoRecepcionSql.Preparar(db.Database, empresa, "_ALBCOMP", SQL_FOTO_COMPRAS, UBICACIONES_DE_COMPRAS, pedidos);
        }

        private async Task Ejecutar(string sql, string que, params object[] parametros)
        {
            int filas = await db.Database.ExecuteSqlCommandAsync(sql, parametros).ConfigureAwait(false);
            if (filas != 1)
            {
                // La línea ha cambiado desde que se leyó (otra persona, otro albarán): mejor no seguir
                throw new NestoBusinessException($"No se ha podido {que}: ha cambiado mientras se recibía. Vuelve a cargar la recepción.");
            }
        }

        internal static Importes Calcular(LineaCompraPendiente linea, int cantidad)
        {
            var dto = new LineaPedidoCompraDTO
            {
                Cantidad = cantidad,
                PrecioUnitario = linea.Precio,
                DescuentoEntidad = linea.DescuentoProveedor,
                DescuentoProducto = linea.DescuentoProducto,
                DescuentoLinea = linea.Descuento,
                DescuentoPP = linea.DescuentoPP,
                AplicarDescuento = linea.AplicarDto,
                PorcentajeIva = linea.PorcentajeIVA,
                PorcentajeRecargoEquivalencia = linea.PorcentajeRE
            };
            return new Importes
            {
                Bruto = dto.Bruto,
                ImporteDto = dto.ImporteDescuento,
                BaseImponible = dto.BaseImponible,
                ImporteIva = dto.ImporteIva,
                ImporteRe = dto.ImporteRecargoEquivalencia,
                Total = dto.Total
            };
        }

        internal class Importes
        {
            public decimal Bruto { get; set; }
            public decimal ImporteDto { get; set; }
            public decimal BaseImponible { get; set; }
            public decimal ImporteIva { get; set; }
            public decimal ImporteRe { get; set; }
            public decimal Total { get; set; }
        }

    }
}
