using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;

namespace NestoAPI.Infraestructure.Ubicaciones
{
    /// <summary>
    /// NestoAPI#594 (corte 2): lo que la puerta registra va a la tabla UbicacionesMovimientos (Scripts/Issue594_UbicacionesMovimientos.sql),
    /// una fila por cambio, DENTRO de la transacción del que llama (mismo NVEntities: si la operación se deshace, el
    /// movimiento también).
    ///
    /// INTERRUPTOR: <c>Ubicaciones:RegistrarMovimientos</c> en Web.config. Por defecto APAGADO (falta la clave o no dice
    /// "true"): no se escribe nada, solo la traza de Debug de siempre. OJO: el valor del Web.config del repo es el que se
    /// publica (encendido a mano en el servidor, la siguiente publicación lo apagaría).
    ///
    /// Sin la tabla (script sin lanzar) no hay registro: el INSERT va detrás de OBJECT_ID y no hace nada, como
    /// ReposicionesTraspasos o los cheques regalo. Cualquier otro error al apuntar (p. ej. falta el GRANT) se deja en ELMAH
    /// y la operación de negocio sigue: el registro es un testigo, no puede tumbar una reposición.
    /// </summary>
    public sealed class RegistroMovimientosUbicacionSql : IRegistroMovimientosUbicacion
    {
        internal const string CLAVE_INTERRUPTOR = "Ubicaciones:RegistrarMovimientos";
        internal const string USUARIO_ELMAH = "Sistema (registro de movimientos de Ubicaciones)";

        internal const string SQL_INSERTAR = @"
IF OBJECT_ID(N'dbo.UbicacionesMovimientos', N'U') IS NOT NULL
    INSERT INTO dbo.UbicacionesMovimientos (Tipo, Empresa, Almacen, Producto, NumOrdenUbicacion,
        PasilloOrigen, FilaOrigen, ColumnaOrigen, PasilloDestino, FilaDestino, ColumnaDestino,
        Cantidad, EstadoAnterior, EstadoNuevo, TipoDocumento, NumDocumento, Usuario, Momento)
    VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17)";

        private readonly Func<bool> activo;
        private readonly Action<string, SqlParameter[]> ejecutar;
        private readonly Action<Exception> avisar;

        /// <summary>El de la API: escribe con el mismo NVEntities que la puerta (y por tanto en su transacción).</summary>
        public RegistroMovimientosUbicacionSql(NVEntities db)
            : this(EstaActivo, (sql, parametros) => db.Database.ExecuteSqlCommand(sql, parametros), ex => ElmahHelper.Log(ex, USUARIO_ELMAH))
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }
        }

        /// <param name="activo">El interruptor (se lee en cada movimiento: cambiar el Web.config no exige reiniciar nada más).</param>
        /// <param name="ejecutar">(sql, parámetros) → lo lanza contra la BD.</param>
        /// <param name="avisar">Qué hacer si apuntar falla (ELMAH). Nunca relanza.</param>
        internal RegistroMovimientosUbicacionSql(Func<bool> activo, Action<string, SqlParameter[]> ejecutar, Action<Exception> avisar = null)
        {
            this.activo = activo ?? (() => false);
            this.ejecutar = ejecutar ?? throw new ArgumentNullException(nameof(ejecutar));
            this.avisar = avisar ?? (_ => { });
        }

        /// <summary>Encendido solo con "true" explícito: escribir en una tabla nueva tiene que ser una decisión, no un descuido.</summary>
        internal static bool EstaActivo(string valorConfigurado)
        {
            return bool.TryParse(valorConfigurado?.Trim(), out bool encendido) && encendido;
        }

        internal static bool EstaActivo()
        {
            return EstaActivo(ConfigurationManager.AppSettings[CLAVE_INTERRUPTOR]);
        }

        public void Registrar(MovimientoUbicacion movimiento)
        {
            if (movimiento == null)
            {
                return;
            }
            Debug.WriteLine("[Ubicaciones] " + movimiento);
            if (!activo())
            {
                return;
            }
            try
            {
                ejecutar(SQL_INSERTAR, Parametros(movimiento).ToArray());
            }
            catch (Exception ex)
            {
                avisar(new Exception($"[Ubicaciones #594] No se ha podido apuntar el movimiento en UbicacionesMovimientos: {movimiento}. {ex.Message}", ex));
            }
        }

        /// <summary>Los 18 valores de la fila, en el orden de <see cref="SQL_INSERTAR"/> (@p0…@p17).</summary>
        internal static List<SqlParameter> Parametros(MovimientoUbicacion m)
        {
            return new List<SqlParameter>
            {
                VarChar("@p0", m.Tipo.ToString(), 30),
                Char("@p1", m.Empresa?.Trim(), 3),
                Char("@p2", m.Almacen?.Trim(), 3),
                Char("@p3", m.Producto?.Trim(), 15),
                Entero("@p4", m.NumeroOrdenUbicacion),
                Char("@p5", Vacio(m.HuecoOrigen?.Pasillo), 3),
                Char("@p6", Vacio(m.HuecoOrigen?.Fila), 3),
                Char("@p7", Vacio(m.HuecoOrigen?.Columna), 3),
                Char("@p8", Vacio(m.HuecoDestino?.Pasillo), 3),
                Char("@p9", Vacio(m.HuecoDestino?.Fila), 3),
                Char("@p10", Vacio(m.HuecoDestino?.Columna), 3),
                Entero("@p11", m.Cantidad),
                Entero("@p12", m.EstadoAnterior),
                Entero("@p13", m.EstadoNuevo),
                VarChar("@p14", m.Documento == null || m.Documento.Tipo == TipoDocumentoUbicacion.Ninguno ? null : m.Documento.Tipo.ToString(), 20),
                Entero("@p15", m.Documento == null || m.Documento.Tipo == TipoDocumentoUbicacion.Ninguno ? (int?)null : m.Documento.Numero),
                VarChar("@p16", UsuarioAuditoriaHelper.ParaAuditoria(m.Usuario), 30),
                new SqlParameter("@p17", SqlDbType.DateTime) { Value = m.Momento == default ? DateTime.Now : m.Momento }
            };
        }

        private static string Vacio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        private static SqlParameter Char(string nombre, string valor, int longitud) =>
            new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter VarChar(string nombre, string valor, int longitud) =>
            new SqlParameter(nombre, SqlDbType.VarChar, longitud) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter Entero(string nombre, int? valor) =>
            new SqlParameter(nombre, SqlDbType.Int) { Value = (object)valor ?? DBNull.Value };
    }
}
