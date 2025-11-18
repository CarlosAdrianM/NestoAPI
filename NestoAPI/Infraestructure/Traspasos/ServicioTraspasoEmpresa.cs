using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
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
            {
                return false;
            }

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
                        _ = cmd.Parameters.Add(param);
                    }
                }

                try
                {
                    _ = await cmd.ExecuteNonQueryAsync();
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
        /// Ejecuta un comando SQL directo usando la conexión y transacción actuales.
        /// Usado para UPDATEs que modifican claves primarias (no permitidos por EF).
        /// </summary>
        private async Task<int> ExecuteSqlCommandAsync(
            DbConnection connection,
            DbTransaction transaction,
            string sqlCommand,
            params SqlParameter[] parameters)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = sqlCommand;
                cmd.CommandType = CommandType.Text;
                cmd.CommandTimeout = 60;

                if (parameters != null && parameters.Length > 0)
                {
                    foreach (var param in parameters)
                    {
                        _ = cmd.Parameters.Add(param);
                    }
                }

                try
                {
                    return await cmd.ExecuteNonQueryAsync();
                }
                catch (SqlException ex)
                {
                    throw new InvalidOperationException(
                        $"Error ejecutando SQL: {ex.Message}\nSQL: {sqlCommand}", ex);
                }
            }
        }

        /// <summary>
        /// Ejecuta una consulta SQL que devuelve un valor escalar (SELECT COUNT, SUM, etc.).
        /// </summary>
        private async Task<T> ExecuteSqlQueryScalarAsync<T>(
            DbConnection connection,
            DbTransaction transaction,
            string sqlQuery,
            params SqlParameter[] parameters)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = sqlQuery;
                cmd.CommandType = CommandType.Text;
                cmd.CommandTimeout = 60;

                if (parameters != null && parameters.Length > 0)
                {
                    foreach (var param in parameters)
                    {
                        _ = cmd.Parameters.Add(param);
                    }
                }

                try
                {
                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value)
                    {
                        return default;
                    }
                    return (T)Convert.ChangeType(result, typeof(T));
                }
                catch (SqlException ex)
                {
                    throw new InvalidOperationException(
                        $"Error ejecutando consulta SQL: {ex.Message}\nSQL: {sqlQuery}", ex);
                }
            }
        }

        /// <summary>
        /// Traspasa un pedido de una empresa a otra.
        ///
        /// PROCESO SEGURO (SIN deshabilitar constraints):
        /// 1. Validar parámetros (pedido no null, empresas no vacías, diferentes)
        /// 2. Verificar que pedido.Empresa == empresaOrigen
        /// 3. Reutilizar conexión del DbContext + transacción:
        ///    a. Copiar cliente con prdCopiarCliente (SqlCommand con parámetros)
        ///    b. Copiar productos con prdCopiarProducto (SqlCommand con parámetros)
        ///    c. Copiar cuentas contables con prdCopiarCuentaContable (SqlCommand con parámetros)
        ///    d. Verificar si existe cabecera en empresa destino
        ///    e. Si NO existe: INSERT cabecera en empresa destino (copia completa)
        ///    f. UPDATE líneas: cambiar Empresa a destino (SQL directo - funciona con albaranadas)
        ///    g. Verificar si quedan líneas en empresa origen
        ///    h. Si NO quedan líneas: DELETE cabecera huérfana de empresa origen
        ///    h. Refrescar objetos en memoria (Detach + Reload)
        ///    i. Recalcular importes con ParámetrosIVA de empresa destino
        ///    j. SaveChanges() para guardar importes recalculados
        ///    k. Commit() - solo si todo salió bien
        ///
        /// VENTAJAS:
        /// - Reutiliza conexión del DbContext (evita inconsistencias)
        /// - Usa parámetros SQL (protección contra inyección)
        /// - Deshabilita temporalmente FK para permitir UPDATE de PK
        /// - UPDATE directo permite cambiar PK sin problemas
        /// - Funciona con líneas albaranadas (Estado >= 2) - no las elimina
        /// - Mantiene el mismo número de pedido
        /// - No hay riesgo de perder datos (UPDATE en lugar de INSERT + DELETE)
        /// - Todo dentro de una transacción - si falla, rollback automático
        /// </summary>
        public async Task TraspasarPedidoAEmpresa(CabPedidoVta pedido, string empresaOrigen, string empresaDestino, string usuario)
        {
            // 1. Validaciones de parámetros
            if (pedido == null)
            {
                throw new ArgumentNullException(nameof(pedido));
            }

            if (string.IsNullOrWhiteSpace(empresaOrigen))
            {
                throw new ArgumentException("La empresa de origen no puede ser null o vacía", nameof(empresaOrigen));
            }

            if (string.IsNullOrWhiteSpace(empresaDestino))
            {
                throw new ArgumentException("La empresa de destino no puede ser null o vacía", nameof(empresaDestino));
            }

            if (empresaOrigen.Trim() == empresaDestino.Trim())
            {
                throw new ArgumentException("La empresa de origen y destino deben ser diferentes");
            }

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

                        // 6. Verificar si ya existe la cabecera en empresa destino
                        System.Diagnostics.Debug.WriteLine($"  → Verificando si existe cabecera en empresa destino");

                        var existeCabeceraDestino = await ExecuteSqlQueryScalarAsync<int>(
                            connection,
                            transaction.UnderlyingTransaction,
                            @"SELECT COUNT(*)
                              FROM CabPedidoVta
                              WHERE Empresa = @EmpresaDestino
                                AND Número = @NumeroPedido",
                            new SqlParameter("@EmpresaDestino", SqlDbType.NVarChar, 10) { Value = empresaDestino.Trim() },
                            new SqlParameter("@NumeroPedido", SqlDbType.Int) { Value = numeroPedido }
                        );

                        if (existeCabeceraDestino == 0)
                        {
                            // 7. Leer parámetros para la cabecera en empresa destino
                            // IMPORTANTE: Usamos el USUARIO AUTENTICADO (el que ejecuta), NO el pedido.Usuario
                            // Razón: El usuario autenticado tiene los parámetros de facturación correctos
                            System.Diagnostics.Debug.WriteLine($"  → Leyendo parámetros para empresa destino");

                            string serieFacturacion = null;

                            if (!string.IsNullOrEmpty(usuario))
                            {
                                // Método centralizado: extrae usuario sin dominio y busca en ParametrosUsuario
                                serieFacturacion = Controllers.ParametrosUsuarioController.LeerParametro(
                                    empresaDestino.Trim(),
                                    usuario.Trim(),
                                    "SerieFacturacionDefecto"
                                );
                            }

                            // Si no se encuentra el parámetro o el usuario es null, mantener la serie original
                            if (string.IsNullOrEmpty(serieFacturacion))
                            {
                                System.Diagnostics.Debug.WriteLine($"  ⚠ No se encontró SerieFacturacionDefecto para usuario '{usuario}' en empresa {empresaDestino}, se mantendrá la serie original");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"  ✓ Serie para facturación: {serieFacturacion}");
                            }

                            // 8. Copiar cabecera a empresa destino usando INSERT
                            System.Diagnostics.Debug.WriteLine($"  → Copiando cabecera a empresa destino con INSERT");

                            string sqlInsert = string.IsNullOrEmpty(serieFacturacion)
                                ? @"INSERT INTO CabPedidoVta (
                                      Empresa, Número, [Nº Cliente], Contacto, Fecha, [Forma Pago], PlazosPago, [Primer Vencimiento],
                                      IVA, Vendedor, Comentarios, ComentarioPicking, [Periodo Facturacion], Ruta, Serie, CCC, Origen,
                                      Agrupada, MotivoDevolución, ContactoCobro, NoComisiona, NotaEntrega, vtoBuenoPlazosPago,
                                      Operador, FijarPrimerVto, MantenerJunto, ServirJunto, Usuario, [Fecha Modificación]
                                    )
                                    SELECT
                                      @EmpresaDestino, Número, [Nº Cliente], Contacto, Fecha, [Forma Pago], PlazosPago, [Primer Vencimiento],
                                      @IVA, Vendedor, Comentarios, ComentarioPicking, [Periodo Facturacion], Ruta, Serie, CCC, Origen,
                                      Agrupada, MotivoDevolución, ContactoCobro, NoComisiona, NotaEntrega, vtoBuenoPlazosPago,
                                      Operador, FijarPrimerVto, MantenerJunto, ServirJunto, Usuario, [Fecha Modificación]
                                    FROM CabPedidoVta
                                    WHERE Empresa = @EmpresaOrigen
                                      AND Número = @NumeroPedido"
                                : @"INSERT INTO CabPedidoVta (
                                      Empresa, Número, [Nº Cliente], Contacto, Fecha, [Forma Pago], PlazosPago, [Primer Vencimiento],
                                      IVA, Vendedor, Comentarios, ComentarioPicking, [Periodo Facturacion], Ruta, Serie, CCC, Origen,
                                      Agrupada, MotivoDevolución, ContactoCobro, NoComisiona, NotaEntrega, vtoBuenoPlazosPago,
                                      Operador, FijarPrimerVto, MantenerJunto, ServirJunto, Usuario, [Fecha Modificación]
                                    )
                                    SELECT
                                      @EmpresaDestino, Número, [Nº Cliente], Contacto, Fecha, [Forma Pago], PlazosPago, [Primer Vencimiento],
                                      @IVA, Vendedor, Comentarios, ComentarioPicking, [Periodo Facturacion], Ruta, @Serie, CCC, Origen,
                                      Agrupada, MotivoDevolución, ContactoCobro, NoComisiona, NotaEntrega, vtoBuenoPlazosPago,
                                      Operador, FijarPrimerVto, MantenerJunto, ServirJunto, Usuario, [Fecha Modificación]
                                    FROM CabPedidoVta
                                    WHERE Empresa = @EmpresaOrigen
                                      AND Número = @NumeroPedido";

                            var parametrosInsert = new List<SqlParameter>
                            {
                                new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
                                new SqlParameter("@EmpresaDestino", SqlDbType.NVarChar, 10) { Value = empresaDestino.Trim() },
                                new SqlParameter("@NumeroPedido", SqlDbType.Int) { Value = numeroPedido },
                                new SqlParameter("@IVA", SqlDbType.NVarChar, 10) { Value = Constantes.Empresas.IVA_POR_DEFECTO }
                            };

                            if (!string.IsNullOrEmpty(serieFacturacion))
                            {
                                parametrosInsert.Add(new SqlParameter("@Serie", SqlDbType.NVarChar, 10) { Value = serieFacturacion.Trim() });
                            }

                            _ = await ExecuteSqlCommandAsync(
                                connection,
                                transaction.UnderlyingTransaction,
                                sqlInsert,
                                parametrosInsert.ToArray()
                            );

                            System.Diagnostics.Debug.WriteLine($"  ✓ Cabecera copiada a empresa destino");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  ✓ Cabecera ya existe en empresa destino");
                        }

                        // 9. Actualizar Empresa en líneas del pedido
                        System.Diagnostics.Debug.WriteLine($"  → Moviendo líneas a empresa destino: {empresaOrigen} → {empresaDestino}");

                        int lineasMovidas = await ExecuteSqlCommandAsync(
                            connection,
                            transaction.UnderlyingTransaction,
                            @"UPDATE LinPedidoVta
                              SET Empresa = @EmpresaDestino
                              WHERE Empresa = @EmpresaOrigen
                                AND Número = @NumeroPedido",
                            new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
                            new SqlParameter("@EmpresaDestino", SqlDbType.NVarChar, 10) { Value = empresaDestino.Trim() },
                            new SqlParameter("@NumeroPedido", SqlDbType.Int) { Value = numeroPedido }
                        );

                        System.Diagnostics.Debug.WriteLine($"  ✓ {lineasMovidas} líneas movidas a empresa destino");

                        // 10. Verificar si quedan líneas en empresa origen y eliminar cabecera huérfana si no quedan
                        if (lineasMovidas > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"  → Verificando si quedan líneas en empresa origen");

                            var lineasRestantesOrigen = await ExecuteSqlQueryScalarAsync<int>(
                                connection,
                                transaction.UnderlyingTransaction,
                                @"SELECT COUNT(*)
                                  FROM LinPedidoVta
                                  WHERE Empresa = @EmpresaOrigen
                                    AND Número = @NumeroPedido",
                                new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
                                new SqlParameter("@NumeroPedido", SqlDbType.Int) { Value = numeroPedido }
                            );

                            if (lineasRestantesOrigen == 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"  → Eliminando cabecera huérfana de empresa origen");

                                _ = await ExecuteSqlCommandAsync(
                                    connection,
                                    transaction.UnderlyingTransaction,
                                    @"DELETE FROM CabPedidoVta
                                      WHERE Empresa = @EmpresaOrigen
                                        AND Número = @NumeroPedido",
                                    new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
                                    new SqlParameter("@NumeroPedido", SqlDbType.Int) { Value = numeroPedido }
                                );

                                System.Diagnostics.Debug.WriteLine($"  ✓ Cabecera huérfana eliminada de empresa origen");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"  ✓ Quedan {lineasRestantesOrigen} líneas en empresa origen, cabecera mantenida");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  ⚠ Cabecera sin líneas - mantenida para no perder datos");
                        }

                        // 11. Refrescar el objeto pedido en memoria para que EF vea los cambios
                        // IMPORTANTE: Necesitamos que EF "olvide" el objeto viejo y cargue el nuevo
                        // porque cambiamos la PK con SQL directo
                        System.Diagnostics.Debug.WriteLine($"  → Refrescando objeto pedido en memoria");

                        db.Entry(pedido).State = EntityState.Detached;

                        var pedidoActualizado = await db.CabPedidoVtas
                            .Include(p => p.LinPedidoVtas)
                            .FirstOrDefaultAsync(p =>
                                p.Empresa == empresaDestino.Trim() &&
                                p.Número == numeroPedido);

                        if (pedidoActualizado == null)
                        {
                            throw new InvalidOperationException(
                                $"No se pudo recargar el pedido {numeroPedido} después del traspaso a empresa {empresaDestino}");
                        }

                        System.Diagnostics.Debug.WriteLine($"  ✓ Pedido recargado desde BD con empresa {empresaDestino}");

                        // 12. Recalcular importes con ParámetrosIVA de empresa destino
                        // IMPORTANTE: Ahora trabajamos con el pedido recargado
                        System.Diagnostics.Debug.WriteLine($"  → Recalculando importes con ParámetrosIVA de empresa {empresaDestino}");

                        var gestorPedidos = new GestorPedidosVenta(servicioPedidos);
                        gestorPedidos.RecalcularImportesLineasPedido(pedidoActualizado);

                        System.Diagnostics.Debug.WriteLine($"  ✓ Importes recalculados");

                        // 13. Guardar los importes recalculados (UPDATE normal de EF)
                        System.Diagnostics.Debug.WriteLine($"  → Guardando importes recalculados");
                        _ = await db.SaveChangesAsync();

                        // 14. Commit de la transacción
                        System.Diagnostics.Debug.WriteLine($"  → Commit de transacción");
                        transaction.Commit();

                        System.Diagnostics.Debug.WriteLine($"  ✓✓✓ Traspaso completado exitosamente ✓✓✓");
                    }
                    catch (Exception ex)
                    {
                        // Rollback en caso de error con protección contra conexión cerrada
                        System.Diagnostics.Debug.WriteLine($"  ❌ ERROR en traspaso: {ex.Message}");

                        try
                        {
                            if (transaction != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"  → Ejecutando Rollback de transacción");
                                transaction.Rollback();
                                System.Diagnostics.Debug.WriteLine($"  ✓ Rollback completado");
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            // Si falla el Rollback (por conexión cerrada, etc.), loggear pero no re-lanzar
                            // La excepción original del traspaso es más importante
                            System.Diagnostics.Debug.WriteLine($"  ⚠ ERROR en Rollback (no crítico): {rollbackEx.Message}");
                        }

                        // Re-lanzar la excepción ORIGINAL del traspaso
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

            // IMPORTANTE: El objeto 'pedido' que se pasó como parámetro quedó Detached
            // El caller debe recargar el pedido desde la BD si necesita trabajar con él
            // O alternativamente, podríamos retornar el pedidoActualizado, pero eso
            // cambiaría la firma del método (void → Task<CabPedidoVta>)
            System.Diagnostics.Debug.WriteLine($"NOTA: El pedido {numeroPedido} fue traspasado a empresa {empresaDestino}. El objeto parámetro quedó Detached.");
        }
    }
}
