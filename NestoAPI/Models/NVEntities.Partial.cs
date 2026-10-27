using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Models
{
    /// <summary>
    /// Clase parcial de NVEntities para agregar constructores personalizados
    /// </summary>
    public partial class NVEntities : DbContext
    {
        /// <summary>
        /// Constructor que acepta una conexión existente.
        /// Útil para compartir una SqlConnection entre múltiples DbContext en una transacción.
        /// </summary>
        /// <param name="existingConnection">Conexión SQL existente</param>
        /// <param name="contextOwnsConnection">
        /// Si es false, el DbContext NO cerrará la conexión al hacer Dispose.
        /// Usar false cuando la conexión es compartida y gestionada externamente.
        /// </param>
        public NVEntities(DbConnection existingConnection, bool contextOwnsConnection)
            : base(existingConnection, contextOwnsConnection)
        {
        }

        // Issue #275: el patrón 'contador.Pedidos++' + SaveChanges NO es atómico: dos peticiones
        // concurrentes leen el mismo valor, el UPDATE del contador no detecta el conflicto
        // (last-write-wins, sin token de concurrencia) y ambas insertan el mismo Número de pedido
        // → violación de PK_CabPedidoVta y pedido perdido.
        internal const string SQL_SIGUIENTE_NUMERO_PEDIDO =
            "UPDATE ContadoresGlobales SET Pedidos = Pedidos + 1 OUTPUT INSERTED.Pedidos;";

        /// <summary>
        /// Reserva de forma ATÓMICA el siguiente número de pedido (el UPDATE toma bloqueo exclusivo
        /// de la fila, así que dos peticiones concurrentes reciben números distintos). El número se
        /// consume aunque el guardado posterior falle: los huecos en la numeración de pedidos son
        /// aceptables (borrar un pedido ya los produce). Virtual para poder fakearlo en tests.
        /// </summary>
        public virtual int TomarSiguienteNumeroPedido()
        {
            return Database.SqlQuery<int>(SQL_SIGUIENTE_NUMERO_PEDIDO).Single();
        }

        // NestoAPI#636: cada pedido rechazado (validación, error al guardar) se comía un número y dejaba
        // un hueco (14/10: Manuel, cuatro rechazos → 928500..928508 solo pares). Devolverlo es atómico y
        // seguro: solo baja si el contador sigue en ESE número (nadie ha reservado otro después) y no hay
        // ningún pedido con él. Si otro ya cogió el siguiente, el hueco queda (como antes), sin riesgo.
        internal const string SQL_DEVOLVER_NUMERO_PEDIDO =
            "UPDATE ContadoresGlobales SET Pedidos = Pedidos - 1 WHERE Pedidos = @numero " +
            "AND NOT EXISTS (SELECT 1 FROM CabPedidoVta WHERE Número = @numero);";

        /// <summary>
        /// Devuelve al contador un número reservado con <see cref="TomarSiguienteNumeroPedido"/> cuyo
        /// pedido no se ha llegado a guardar. True si se ha devuelto; false si ya no era el último.
        /// Virtual para poder fakearlo en tests.
        /// </summary>
        public virtual bool DevolverNumeroPedido(int numero)
        {
            return Database.ExecuteSqlCommand(SQL_DEVOLVER_NUMERO_PEDIDO,
                new System.Data.SqlClient.SqlParameter("@numero", numero)) > 0;
        }

        // NestoAPI#684: red de seguridad de la fecha de entrega. El POST y el PUT de pedidos ya la validan a la entrada
        // con un 400 claro; aquí se para cualquier otro camino (notas de entrega, líneas partidas, copias...) que cree o
        // cambie la fecha de una línea de venta a una anterior al 01/01/2000, con el mismo mensaje y sin llegar al CHECK
        // de SQL. Las líneas que se modifican sin tocar la fecha no se miran (hay históricas facturadas con 1899-1997).
        //
        // NestoAPI#705: además, los productos de las líneas de venta que comprometen o liberan stock se encolan en
        // Nesto_sync DESPUÉS de grabar, para que la tienda reciba el CantidadDisponibleTotal nuevo (un pedido pendiente
        // no genera movimiento en ExtractoProducto y el job nocturno no lo veía). Aquí y no en cada controller: por este
        // punto pasan todas las grabaciones de líneas de la API (POST, PUT, presupuestos, canales externos, tienda...).
        public override int SaveChanges()
        {
            ComprobarFechasEntregaLineasVenta();
            List<string> productosAEncolar = ProductosLineasVentaAEncolar();
            int resultado = base.SaveChanges();
            EncolarProductosLineasVentaSinRomper(productosAEncolar);
            return resultado;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            ComprobarFechasEntregaLineasVenta();
            List<string> productosAEncolar = ProductosLineasVentaAEncolar();
            int resultado = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await EncolarProductosLineasVentaSinRomperAsync(productosAEncolar).ConfigureAwait(false);
            return resultado;
        }

        /// <summary>
        /// NestoAPI#705: foto de las líneas de venta que se van a grabar (antes de SaveChanges, que las deja Unchanged).
        /// Best-effort: si algo falla al mirarlas, se loguea y se graba igual.
        /// </summary>
        private List<string> ProductosLineasVentaAEncolar()
        {
            try
            {
                var cambios = new List<CambioLineaVenta>();
                foreach (DbEntityEntry<LinPedidoVta> entrada in ChangeTracker.Entries<LinPedidoVta>())
                {
                    switch (entrada.State)
                    {
                        case EntityState.Added:
                            cambios.Add(CambioLineaVenta.Alta(ProductosLineasVentaSync.Foto(entrada.Entity)));
                            break;
                        case EntityState.Deleted:
                            cambios.Add(CambioLineaVenta.Borrado(FotoOriginal(entrada)));
                            break;
                        case EntityState.Modified:
                            cambios.Add(new CambioLineaVenta
                            {
                                Antes = FotoOriginal(entrada),
                                Despues = ProductosLineasVentaSync.Foto(entrada.Entity),
                                CamposDeStockModificados = entrada.Property(l => l.Producto).IsModified
                                    || entrada.Property(l => l.Cantidad).IsModified
                                    || entrada.Property(l => l.Almacén).IsModified
                                    || entrada.Property(l => l.Estado).IsModified
                                    || entrada.Property(l => l.TipoLinea).IsModified
                                    || entrada.Property(l => l.Empresa).IsModified
                            });
                            break;
                    }
                }
                return ProductosLineasVentaSync.ProductosAEncolar(cambios);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[NestoAPI#705] No se han podido mirar las líneas de venta para encolar sus productos: {ex.Message}", ex));
                return new List<string>();
            }
        }

        private static LineaVentaStock FotoOriginal(DbEntityEntry<LinPedidoVta> entrada)
        {
            DbPropertyValues originales = entrada.OriginalValues;
            return new LineaVentaStock
            {
                Empresa = originales.GetValue<string>(nameof(LinPedidoVta.Empresa)),
                Producto = originales.GetValue<string>(nameof(LinPedidoVta.Producto)),
                Almacen = originales.GetValue<string>(nameof(LinPedidoVta.Almacén)),
                Estado = originales.GetValue<short>(nameof(LinPedidoVta.Estado)),
                TipoLinea = originales.GetValue<byte?>(nameof(LinPedidoVta.TipoLinea)),
                Cantidad = originales.GetValue<short?>(nameof(LinPedidoVta.Cantidad))
            };
        }

        private void EncolarProductosLineasVentaSinRomper(List<string> productos)
        {
            if (productos == null || productos.Count == 0)
            {
                return;
            }
            try
            {
                _ = EncolarProductosSyncSincrono(productos, ProductosLineasVentaSync.USUARIO_ENCOLADO);
            }
            catch (Exception ex)
            {
                LoguearFalloEncolado(productos, ex);
            }
        }

        /// <summary>
        /// NestoAPI#705: encola sin romper nunca la grabación del pedido (ya está grabado). Si falla, lo recoge el job
        /// nocturno por la [Fecha Modificación] de la línea. Público para el DELETE de pedidos, cuyas líneas no pasan
        /// por el ChangeTracker. Virtual para poder comprobarlo en los tests.
        /// </summary>
        public virtual async Task EncolarProductosLineasVentaSinRomperAsync(List<string> productos)
        {
            if (productos == null || productos.Count == 0)
            {
                return;
            }
            try
            {
                _ = await EncolarProductosSync(productos, ProductosLineasVentaSync.USUARIO_ENCOLADO).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoguearFalloEncolado(productos, ex);
            }
        }

        private static void LoguearFalloEncolado(List<string> productos, Exception ex)
        {
            ElmahHelper.Log(new Exception(
                $"[NestoAPI#705] Pedido grabado, pero no se han podido encolar en Nesto_sync los productos {string.Join(", ", productos)} " +
                $"(los recogerá el job nocturno): {ex.Message}", ex));
        }

        private void ComprobarFechasEntregaLineasVenta()
        {
            foreach (DbEntityEntry<LinPedidoVta> entrada in ChangeTracker.Entries<LinPedidoVta>())
            {
                if (ValidadorFechaEntrega.HayQueComprobarAlGuardar(entrada.State,
                    () => entrada.Property(l => l.Fecha_Entrega).IsModified))
                {
                    ValidadorFechaEntrega.Comprobar(entrada.Entity);
                }
            }
        }
    }
}
