using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Data;
using System.Data.Common;
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
    ///    - Reutiliza la conexión del DbContext (no crea una nueva)
    ///    - Usa transacción del DbContext
    ///    - Copia cliente usando prdCopiarCliente (SqlCommand con parámetros)
    ///    - Copia productos usando prdCopiarProducto (SqlCommand con parámetros)
    ///    - Copia cuentas contables usando prdCopiarCuentaContable (SqlCommand con parámetros)
    ///    - Actualiza empresa en cabecera y líneas
    ///    - Recalcula IVA/RE/Total con ParámetrosIVA de empresa destino
    ///    - Guarda cambios de forma transaccional
    ///
    /// MEJORAS:
    /// - Reutiliza la conexión del DbContext (evita inconsistencias)
    /// - Usa parámetros en SqlCommand (protección contra inyección SQL)
    /// - Verifica estado de conexión antes de abrirla
    /// - Los procedimientos almacenados se ejecutan con SqlCommand directamente
    ///   para evitar el error UnintentionalCodeFirstException
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
        /// IMPORTANTE: Usa SIEMPRE parámetros para evitar inyección SQL.
        /// </summary>
        private async Task ExecuteStoredProcedureAsync(
            DbConnection connection,
            DbTransaction transaction,
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
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.Add(param);
                    }
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
        /// PROCESO SEGURO:
        /// 1. Validar parámetros (pedido no null, empresas no vacías, diferentes)
        /// 2. Verificar que pedido.Empresa == empresaOrigen
        /// 3. Reutilizar conexión del DbContext + transacción:
        ///    a. Copiar cliente con prdCopiarCliente (SqlCommand con parámetros)
        ///    b. Copiar productos con prdCopiarProducto (SqlCommand con parámetros)
        ///    c. Copiar cuentas contables con prdCopiarCuentaContable (SqlCommand con parámetros)
        ///    d. Clonar el pedido con empresa destino
        ///    e. INSERTAR el nuevo pedido (empresa destino) usando EF
        ///    f. Recalcular importes
        ///    g. ELIMINAR el pedido original (empresa origen) usando EF
        ///    h. Commit() - solo si todo salió bien
        ///
        /// VENTAJAS:
        /// - Reutiliza conexión del DbContext (evita inconsistencias)
        /// - Usa parámetros SQL (protección contra inyección)
        /// - Verifica estado de conexión
        /// - NO usa DbContext para procedimientos almacenados (evita UnintentionalCodeFirstException)
        /// - Orden seguro: INSERT antes de DELETE (nunca perdemos el pedido original)
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

            // 3. Reutilizar la conexión del DbContext
            var connection = db.Database.Connection;
            bool connectionWasOpen = connection.State == ConnectionState.Open;

            try
            {
                // Abrir conexión solo si no está abierta
                if (!connectionWasOpen)
                {
                    await connection.OpenAsync();
                }

                // Crear transacción desde el DbContext
                using (var transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        // 4. Copiar cliente a la empresa destino usando SqlCommand con parámetros
                        // NOTA: prdCopiarCliente NO tiene transacción interna → se revierte con transaction
                        await ExecuteStoredProcedureAsync(
                            connection,
                            transaction.UnderlyingTransaction,
                            "prdCopiarCliente",
                            new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
                            new SqlParameter("@EmpresaDestino", SqlDbType.NVarChar, 10) { Value = empresaDestino.Trim() },
                            new SqlParameter("@NumCliente", SqlDbType.NVarChar, 10) { Value = (object)clienteNumero ?? DBNull.Value }
                        );

                        // 5. Copiar productos y cuentas contables del pedido
                        if (pedido.LinPedidoVtas != null && pedido.LinPedidoVtas.Any())
                        {
                            // 5a. Copiar productos únicos (líneas tipo PRODUCTO = 1)
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
                                    connection,
                                    transaction.UnderlyingTransaction,
                                    "prdCopiarProducto",
                                    new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
                                    new SqlParameter("@EmpresaDestino", SqlDbType.NVarChar, 10) { Value = empresaDestino.Trim() },
                                    new SqlParameter("@NumProducto", SqlDbType.NVarChar, 20) { Value = producto }
                                );
                            }

                            // 5b. Copiar cuentas contables únicas (líneas tipo CUENTA_CONTABLE = 2)
                            // NOTA: prdCopiarCuentaContable NO tiene transacción interna → se revierte con transaction
                            var cuentasContablesUnicas = pedido.LinPedidoVtas
                                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)
                                .Select(l => l.Producto?.Trim())
                                .Where(c => !string.IsNullOrEmpty(c))
                                .Distinct()
                                .ToList();

                            foreach (var cuenta in cuentasContablesUnicas)
                            {
                                await ExecuteStoredProcedureAsync(
                                    connection,
                                    transaction.UnderlyingTransaction,
                                    "prdCopiarCuentaContable",
                                    new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
                                    new SqlParameter("@EmpresaDestino", SqlDbType.NVarChar, 10) { Value = empresaDestino.Trim() },
                                    new SqlParameter("@NumCuenta", SqlDbType.NVarChar, 20) { Value = cuenta }
                                );
                            }
                        }

                        // 6. Clonar el pedido para empresa destino
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

                        // 7. Recalcular importes con ParámetrosIVA de empresa destino
                        // IMPORTANTE: Hacerlo ANTES de insertar
                        var gestorPedidos = new GestorPedidosVenta(servicioPedidos);
                        gestorPedidos.RecalcularImportesLineasPedido(pedidoNuevo);

                        // 8. INSERTAR el nuevo pedido PRIMERO (orden seguro)
                        // Si esto falla, no hemos borrado nada todavía
                        db.CabPedidoVtas.Add(pedidoNuevo);
                        await db.SaveChangesAsync();

                        // 9. AHORA sí, eliminar el pedido original
                        // Cargar el pedido original con sus líneas (en el mismo contexto)
                        var pedidoOriginal = await db.CabPedidoVtas
                            .Include(p => p.LinPedidoVtas)
                            .FirstOrDefaultAsync(p =>
                                p.Empresa == empresaOrigen.Trim() &&
                                p.Número == numeroPedido);

                        if (pedidoOriginal != null)
                        {
                            // Eliminar líneas primero (restricción FK)
                            if (pedidoOriginal.LinPedidoVtas != null && pedidoOriginal.LinPedidoVtas.Any())
                            {
                                db.LinPedidoVtas.RemoveRange(pedidoOriginal.LinPedidoVtas);
                            }

                            // Eliminar cabecera
                            db.CabPedidoVtas.Remove(pedidoOriginal);

                            // Guardar cambios (dentro de la misma transacción)
                            await db.SaveChangesAsync();
                        }

                        // 10. Si llegamos aquí, TODO salió bien
                        transaction.Commit();
                    }
                    catch
                    {
                        // Rollback en caso de error
                        // NOTA: prdCopiarProducto con COMMIT interno NO se revierte (no crítico)
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            finally
            {
                // Solo cerrar la conexión si nosotros la abrimos
                if (!connectionWasOpen && connection.State == ConnectionState.Open)
                {
                    connection.Close();
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
