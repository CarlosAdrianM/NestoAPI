using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Traspasos
{
    /// <summary>
    /// Implementación del servicio de traspaso de pedidos entre empresas.
    ///
    /// PROCESO COMPLETO:
    /// 1. HayQueTraspasar() determina si un pedido debe traspasarse
    /// 2. TraspasarPedidoAEmpresa() ejecuta el traspaso:
    ///    - Copia cliente usando prdCopiarCliente (ADO.NET directo)
    ///    - Copia productos usando prdCopiarProducto (ADO.NET directo)
    ///    - Copia cuentas contables usando prdCopiarCuentaContable (ADO.NET directo)
    ///    - Actualiza empresa en cabecera y líneas
    ///    - Recalcula IVA/RE/Total con ParámetrosIVA de empresa destino
    ///    - Guarda cambios de forma transaccional
    ///
    /// CORRECCIÓN: Los procedimientos almacenados se ejecutan con SqlCommand directamente
    /// para evitar el error UnintentionalCodeFirstException que ocurre cuando se usa
    /// DbContext.Database.ExecuteSqlCommandAsync con una conexión SqlConnection externa.
    /// </summary>
    public class ServicioTraspasoEmpresa : IServicioTraspasoEmpresa
    {
        private readonly NVEntities db;
        private readonly ServicioPedidosVenta servicioPedidos;

        public ServicioTraspasoEmpresa(NVEntities db) : this(db, new ServicioPedidosVenta())
        {
        }

        public ServicioTraspasoEmpresa(NVEntities db, ServicioPedidosVenta servicioPedidos)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.servicioPedidos = servicioPedidos ?? throw new ArgumentNullException(nameof(servicioPedidos));
        }

        /// <summary>
        /// Empresa de destino para traspasos
        /// </summary>
        public string EmpresaTraspaso => "3";

        /// <summary>
        /// Determina si un pedido debe ser traspasado a la empresa espejo.
        ///
        /// CRITERIO ACTUAL:
        /// - Si el campo IVA de la cabecera del pedido es NULL → TRUE (debe traspasarse)
        /// - Si el campo IVA tiene algún valor → FALSE (no debe traspasarse)
        ///
        /// LÓGICA DE NEGOCIO:
        /// Los pedidos con IVA null requieren traspaso a la empresa espejo (3)
        /// antes de ser facturados.
        /// </summary>
        public bool HayQueTraspasar(CabPedidoVta pedido)
        {
            if (pedido == null)
                return false;

            // Si IVA es null o vacío → traspasar
            return string.IsNullOrWhiteSpace(pedido.IVA);
        }

        /// <summary>
        /// Ejecuta un procedimiento almacenado usando SqlCommand directamente.
        /// Evita el error UnintentionalCodeFirstException de Entity Framework.
        /// </summary>
        private async Task ExecuteStoredProcedureAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string procedureName,
            params SqlParameter[] parameters)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = procedureName;
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 60; // 60 segundos timeout

                if (parameters != null && parameters.Length > 0)
                {
                    cmd.Parameters.AddRange(parameters);
                }

                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (SqlException ex)
                {
                    // Re-lanzar con contexto adicional
                    throw new InvalidOperationException(
                        $"Error ejecutando procedimiento '{procedureName}': {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Traspasa un pedido de una empresa a otra.
        ///
        /// PROCESO (SEGURO - con SqlConnection + SqlTransaction local):
        /// 1. Validar parámetros (pedido no null, empresas no vacías, diferentes)
        /// 2. Verificar que pedido.Empresa == empresaOrigen
        /// 3. Abrir SqlConnection + SqlTransaction (60s timeout):
        ///    a. Copiar cliente con prdCopiarCliente (SqlCommand directo)
        ///    b. Copiar productos con prdCopiarProducto (SqlCommand directo)
        ///    c. Copiar cuentas contables con prdCopiarCuentaContable (SqlCommand directo)
        ///    d. Clonar el pedido con empresa destino
        ///    e. INSERTAR el nuevo pedido (empresa destino) usando EF
        ///    f. Recalcular importes
        ///    g. ELIMINAR el pedido original (empresa origen) usando EF
        ///    h. CommitAsync() - solo si todo salió bien
        ///
        /// VENTAJAS:
        /// - Una sola conexión física (NO promueve a MSDTC)
        /// - Timeout controlado (60s, no MaximumTimeout)
        /// - NO usa DbContext para procedimientos almacenados (evita UnintentionalCodeFirstException)
        /// - Más eficiente y predecible
        /// </summary>
        public async Task TraspasarPedidoAEmpresa(CabPedidoVta pedido, string empresaOrigen, string empresaDestino)
        {
            // 1. Validaciones de parámetros
            if (pedido == null)
                throw new ArgumentNullException(nameof(pedido));

            if (string.IsNullOrWhiteSpace(empresaOrigen))
                throw new ArgumentException("La empresa de origen no puede ser null o vacía", nameof(empresaOrigen));

            if (string.IsNullOrWhiteSpace(empresaDestino))
                throw new ArgumentException("La empresa de destino no puede ser null o vacía", nameof(empresaDestino));

            if (empresaOrigen.Trim() == empresaDestino.Trim())
                throw new ArgumentException("La empresa de origen y destino deben ser diferentes");

            // 2. Verificar que el pedido pertenece a la empresa origen
            if (pedido.Empresa?.Trim() != empresaOrigen.Trim())
            {
                throw new InvalidOperationException(
                    $"El pedido {pedido.Número} pertenece a la empresa '{pedido.Empresa}', " +
                    $"pero se intentó traspasar desde empresa '{empresaOrigen}'");
            }

            // Guardar datos necesarios antes de iniciar la transacción
            int numeroPedido = pedido.Número;
            string clienteNumero = pedido.Nº_Cliente?.Trim();

            // Obtener connection string del DbContext
            string connectionString = db.Database.Connection.ConnectionString;

            // IMPORTANTE: SqlConnection + SqlTransaction local garantiza:
            // 1. Una sola conexión física (NO promueve a MSDTC)
            // 2. Timeout controlado (60 segundos)
            // 3. Procedimientos almacenados ejecutados con SqlCommand (evita UnintentionalCodeFirstException)
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                // Timeout de 60 segundos (suficiente para un traspaso normal)
                using (var tx = conn.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    try
                    {
                        // 3. Copiar cliente a la empresa destino usando SqlCommand directo
                        // NOTA: prdCopiarCliente NO tiene transacción interna → se revierte con tx
                        await ExecuteStoredProcedureAsync(
                            conn,
                            tx,
                            "prdCopiarCliente",
                            new SqlParameter("@EmpresaOrigen", empresaOrigen.Trim()),
                            new SqlParameter("@EmpresaDestino", empresaDestino.Trim()),
                            new SqlParameter("@NumCliente", clienteNumero)
                        );

                        // 4. Copiar productos y cuentas contables del pedido
                        if (pedido.LinPedidoVtas != null && pedido.LinPedidoVtas.Any())
                        {
                            // 4a. Copiar productos únicos (líneas tipo PRODUCTO = 1)
                            // ADVERTENCIA: prdCopiarProducto tiene COMMIT interno
                            // Si el traspaso falla después, los productos quedan copiados (no es crítico)
                            var productosUnicos = pedido.LinPedidoVtas
                                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                                .Select(l => l.Producto?.Trim())
                                .Where(p => !string.IsNullOrEmpty(p))
                                .Distinct()
                                .ToList();

                            foreach (var producto in productosUnicos)
                            {
                                await ExecuteStoredProcedureAsync(
                                    conn,
                                    tx,
                                    "prdCopiarProducto",
                                    new SqlParameter("@EmpresaOrigen", empresaOrigen.Trim()),
                                    new SqlParameter("@EmpresaDestino", empresaDestino.Trim()),
                                    new SqlParameter("@NumProducto", producto)
                                );
                            }

                            // 4b. Copiar cuentas contables únicas (líneas tipo CUENTA_CONTABLE = 2)
                            // NOTA: prdCopiarCuentaContable NO tiene transacción interna → se revierte con tx
                            var cuentasContablesUnicas = pedido.LinPedidoVtas
                                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)
                                .Select(l => l.Producto?.Trim())
                                .Where(c => !string.IsNullOrEmpty(c))
                                .Distinct()
                                .ToList();

                            foreach (var cuenta in cuentasContablesUnicas)
                            {
                                await ExecuteStoredProcedureAsync(
                                    conn,
                                    tx,
                                    "prdCopiarCuentaContable",
                                    new SqlParameter("@EmpresaOrigen", empresaOrigen.Trim()),
                                    new SqlParameter("@EmpresaDestino", empresaDestino.Trim()),
                                    new SqlParameter("@NumCuenta", cuenta)
                                );
                            }
                        }

                        // 5. Clonar el pedido para empresa destino
                        // IMPORTANTE: Usamos el método ClonarParaEmpresa() que copia TODAS las propiedades
                        // VENTAJA: Si se agregan nuevas propiedades al modelo, se copiarán automáticamente
                        var pedidoNuevo = pedido.ClonarParaEmpresa(empresaDestino.Trim());

                        // Clonar las líneas usando el método ClonarParaEmpresa()
                        var lineasNuevas = new System.Collections.Generic.List<LinPedidoVta>();
                        if (pedido.LinPedidoVtas != null)
                        {
                            foreach (var lineaOriginal in pedido.LinPedidoVtas)
                            {
                                var lineaNueva = lineaOriginal.ClonarParaEmpresa(empresaDestino.Trim(), numeroPedido);
                                lineasNuevas.Add(lineaNueva);
                            }
                        }

                        pedidoNuevo.LinPedidoVtas = lineasNuevas;

                        // 6. Recalcular importes con ParámetrosIVA de empresa destino
                        // IMPORTANTE: Hacerlo ANTES de insertar
                        var gestorPedidos = new GestorPedidosVenta(servicioPedidos);
                        gestorPedidos.RecalcularImportesLineasPedido(pedidoNuevo);

                        // 7. INSERTAR el nuevo pedido PRIMERO (orden seguro)
                        // Ahora SÍ usamos DbContext, pero SOLO para operaciones CRUD (no procedimientos)
                        // Esto NO causará el error UnintentionalCodeFirstException
                        using (var dbContext = new NVEntities(conn, contextOwnsConnection: false))
                        {
                            dbContext.Database.UseTransaction(tx);

                            // Si esto falla, no hemos borrado nada todavía
                            dbContext.CabPedidoVtas.Add(pedidoNuevo);
                            await dbContext.SaveChangesAsync();
                        } // Dispose dbContext (pero NO cierra la conexión por contextOwnsConnection: false)

                        // 8. AHORA sí, eliminar el pedido original usando otro contexto
                        // Usamos otro contexto para evitar conflictos de tracking
                        // PERO usamos la MISMA conexión y transacción
                        using (var dbDelete = new NVEntities(conn, contextOwnsConnection: false))
                        {
                            dbDelete.Database.UseTransaction(tx);

                            // Cargar el pedido original con sus líneas
                            var pedidoOriginal = await dbDelete.CabPedidoVtas
                                .Include(p => p.LinPedidoVtas)
                                .FirstOrDefaultAsync(p =>
                                    p.Empresa == empresaOrigen.Trim() &&
                                    p.Número == numeroPedido);

                            if (pedidoOriginal != null)
                            {
                                // Eliminar líneas primero (restricción FK)
                                if (pedidoOriginal.LinPedidoVtas != null && pedidoOriginal.LinPedidoVtas.Any())
                                {
                                    dbDelete.LinPedidoVtas.RemoveRange(pedidoOriginal.LinPedidoVtas);
                                }

                                // Eliminar cabecera
                                dbDelete.CabPedidoVtas.Remove(pedidoOriginal);

                                // Guardar cambios (dentro de la misma transacción tx)
                                await dbDelete.SaveChangesAsync();
                            }
                        }

                        // 9. Si llegamos aquí, TODO salió bien
                        // Commit() commitea la transacción
                        tx.Commit();
                    }
                    catch
                    {
                        // Rollback en caso de error
                        // NOTA: prdCopiarProducto con COMMIT interno NO se revierte (no crítico)
                        tx.Rollback();
                        throw;
                    }
                }
            }

            // Actualizar el objeto pedido original con la empresa destino
            // (para que el caller tenga el objeto actualizado)
            pedido.Empresa = empresaDestino.Trim();
            if (pedido.LinPedidoVtas != null)
            {
                foreach (var linea in pedido.LinPedidoVtas)
                {
                    linea.Empresa = empresaDestino.Trim();
                }
            }
        }
    }
}
