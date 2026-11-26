using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#880, corte 2 (26/11/26): bultos pendientes de una entrega incompleta, con cierre a mano.
    //
    //  - Cada bulto que FALTA en una entrega incompleta (corte 1: EntregaIncompletaReparto) queda como una fila Pendiente en
    //    dbo.RepartoBultosPendientes (Scripts/Issue880_RepartoBultosPendientes.sql, fuera del EDMX; sin ella, nada se apunta y los
    //    endpoints dan 503). Idempotente por el código del bulto (RP{envío}-{n}): un reintento de la entrega no lo duplica.
    //  - GET api/RepartoPropio/BultosPendientes (Agencias y la pestaña Entrega del pedido en Nesto) con dónde está AHORA cada bulto
    //    (último movimiento de la furgoneta, #772).
    //  - Cerrar a mano con motivo (decisión 9 de Carlos, 19/11/26: Almacén, y Dirección/Informática): EntregadoEnMano, Perdido o Abonado.
    //    EntregadoEnMano deja además el movimiento Entregado del bulto (Origen Almacen) en la MISMA transacción: el bulto no se queda
    //    «en una furgoneta» ni «en el almacén» para siempre. Rastro en Modificaciones (Tabla RepartoBultoPendienteCerrado).
    //  - Recordatorio diario (job reparto-bultos-pendientes L-V 9:00): campana a Almacén con lo que sigue pendiente.
    //  - Seguimiento público (decisión 8): «Entregado parcialmente» mientras quede algún bulto pendiente del envío.
    //  - NO en este corte: la segunda parada al escanear el bulto (corte 4) ni la propuesta de rectificativa/inventario del perdido
    //    (decisión 10, sin definir qué se propone).
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Una fila de RepartoBultosPendientes.</summary>
    public class BultoPendienteReparto
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int EnvioId { get; set; }
        public int? Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Codigo { get; set; }
        public int Bulto { get; set; }
        public int Total { get; set; }
        /// <summary>Por qué faltaba al entregar (<see cref="SituacionBultoEntrega"/>: NoLeidoSeQueda, NoSeCargo, VolvioAlAlmacen, LoLlevaOtro).</summary>
        public string SituacionAlEntregar { get; set; }
        /// <summary>Quien hizo la entrega incompleta.</summary>
        public string Repartidor { get; set; }
        public DateTime FechaEntrega { get; set; }
        public string Estado { get; set; }
        public string Cierre { get; set; }
        public string MotivoCierre { get; set; }
        public string UsuarioCierre { get; set; }
        public DateTime? FechaCierre { get; set; }
        public DateTime FechaRegistro { get; set; }
    }

    /// <summary>Un bulto pendiente tal como lo ven Nesto (Agencias, pestaña Entrega del pedido) y Ariadna.</summary>
    public class BultoPendienteRepartoDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int EnvioId { get; set; }
        public int? Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Codigo { get; set; }
        public int Bulto { get; set; }
        public int Total { get; set; }
        public string SituacionAlEntregar { get; set; }
        /// <summary>«no se cargó», «lo lleva Miguel»… (el motivo de la falta, de usted).</summary>
        public string MotivoFalta { get; set; }
        public string Repartidor { get; set; }
        public DateTime FechaEntrega { get; set; }
        /// <summary>Pendiente o Cerrado.</summary>
        public string Estado { get; set; }
        /// <summary>EntregadoEnMano, Perdido o Abonado (null si sigue pendiente).</summary>
        public string Cierre { get; set; }
        public string MotivoCierre { get; set; }
        public string UsuarioCierre { get; set; }
        public DateTime? FechaCierre { get; set; }
        /// <summary>Dónde está ahora por la furgoneta (#772): «En la furgoneta de Miguel desde el 26/11 a las 9:42», «En el almacén…»; null si no se sabe.</summary>
        public string DondeEsta { get; set; }
        /// <summary>«RP251099-5 (bulto 5 de 5) del envío 251099: no se cargó. Pendiente desde el 26/11/2026.»</summary>
        public string Texto { get; set; }
    }

    /// <summary>POST api/RepartoPropio/BultosPendientes/{id}/Cerrar.</summary>
    public class CerrarBultoPendienteDTO
    {
        /// <summary>EntregadoEnMano, Perdido o Abonado.</summary>
        public string Cierre { get; set; }
        /// <summary>Obligatorio, 1..500.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>Qué leer en la lista (todo opcional).</summary>
    public class FiltroBultosPendientes
    {
        public bool SoloPendientes { get; set; } = true;
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        public int? Envio { get; set; }
        /// <summary>Solo con <see cref="SoloPendientes"/> = false: los cerrados de los últimos N días (los pendientes, todos).</summary>
        public int Dias { get; set; } = 30;
    }

    /// <summary>NestoAPI#880 corte 2: LAS REGLAS (puras) de los bultos pendientes.</summary>
    public static class ReglasBultosPendientes
    {
        public const string TABLA = "RepartoBultosPendientes";
        public const string TABLA_MODIFICACIONES = "RepartoBultoPendienteCerrado";
        public const string ESTADO_PENDIENTE = "Pendiente";
        public const string ESTADO_CERRADO = "Cerrado";
        public const string CIERRE_ENTREGADO_EN_MANO = "EntregadoEnMano";
        public const string CIERRE_PERDIDO = "Perdido";
        public const string CIERRE_ABONADO = "Abonado";
        public static readonly IReadOnlyList<string> CIERRES = new[] { CIERRE_ENTREGADO_EN_MANO, CIERRE_PERDIDO, CIERRE_ABONADO };
        public const int LONGITUD_MOTIVO = 500;
        public const string TIPO_NOTIFICACION_RECORDATORIO = "BultosPendientesReparto";
        public const string MARCA_ENTREGADO_EN_MANO = "Entregado en mano: ";

        public const string MENSAJE_SIN_TABLA = "Todavía no se pueden ver ni cerrar los bultos pendientes: falta la tabla RepartoBultosPendientes " +
            "(Scripts/Issue880_RepartoBultosPendientes.sql). Avise a Informática.";
        public const string MENSAJE_SIN_PERMISO_CERRAR = "Solo Almacén (o Dirección) puede cerrar un bulto pendiente: es quien sabe qué ha pasado con él.";

        /// <summary>Una fila Pendiente por cada bulto que falta (solo si la entrega es incompleta).</summary>
        public static List<BultoPendienteReparto> DesdeEvaluacion(EnviosAgencia envio, EvaluacionBultosEntrega evaluacion, string repartidor, DateTime momento)
        {
            if (envio == null || evaluacion == null || !evaluacion.Incompleta)
            {
                return new List<BultoPendienteReparto>();
            }
            return evaluacion.Faltan.Select(b => new BultoPendienteReparto
            {
                Empresa = envio.Empresa?.Trim(),
                EnvioId = envio.Numero,
                Pedido = envio.Pedido,
                Cliente = envio.Cliente?.Trim(),
                Contacto = envio.Contacto?.Trim(),
                Nombre = Recortar(envio.Nombre?.Trim(), 100),
                Codigo = b.Codigo,
                Bulto = b.Bulto,
                Total = evaluacion.Total,
                SituacionAlEntregar = b.Situacion.ToString(),
                Repartidor = Recortar(repartidor?.Trim(), 50),
                FechaEntrega = momento,
                Estado = ESTADO_PENDIENTE
            }).ToList();
        }

        /// <summary>Null si se puede cerrar; si no, el motivo (400 o 409 según <paramref name="conflicto"/>).</summary>
        public static string MotivoNoSePuedeCerrar(BultoPendienteReparto fila, CerrarBultoPendienteDTO peticion, out bool conflicto)
        {
            conflicto = false;
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cierre) || !CIERRES.Any(c => string.Equals(c, peticion.Cierre.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return "Indique cómo se cierra el bulto: EntregadoEnMano (se le ha llevado al cliente), Perdido o Abonado.";
            }
            if (string.IsNullOrWhiteSpace(peticion.Motivo))
            {
                return "Escriba el motivo: queda en la historia del bulto.";
            }
            if (peticion.Motivo.Trim().Length > LONGITUD_MOTIVO)
            {
                return $"El motivo no puede pasar de {LONGITUD_MOTIVO} caracteres.";
            }
            if (fila != null && !string.Equals(fila.Estado?.Trim(), ESTADO_PENDIENTE, StringComparison.OrdinalIgnoreCase))
            {
                conflicto = true;
                return $"El bulto {fila.Codigo} ya está cerrado ({TextoCierre(fila.Cierre)}{(fila.UsuarioCierre == null ? string.Empty : ", " + ServicioRepartoPropio.NombreRepartidor(fila.UsuarioCierre))}).";
            }
            return null;
        }

        /// <summary>El nombre canónico del cierre pedido (sin mayúsculas raras).</summary>
        public static string Canonico(string cierre) => CIERRES.FirstOrDefault(c => string.Equals(c, cierre?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static string TextoCierre(string cierre)
        {
            switch (Canonico(cierre))
            {
                case CIERRE_ENTREGADO_EN_MANO: return "entregado en mano";
                case CIERRE_PERDIDO: return "perdido";
                case CIERRE_ABONADO: return "abonado";
                default: return "cerrado";
            }
        }

        /// <summary>«no se cargó»…, el motivo de la falta con la regla del corte 1.</summary>
        public static string MotivoFalta(string situacion, string repartidor)
        {
            if (!Enum.TryParse(situacion?.Trim(), out SituacionBultoEntrega s))
            {
                return null;
            }
            return EntregaIncompletaReparto.Motivo(new BultoEntregaEvaluado { Situacion = s, Repartidor = s == SituacionBultoEntrega.LoLlevaOtro ? null : repartidor });
        }

        /// <summary>Dónde está el bulto AHORA por su último movimiento de la furgoneta; null si no tiene ninguno.</summary>
        public static string DondeEsta(MovimientoBultoReparto ultimo)
        {
            if (ultimo == null)
            {
                return null;
            }
            string cuando = $"el {ultimo.FechaHora.ToString("dd/MM", CultureInfo.InvariantCulture)} a las {ultimo.FechaHora.ToString("H:mm", CultureInfo.InvariantCulture)}";
            string quien = ServicioRepartoPropio.NombreRepartidor(ultimo.Usuario);
            if (TiposMovimientoBulto.DejaEnFurgoneta(ultimo.Tipo))
            {
                return $"En la furgoneta de {quien} desde {cuando}.";
            }
            if (ultimo.Tipo == TiposMovimientoBulto.DESCARGADO)
            {
                return $"En el almacén: lo descargó {quien} {cuando}.";
            }
            if (ultimo.Tipo == TiposMovimientoBulto.ENTREGADO)
            {
                return $"Entregado ({quien}, {cuando}).";
            }
            return null;
        }

        public static BultoPendienteRepartoDTO ADto(BultoPendienteReparto f, MovimientoBultoReparto ultimo)
        {
            string motivo = MotivoFalta(f.SituacionAlEntregar, null);
            bool pendiente = string.Equals(f.Estado?.Trim(), ESTADO_PENDIENTE, StringComparison.OrdinalIgnoreCase);
            string estado = pendiente
                ? $"Pendiente desde el {f.FechaEntrega.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}."
                : $"Cerrado: {TextoCierre(f.Cierre)}{(f.FechaCierre.HasValue ? " el " + f.FechaCierre.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty)}.";
            return new BultoPendienteRepartoDTO
            {
                Id = f.Id,
                Empresa = f.Empresa?.Trim(),
                EnvioId = f.EnvioId,
                Pedido = f.Pedido,
                Cliente = f.Cliente?.Trim(),
                Contacto = f.Contacto?.Trim(),
                Nombre = f.Nombre?.Trim(),
                Codigo = f.Codigo?.Trim(),
                Bulto = f.Bulto,
                Total = f.Total,
                SituacionAlEntregar = f.SituacionAlEntregar?.Trim(),
                MotivoFalta = motivo,
                Repartidor = ServicioRepartoPropio.NombreRepartidor(f.Repartidor),
                FechaEntrega = f.FechaEntrega,
                Estado = f.Estado?.Trim(),
                Cierre = f.Cierre?.Trim(),
                MotivoCierre = f.MotivoCierre,
                UsuarioCierre = f.UsuarioCierre == null ? null : ServicioRepartoPropio.NombreRepartidor(f.UsuarioCierre),
                FechaCierre = f.FechaCierre,
                DondeEsta = pendiente ? DondeEsta(ultimo) : null,
                Texto = $"{f.Codigo?.Trim()} (bulto {f.Bulto} de {f.Total}) del envío {f.EnvioId}{(motivo == null ? string.Empty : ": " + motivo)}. {estado}"
            };
        }

        /// <summary>
        /// El movimiento Entregado de un bulto cerrado como «entregado en mano» (Origen Almacen, a nombre de quien lo cierra), con clave
        /// derivada del Id de la fila: un reintento no lo duplica.
        /// </summary>
        public static MovimientoBultoReparto MovimientoEntregadoEnMano(BultoPendienteReparto fila, string motivo, string usuario, DateTime momento)
            => new MovimientoBultoReparto
            {
                Codigo = fila.Codigo?.Trim(),
                TipoCodigo = CodigoBultoFurgoneta.RP,
                EnvioId = fila.EnvioId,
                Pedido = fila.Pedido,
                Bulto = fila.Bulto,
                Total = fila.Total,
                Tipo = TiposMovimientoBulto.ENTREGADO,
                Usuario = Recortar(usuario, 50),
                FechaHora = momento,
                FechaRegistro = momento,
                Origen = OrigenesMovimientoBulto.ALMACEN,
                Descripcion = Recortar(MARCA_ENTREGADO_EN_MANO + motivo?.Trim(), 150),
                ClaveIdempotencia = NucleoFurgoneta.ClaveDerivada(Guid.Empty, "BultoPendiente:" + fila.Id.ToString(CultureInfo.InvariantCulture), TiposMovimientoBulto.ENTREGADO)
            };

        /// <summary>Decisión 8: «Entregado parcialmente» mientras el envío esté entregado con entrega incompleta y quede algún bulto pendiente.</summary>
        public static bool EntregaParcial(short estadoEnvio, string detalleEstado, int bultosPendientes)
            => estadoEnvio == Constantes.Agencias.ESTADO_ENTREGADO && EntregaIncompletaReparto.EsIncompleta(detalleEstado) && bultosPendientes > 0;

        /// <summary>La campana diaria a Almacén; null si no hay nada pendiente.</summary>
        public static NotificacionPushDTO Recordatorio(IReadOnlyList<BultoPendienteRepartoDTO> pendientes)
        {
            List<BultoPendienteRepartoDTO> lista = (pendientes ?? new List<BultoPendienteRepartoDTO>()).Where(p => p != null).OrderBy(p => p.FechaEntrega).ToList();
            if (lista.Count == 0)
            {
                return null;
            }
            int envios = lista.Select(p => p.EnvioId).Distinct().Count();
            string lineas = string.Join(" ", lista.Take(10).Select(p => $"{p.Codigo} ({p.Nombre?.Trim() ?? p.Cliente}, desde el {p.FechaEntrega.ToString("dd/MM", CultureInfo.InvariantCulture)}{(p.DondeEsta == null ? string.Empty : "; " + p.DondeEsta.TrimEnd('.'))})."));
            string resto = lista.Count > 10 ? $" Y {lista.Count - 10} más." : string.Empty;
            return new NotificacionPushDTO
            {
                Titulo = lista.Count == 1 ? "Hay 1 bulto pendiente de entregar" : $"Hay {lista.Count} bultos pendientes de entregar",
                Cuerpo = $"Quedan {(lista.Count == 1 ? "1 bulto" : lista.Count + " bultos")} de {(envios == 1 ? "1 envío" : envios + " envíos")} entregados de forma incompleta: " +
                    lineas + resto + " Llévelos al cliente o ciérrelos en Agencias con el motivo.",
                Tipo = TIPO_NOTIFICACION_RECORDATORIO,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION_RECORDATORIO,
                    ["bultos"] = lista.Count.ToString(CultureInfo.InvariantCulture),
                    ["envios"] = string.Join(",", lista.Select(p => p.EnvioId).Distinct())
                }
            };
        }

        internal static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    /// <summary>NestoAPI#880 corte 2: la tabla RepartoBultosPendientes (fuera del EDMX).</summary>
    public interface IRepositorioBultosPendientes
    {
        /// <summary>¿Existe la tabla?</summary>
        bool Disponible();
        /// <summary>Las que no estén ya (por Codigo). Devuelve cuántas ha insertado.</summary>
        Task<int> Registrar(IReadOnlyList<BultoPendienteReparto> filas);
        Task<List<BultoPendienteReparto>> Leer(FiltroBultosPendientes filtro, DateTime hoy);
        Task<BultoPendienteReparto> LeerPorId(int id);
        /// <summary>
        /// En UNA transacción: la fila a Cerrado (solo si sigue Pendiente), el movimiento <paramref name="entregado"/> (si viene) y el
        /// rastro en Modificaciones. False si ya no estaba pendiente (nada cambia).
        /// </summary>
        Task<bool> Cerrar(int id, string cierre, string motivo, string usuario, DateTime momento, MovimientoBultoReparto entregado, string anterior, string nuevo);
        /// <summary>Cuántos bultos del envío siguen pendientes.</summary>
        Task<int> ContarPendientes(int envio);
    }

    public class RepositorioBultosPendientesSql : IRepositorioBultosPendientes
    {
        private const string T = "dbo." + ReglasBultosPendientes.TABLA;
        private const string COLUMNAS = "Id, RTRIM(Empresa) AS Empresa, EnvioId, Pedido, RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, Nombre, RTRIM(Codigo) AS Codigo, " +
            "Bulto, Total, RTRIM(SituacionAlEntregar) AS SituacionAlEntregar, RTRIM(Repartidor) AS Repartidor, FechaEntrega, RTRIM(Estado) AS Estado, RTRIM(Cierre) AS Cierre, " +
            "MotivoCierre, RTRIM(UsuarioCierre) AS UsuarioCierre, FechaCierre, FechaRegistro";

        internal const string SQL_INSERTAR = @"
IF NOT EXISTS (SELECT 1 FROM " + T + @" WHERE Codigo = @Codigo)
    INSERT INTO " + T + @" (Empresa, EnvioId, Pedido, Cliente, Contacto, Nombre, Codigo, Bulto, Total, SituacionAlEntregar, Repartidor, FechaEntrega, Estado, FechaRegistro)
    VALUES (@Empresa, @EnvioId, @Pedido, @Cliente, @Contacto, @Nombre, @Codigo, @Bulto, @Total, @Situacion, @Repartidor, @FechaEntrega, @Pendiente, GETDATE());";

        internal const string SQL_CERRAR = @"
UPDATE " + T + @" SET Estado = @Cerrado, Cierre = @Cierre, MotivoCierre = @Motivo, UsuarioCierre = @Usuario, FechaCierre = @Momento
WHERE Id = @Id AND Estado = @Pendiente;";

        private const string SQL_RASTRO = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @UsuarioRastro);";

        public bool Disponible()
        {
            using (var db = new NVEntities())
            {
                return ColumnasOpcionales.ExisteTabla(db, ReglasBultosPendientes.TABLA);
            }
        }

        public async Task<int> Registrar(IReadOnlyList<BultoPendienteReparto> filas)
        {
            if (filas == null || filas.Count == 0)
            {
                return 0;
            }
            int insertadas = 0;
            using (var db = new NVEntities())
            {
                foreach (BultoPendienteReparto f in filas)
                {
                    try
                    {
                        insertadas += await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR,
                            new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = (object)f.Empresa ?? DBNull.Value },
                            new SqlParameter("@EnvioId", SqlDbType.Int) { Value = f.EnvioId },
                            new SqlParameter("@Pedido", SqlDbType.Int) { Value = (object)f.Pedido ?? DBNull.Value },
                            new SqlParameter("@Cliente", SqlDbType.VarChar, 10) { Value = (object)f.Cliente ?? DBNull.Value },
                            new SqlParameter("@Contacto", SqlDbType.VarChar, 3) { Value = (object)f.Contacto ?? DBNull.Value },
                            new SqlParameter("@Nombre", SqlDbType.NVarChar, 100) { Value = (object)f.Nombre ?? DBNull.Value },
                            new SqlParameter("@Codigo", SqlDbType.VarChar, 30) { Value = f.Codigo },
                            new SqlParameter("@Bulto", SqlDbType.Int) { Value = f.Bulto },
                            new SqlParameter("@Total", SqlDbType.Int) { Value = f.Total },
                            new SqlParameter("@Situacion", SqlDbType.VarChar, 20) { Value = f.SituacionAlEntregar },
                            new SqlParameter("@Repartidor", SqlDbType.VarChar, 50) { Value = (object)f.Repartidor ?? DBNull.Value },
                            new SqlParameter("@FechaEntrega", SqlDbType.DateTime) { Value = f.FechaEntrega },
                            new SqlParameter("@Pendiente", SqlDbType.VarChar, 10) { Value = ReglasBultosPendientes.ESTADO_PENDIENTE }).ConfigureAwait(false);
                    }
                    catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                    {
                        // Otra petición con el mismo bulto a la vez: ya está apuntado.
                    }
                }
            }
            return insertadas;
        }

        public async Task<List<BultoPendienteReparto>> Leer(FiltroBultosPendientes filtro, DateTime hoy)
        {
            filtro = filtro ?? new FiltroBultosPendientes();
            var condiciones = new List<string>();
            var parametros = new List<SqlParameter> { new SqlParameter("@Pendiente", SqlDbType.VarChar, 10) { Value = ReglasBultosPendientes.ESTADO_PENDIENTE } };
            if (filtro.SoloPendientes)
            {
                condiciones.Add("Estado = @Pendiente");
            }
            else
            {
                condiciones.Add("(Estado = @Pendiente OR FechaCierre >= @Desde)");
                parametros.Add(new SqlParameter("@Desde", SqlDbType.DateTime) { Value = hoy.Date.AddDays(-Math.Max(1, Math.Min(filtro.Dias, 366))) });
            }
            if (!string.IsNullOrWhiteSpace(filtro.Empresa))
            {
                condiciones.Add("Empresa = @Empresa");
                parametros.Add(new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = filtro.Empresa.Trim() });
            }
            if (filtro.Pedido.HasValue)
            {
                condiciones.Add("Pedido = @Pedido");
                parametros.Add(new SqlParameter("@Pedido", SqlDbType.Int) { Value = filtro.Pedido.Value });
            }
            if (filtro.Envio.HasValue)
            {
                condiciones.Add("EnvioId = @Envio");
                parametros.Add(new SqlParameter("@Envio", SqlDbType.Int) { Value = filtro.Envio.Value });
            }
            using (var db = new NVEntities())
            {
                return await db.Database.SqlQuery<BultoPendienteReparto>(
                    $"SELECT TOP 500 {COLUMNAS} FROM {T} WHERE {string.Join(" AND ", condiciones)} ORDER BY FechaEntrega, EnvioId, Bulto",
                    parametros.Cast<object>().ToArray()).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<BultoPendienteReparto> LeerPorId(int id)
        {
            using (var db = new NVEntities())
            {
                return (await db.Database.SqlQuery<BultoPendienteReparto>($"SELECT TOP 1 {COLUMNAS} FROM {T} WHERE Id = @Id",
                    new SqlParameter("@Id", SqlDbType.Int) { Value = id }).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            }
        }

        public async Task<bool> Cerrar(int id, string cierre, string motivo, string usuario, DateTime momento, MovimientoBultoReparto entregado, string anterior, string nuevo)
        {
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int filas = await db.Database.ExecuteSqlCommandAsync(SQL_CERRAR,
                        new SqlParameter("@Cerrado", SqlDbType.VarChar, 10) { Value = ReglasBultosPendientes.ESTADO_CERRADO },
                        new SqlParameter("@Cierre", SqlDbType.VarChar, 20) { Value = cierre },
                        new SqlParameter("@Motivo", SqlDbType.NVarChar, ReglasBultosPendientes.LONGITUD_MOTIVO) { Value = motivo },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = ReglasBultosPendientes.Recortar(usuario, 50) ?? string.Empty },
                        new SqlParameter("@Momento", SqlDbType.DateTime) { Value = momento },
                        new SqlParameter("@Id", SqlDbType.Int) { Value = id },
                        new SqlParameter("@Pendiente", SqlDbType.VarChar, 10) { Value = ReglasBultosPendientes.ESTADO_PENDIENTE }).ConfigureAwait(false);
                    if (filas != 1)
                    {
                        transaccion.Rollback();
                        return false;
                    }
                    if (entregado != null && ColumnasOpcionales.ExisteTabla(db, RepositorioFurgoneta.TABLA))
                    {
                        _ = await db.Database.ExecuteSqlCommandAsync(RepositorioFurgoneta.SQL_INSERTAR, RepositorioFurgoneta.Parametros(entregado)).ConfigureAwait(false);
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                        new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = ReglasBultosPendientes.TABLA_MODIFICACIONES },
                        new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                        new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                        new SqlParameter("@UsuarioRastro", SqlDbType.NVarChar, 50) { Value = ReglasBultosPendientes.Recortar(usuario, 50) ?? string.Empty }).ConfigureAwait(false);
                    transaccion.Commit();
                    return true;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        public async Task<int> ContarPendientes(int envio)
        {
            using (var db = new NVEntities())
            {
                return (await db.Database.SqlQuery<int>($"SELECT COUNT(*) FROM {T} WHERE EnvioId = @Envio AND Estado = @Pendiente",
                    new SqlParameter("@Envio", SqlDbType.Int) { Value = envio },
                    new SqlParameter("@Pendiente", SqlDbType.VarChar, 10) { Value = ReglasBultosPendientes.ESTADO_PENDIENTE }).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            }
        }
    }

    /// <summary>Lo que devuelve el servicio (el controlador lo traduce a HTTP).</summary>
    public class ResultadoBultosPendientes<T>
    {
        public HttpStatusCode Estado { get; set; } = HttpStatusCode.OK;
        public string Mensaje { get; set; }
        public T Contenido { get; set; }

        public static ResultadoBultosPendientes<T> Error(HttpStatusCode estado, string mensaje) => new ResultadoBultosPendientes<T> { Estado = estado, Mensaje = mensaje };
    }

    /// <summary>NestoAPI#880 corte 2: apuntar, listar, cerrar y recordar los bultos pendientes.</summary>
    public class ServicioBultosPendientesReparto
    {
        private readonly IRepositorioBultosPendientes repositorio;
        private readonly IRepositorioFurgoneta furgoneta;
        private readonly Func<DateTime> ahora;

        /// <param name="furgoneta">Para saber dónde está cada bulto ahora (#772). Null: sin «dónde está».</param>
        public ServicioBultosPendientesReparto(IRepositorioBultosPendientes repositorio, IRepositorioFurgoneta furgoneta, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.furgoneta = furgoneta;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>Tras una entrega incompleta (también en el reintento): una fila por bulto que falta. Nunca lanza.</summary>
        public async Task<int> RegistrarDeEntrega(EnviosAgencia envio, EvaluacionBultosEntrega evaluacion, string repartidor)
        {
            try
            {
                List<BultoPendienteReparto> filas = ReglasBultosPendientes.DesdeEvaluacion(envio, evaluacion, repartidor, ahora());
                if (filas.Count == 0 || !repositorio.Disponible())
                {
                    return 0;
                }
                return await repositorio.Registrar(filas).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #880] La entrega incompleta del envío {envio?.Numero} está hecha, pero no se han podido apuntar sus bultos pendientes: {ex.Message}", ex));
                return 0;
            }
        }

        public async Task<ResultadoBultosPendientes<List<BultoPendienteRepartoDTO>>> Listar(FiltroBultosPendientes filtro)
        {
            if (!repositorio.Disponible())
            {
                return ResultadoBultosPendientes<List<BultoPendienteRepartoDTO>>.Error(HttpStatusCode.ServiceUnavailable, ReglasBultosPendientes.MENSAJE_SIN_TABLA);
            }
            List<BultoPendienteReparto> filas = await repositorio.Leer(filtro, ahora()).ConfigureAwait(false) ?? new List<BultoPendienteReparto>();
            Dictionary<string, MovimientoBultoReparto> ultimos = await Ultimos(filas.Where(f => f.Estado == ReglasBultosPendientes.ESTADO_PENDIENTE)).ConfigureAwait(false);
            return new ResultadoBultosPendientes<List<BultoPendienteRepartoDTO>>
            {
                Contenido = filas.Select(f => ReglasBultosPendientes.ADto(f, ultimos.TryGetValue(f.Codigo?.Trim() ?? string.Empty, out MovimientoBultoReparto u) ? u : null)).ToList()
            };
        }

        /// <param name="puedeCerrar">Almacén, Dirección o Informática (lo decide el controlador).</param>
        public async Task<ResultadoBultosPendientes<BultoPendienteRepartoDTO>> Cerrar(int id, CerrarBultoPendienteDTO peticion, string usuario, bool puedeCerrar)
        {
            if (!puedeCerrar)
            {
                return ResultadoBultosPendientes<BultoPendienteRepartoDTO>.Error(HttpStatusCode.Forbidden, ReglasBultosPendientes.MENSAJE_SIN_PERMISO_CERRAR);
            }
            if (!repositorio.Disponible())
            {
                return ResultadoBultosPendientes<BultoPendienteRepartoDTO>.Error(HttpStatusCode.ServiceUnavailable, ReglasBultosPendientes.MENSAJE_SIN_TABLA);
            }
            string motivoPeticion = ReglasBultosPendientes.MotivoNoSePuedeCerrar(null, peticion, out _);
            if (motivoPeticion != null)
            {
                return ResultadoBultosPendientes<BultoPendienteRepartoDTO>.Error(HttpStatusCode.BadRequest, motivoPeticion);
            }
            BultoPendienteReparto fila = await repositorio.LeerPorId(id).ConfigureAwait(false);
            if (fila == null)
            {
                return ResultadoBultosPendientes<BultoPendienteRepartoDTO>.Error(HttpStatusCode.NotFound, $"No existe el bulto pendiente {id}.");
            }
            string motivo = ReglasBultosPendientes.MotivoNoSePuedeCerrar(fila, peticion, out bool conflicto);
            if (motivo != null)
            {
                return ResultadoBultosPendientes<BultoPendienteRepartoDTO>.Error(conflicto ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, motivo);
            }
            string cierre = ReglasBultosPendientes.Canonico(peticion.Cierre);
            string texto = peticion.Motivo.Trim();
            DateTime momento = ahora();
            MovimientoBultoReparto entregado = cierre == ReglasBultosPendientes.CIERRE_ENTREGADO_EN_MANO
                ? ReglasBultosPendientes.MovimientoEntregadoEnMano(fila, texto, usuario, momento)
                : null;
            string anterior = Newtonsoft.Json.JsonConvert.SerializeObject(new { fila.Id, fila.Codigo, fila.EnvioId, fila.Pedido, fila.Estado, fila.SituacionAlEntregar });
            string nuevo = Newtonsoft.Json.JsonConvert.SerializeObject(new { fila.Id, fila.Codigo, Estado = ReglasBultosPendientes.ESTADO_CERRADO, Cierre = cierre, Motivo = texto, Usuario = usuario, MovimientoEntregado = entregado != null });
            bool cerrado = await repositorio.Cerrar(id, cierre, texto, usuario, momento, entregado, anterior, nuevo).ConfigureAwait(false);
            if (!cerrado)
            {
                BultoPendienteReparto ahoraFila = await repositorio.LeerPorId(id).ConfigureAwait(false) ?? fila;
                return ResultadoBultosPendientes<BultoPendienteRepartoDTO>.Error(HttpStatusCode.Conflict,
                    ReglasBultosPendientes.MotivoNoSePuedeCerrar(ahoraFila, peticion, out _) ?? $"El bulto {fila.Codigo} ha cambiado mientras tanto: vuelva a cargar la lista.");
            }
            fila.Estado = ReglasBultosPendientes.ESTADO_CERRADO;
            fila.Cierre = cierre;
            fila.MotivoCierre = texto;
            fila.UsuarioCierre = usuario;
            fila.FechaCierre = momento;
            return new ResultadoBultosPendientes<BultoPendienteRepartoDTO> { Contenido = ReglasBultosPendientes.ADto(fila, null) };
        }

        /// <summary>Decisión 8: ¿quedan bultos pendientes de ese envío? Si no se puede saber, se fía del DetalleEstado (true). Nunca lanza.</summary>
        public async Task<int> PendientesDelEnvio(int envio)
        {
            try
            {
                return repositorio.Disponible() ? await repositorio.ContarPendientes(envio).ConfigureAwait(false) : 1;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #880] No se han podido contar los bultos pendientes del envío {envio}: {ex.Message}", ex));
                return 1;
            }
        }

        private async Task<Dictionary<string, MovimientoBultoReparto>> Ultimos(IEnumerable<BultoPendienteReparto> filas)
        {
            var resultado = new Dictionary<string, MovimientoBultoReparto>(StringComparer.OrdinalIgnoreCase);
            List<string> codigos = filas.Select(f => f.Codigo?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (furgoneta == null || codigos.Count == 0)
            {
                return resultado;
            }
            try
            {
                if (!furgoneta.ScriptAplicado())
                {
                    return resultado;
                }
                for (int i = 0; i < codigos.Count; i += 200)
                {
                    List<MovimientoBultoReparto> historia = await furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Codigos = codigos.Skip(i).Take(200).ToList() }).ConfigureAwait(false)
                        ?? new List<MovimientoBultoReparto>();
                    foreach (MovimientoBultoReparto u in NucleoFurgoneta.Ultimos(historia))
                    {
                        resultado[u.Codigo?.Trim() ?? string.Empty] = u;
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #880] No se ha podido mirar dónde están los bultos pendientes: {ex.Message}", ex));
            }
            return resultado;
        }

        /// <summary>El recordatorio diario: la campana a Almacén con lo que sigue pendiente. Devuelve a cuántos se ha avisado. Nunca lanza.</summary>
        public async Task<int> Recordar(Func<IServicioNotificacionesPush> notificaciones, Func<string, List<string>> miembros)
        {
            try
            {
                ResultadoBultosPendientes<List<BultoPendienteRepartoDTO>> lista = await Listar(new FiltroBultosPendientes()).ConfigureAwait(false);
                NotificacionPushDTO notificacion = lista.Estado == HttpStatusCode.OK ? ReglasBultosPendientes.Recordatorio(lista.Contenido) : null;
                if (notificacion == null)
                {
                    return 0;
                }
                IServicioNotificacionesPush servicio = notificaciones();
                int avisados = 0;
                foreach (string usuario in new HashSet<string>((miembros(Constantes.GruposSeguridad.ALMACEN) ?? new List<string>()).Select(AvisosFurgonetaSinDescargar.ConDominio), StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                        avisados++;
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Reparto #880] No se pudo recordar a {usuario} los bultos pendientes: {ex.Message}", ex));
                    }
                }
                return avisados;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #880] El recordatorio de los bultos pendientes ha fallado: {ex.Message}", ex));
                return 0;
            }
        }
    }

    /// <summary>NestoAPI#880 corte 2: el recordatorio diario (L-V a las 9:00) de los bultos pendientes. Sin la tabla, nada.</summary>
    public static class BultosPendientesRecordatorioJobsService
    {
        public const string JOB_ID = "reparto-bultos-pendientes";
        public const string CRON = "0 9 * * 1-5"; // de lunes a viernes a las 9:00
        internal const string USUARIO_SISTEMA = "Sistema (bultos pendientes)";
        internal const string MENSAJE_FESTIVO = "[Reparto #880] No se recuerdan los bultos pendientes: el día es festivo";

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Recordar()
        {
            if (JobsFestivos.SaltarSiFestivo(MENSAJE_FESTIVO, USUARIO_SISTEMA, JobsFestivos.EsFestivoAlgete, () => DateTime.Now))
            {
                return;
            }
            using (var db = new NVEntities())
            {
                _ = await new ServicioBultosPendientesReparto(new RepositorioBultosPendientesSql(), new RepositorioFurgoneta(db))
                    .Recordar(() => new ServicioNotificacionesPush(), MiembrosGrupoDominio.Leer).ConfigureAwait(false);
            }
        }
    }
}
