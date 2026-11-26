using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Punto 5 (Carlos 19/11/26): la parada de una NOTA DE RECOGIDA. LAS REGLAS, puras:
    /// <list type="bullet">
    /// <item>«No se ha recogido» (motivo obligatorio): la parada sale del reparto con «No recogido (reparto propio): motivo» y no se crean
    /// bultos NR. Si ya hay bultos NR en una furgoneta o está abonada, no. Carlos 20/11/26: NO se cierra; queda Incidentada con el día
    /// <see cref="FechaRepartoPropio.FECHA_PENDIENTE_DECIDIR"/> (no sale en ningún día) y una fila Pendiente en
    /// <see cref="TABLA_NO_RECOGIDAS"/> hasta que Almacén decida: Reprogramar (la misma nota y parada vuelven al reparto,
    /// <see cref="FechaReprogramada"/>, sin repartidor) o Anular (Cerrado a mano). Sin la tabla, Cerrado a mano (5) como antes.</item>
    /// <item>El comentario al recogerla («recogido, pero sin el manípulo»): historia, DetalleEstado «Recogido (reparto propio): …» y prueba.</item>
    /// <item>«Anular recogida» de una nota YA en reparto: misma salida (5, «Recogida anulada: motivo»), con rastro en Modificaciones; nunca
    /// si sus bultos NR ya están en una furgoneta o la recogida está abonada.</item>
    /// </list>
    /// </summary>
    public static class ReglasParadaRecogida
    {
        public const int MAXIMO_TEXTO = 500;
        /// <summary>DetalleEstado es varchar(100).</summary>
        public const int LONGITUD_DETALLE = 100;
        public const string PREFIJO_RECOGIDO = "Recogido (reparto propio): ";
        public const string PREFIJO_NO_RECOGIDO = "No recogido (reparto propio): ";
        public const string PREFIJO_ANULADA = "Recogida anulada: ";
        /// <summary>El Estado de la respuesta de NoRecogida (EstadoEnvioRepartoDTO.Estado).</summary>
        public const string ESTADO_NO_RECOGIDO = "NoRecogido";
        public const string ESTADO_ANULADA = "Anulada";
        public const string TABLA_MODIFICACIONES_ANULADA = "RecogidaAnulada";
        public const string CODIGO_YA_RECOGIDA = "RECOGIDA_YA_RECOGIDA";

        /// <returns>El error de usted, o null si vale.</returns>
        public static string ValidarTexto(string texto, bool obligatorio, string que)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return obligatorio ? $"{que} es obligatorio." : null;
            }
            return texto.Trim().Length > MAXIMO_TEXTO ? $"{que} no puede pasar de {MAXIMO_TEXTO} caracteres." : null;
        }

        public static string Detalle(string prefijo, string texto)
        {
            string detalle = prefijo + (texto ?? string.Empty).Trim();
            return detalle.Length <= LONGITUD_DETALLE ? detalle : detalle.Substring(0, LONGITUD_DETALLE);
        }

        /// <summary>Lo que va en EnviosAgenciaPruebasEntrega.Texto: las observaciones y, debajo, el comentario de la recogida.</summary>
        public static string TextoPrueba(string observaciones, string comentario)
        {
            string obs = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
            string com = string.IsNullOrWhiteSpace(comentario) ? null : "Comentario: " + comentario.Trim();
            return obs == null ? com : com == null ? obs : obs + "\n" + com;
        }

        /// <summary>
        /// Lo que impide quitar la parada (no recogerla o anularla): sus bultos NR ya están en una furgoneta o la recogida ya está abonada.
        /// Null: se puede.
        /// </summary>
        public static string MotivoYaRecogida(string documento, SeguimientoNotaRecogida seguimiento)
        {
            if (seguimiento == null)
            {
                return null;
            }
            if (seguimiento.PedidoAbono.HasValue)
            {
                return $"La recogida {documento} ya está abonada (abono {seguimiento.PedidoAbono.Value}): no se puede quitar del reparto.";
            }
            return seguimiento.BultosRecogidos
                ? $"Ya hay bultos de la recogida {documento} recogidos en una furgoneta: entréguela como recogida o descárguelos antes."
                : null;
        }

        // ------------------------------------------------ Caso 930553 (Carlos 26/11/26): la recogida marcada «Recogido» por error

        public const string PREFIJO_ANULADA_MARCADA = "Recogida anulada (marcada por error): ";
        public const string CODIGO_ABONADA = "RECOGIDA_ABONADA";
        internal const string TIPO_CLAVE_DESCARGA_ANULAR = "AnularRecogidaMarcada";

        /// <summary>
        /// LA REGLA de «Anular» una recogida cuya parada ya está ENTREGADA (el repartidor la marcó «Recogido» por error): solo Almacén o
        /// Dirección y solo si no hay abono de esa recogida (NotasEntregaCabecera.PedidoAbono, lo mismo que mira ServicioAbonosRecogida).
        /// Los bultos NR en una furgoneta NO lo impiden: se dan por descargados. Null: se puede.
        /// </summary>
        public static string MotivoNoAnulableMarcada(string documento, bool puedeAnularMarcada, SeguimientoNotaRecogida seguimiento)
        {
            if (!puedeAnularMarcada)
            {
                return $"La recogida {documento} ya figura como recogida: solo Almacén o Dirección pueden anularla.";
            }
            return seguimiento?.PedidoAbono.HasValue == true
                ? $"La recogida {documento} ya está abonada (abono {seguimiento.PedidoAbono.Value}): no se puede anular."
                : null;
        }

        // ------------------------------------------------ Carlos 20/11/26: «No se ha recogido» pasa a Almacén, que decide

        /// <summary>La tabla de las recogidas no hechas pendientes de decidir (Scripts/RepartoRecogidasNoRecogidas.sql).</summary>
        public const string TABLA_NO_RECOGIDAS = "RepartoRecogidasNoRecogidas";
        public const string DECISION_PENDIENTE = "Pendiente";
        public const string DECISION_REPROGRAMADA = "Reprogramada";
        public const string DECISION_ANULADA = "Anulada";
        public const string TIPO_NOTIFICACION_NO_RECOGIDA = "RecogidaNoRecogida";
        public const string TABLA_MODIFICACIONES_REPROGRAMADA = "RecogidaReprogramada";
        public const string MENSAJE_FALTA_SCRIPT_NO_RECOGIDAS = "Falta lanzar Scripts/RepartoRecogidasNoRecogidas.sql (las recogidas no hechas que decide Almacén). Avise a Informática.";
        internal const string TIPO_CLAVE_DESCARGA_REPROGRAMAR = "ReprogramarRecogida";

        /// <summary>
        /// LA REGLA del día al que vuelve sin fecha: el siguiente día de reparto propio del cliente a partir de MAÑANA (su día de ruta interna
        /// —DiasRepartoCodigosPostales.DiaRutaInterna— si lo tiene; si no, el siguiente día que no sea festivo del reparto propio).
        /// </summary>
        public static DateTime FechaReprogramada(DateTime hoy, DayOfWeek? diaRuta, Func<DateTime, bool> esDiaSinReparto)
            => FechaRepartoPropio.Calcular(hoy.Date.AddDays(1), hoy.Date, diaRuta, esDiaSinReparto);

        /// <returns>El error de usted, o null si vale.</returns>
        public static string ValidarFechaReprogramar(DateTime? fecha, DateTime hoy)
            => fecha.HasValue && fecha.Value.Date < hoy.Date ? "La fecha no puede ser anterior a hoy." : null;

        public static string MensajeReprogramada(string documento, DateTime fecha)
            => $"La recogida {documento} vuelve al reparto del {fecha.ToString("dddd dd/MM/yyyy", new System.Globalization.CultureInfo("es-ES"))}, " +
               "sin repartidor: hay que leer el papel al cargarla.";

        /// <summary>La campana a Almacén: de usted, con el cliente, el documento NR y el motivo.</summary>
        public static NotificacionPushDTO NotificacionNoRecogida(EnviosAgencia envio, string documento, string motivo, string repartidor)
        {
            string cliente = envio?.Cliente?.Trim() + (string.IsNullOrWhiteSpace(envio?.Contacto) ? string.Empty : "/" + envio.Contacto.Trim())
                + (string.IsNullOrWhiteSpace(envio?.Nombre) ? string.Empty : " - " + envio.Nombre.Trim());
            string quien = ServicioRepartoPropio.NombreRepartidor(repartidor) ?? "El repartidor";
            var datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION_NO_RECOGIDA,
                ["documento"] = documento,
                ["cliente"] = envio?.Cliente?.Trim(),
                ["envio"] = envio?.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            if (envio?.Pedido != null)
            {
                datos["pedido"] = envio.Pedido.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return new NotificacionPushDTO
            {
                Titulo = $"Recogida no hecha: {documento}",
                Cuerpo = $"{quien} no ha podido hacer la recogida {documento} de {cliente}. Motivo: {motivo?.Trim()}. " +
                    "Decida si vuelve al siguiente reparto o se anula del todo.",
                Tipo = TIPO_NOTIFICACION_NO_RECOGIDA,
                Datos = datos
            };
        }
    }

    /// <summary>Carlos 20/11/26: una fila de RepartoRecogidasNoRecogidas con lo que hace falta para decidir.</summary>
    public class RecogidaNoRecogidaPendiente
    {
        public int Id { get; set; }
        public int Pedido { get; set; }
        public int EnvioId { get; set; }
        public string Motivo { get; set; }
        public string Repartidor { get; set; }
        public DateTime FechaHora { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        public DateTime? FechaEntregaAnterior { get; set; }
        public string Estado { get; set; }
        // Para el día al que vuelve (del envío y de la cabecera del pedido)
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string CodPostal { get; set; }
        public string Ruta { get; set; }
    }

    /// <summary>Punto 5: lo que la parada de una recogida necesita fuera del reparto (seguimiento, cerrar la parada, avisos de la solicitud).</summary>
    public interface IRecogidasParadaReparto
    {
        /// <summary>Abono y bultos NR de la nota (empresa 1). Null si no se sabe.</summary>
        Task<SeguimientoNotaRecogida> LeerSeguimiento(int pedido);
        /// <summary>
        /// En UNA transacción: el envío a Cerrado a mano con <paramref name="detalle"/> (solo si sigue Tramitado o Incidentado), su historia y
        /// una fila en Modificaciones. False si el envío ya no estaba en reparto (nada cambia).
        /// </summary>
        Task<bool> CerrarParada(int envio, string detalle, IList<EnvioHistoria> historia, string tabla, string anterior, string nuevo, string usuario);
        /// <summary>Aviso a quien pidió la recogida (si viene de una solicitud). Nunca lanza.</summary>
        Task AvisarSolicitud(int pedido, bool recogidaHecha, string texto, string usuario);
        /// <summary>La solicitud de esa nota (si la hay) queda Anulada y se avisa. Nunca lanza.</summary>
        Task AnularSolicitud(int pedido, string motivo, string usuario);

        // Carlos 20/11/26: «No se ha recogido» pasa a Almacén, que decide.
        /// <summary>¿Existe RepartoRecogidasNoRecogidas? Sin ella, NoRecogida como antes y los endpoints nuevos, 503.</summary>
        bool PendientesDisponibles();
        /// <summary>La recogida no hecha queda pendiente de decidir (idempotente por la clave y por el envío ya pendiente).</summary>
        Task ApuntarNoRecogida(RecogidaNoRecogidaPendiente fila);
        /// <summary>Las pendientes de decidir cuyo envío sigue esperando (Incidentado de la 14).</summary>
        Task<List<RecogidaNoRecogidaDTO>> LeerNoRecogidas();
        /// <summary>La pendiente de esa nota, o null.</summary>
        Task<RecogidaNoRecogidaPendiente> LeerPendiente(int pedido);
        /// <summary>
        /// En UNA transacción: el envío vuelve a Tramitado con su nuevo día de reparto (solo si sigue Incidentado), la fila pasa a
        /// Reprogramada, su historia, los Descargado de sus cargas (sin repartidor) y el rastro. False si algo había cambiado (nada cambia).
        /// </summary>
        Task<bool> Reprogramar(RecogidaNoRecogidaPendiente pendiente, DateTime fecha, IList<EnvioHistoria> historia,
            IReadOnlyList<MovimientoBultoReparto> descargas, string anterior, string nuevo, string usuario);
        /// <summary>La pendiente de esa nota (si la hay) queda Anulada. Nunca lanza.</summary>
        Task CerrarPendiente(int pedido, string usuario);
        /// <summary>La campana a Almacén. Nunca lanza.</summary>
        Task AvisarAlmacen(NotificacionPushDTO notificacion);

        // Caso 930553 (Carlos 26/11/26): la recogida marcada «Recogido» por error.
        /// <summary>
        /// En UNA transacción: el envío ENTREGADO pasa a Cerrado a mano con <paramref name="detalle"/> (solo si sigue Entregado y la nota
        /// sigue sin abono), su historia, los Descargado de <paramref name="descargas"/>, la solicitud Confirmada de la nota (si la hay) a
        /// Anulada con el motivo y el rastro (RecogidaAnulada). Null si algo había cambiado (nada cambia).
        /// </summary>
        Task<AnulacionRecogidaMarcada> AnularRecogidaMarcada(int envio, int pedido, string detalle, string motivo, IList<EnvioHistoria> historia,
            IReadOnlyList<MovimientoBultoReparto> descargas, string anterior, string nuevo, string usuario);
        /// <summary>El aviso a quien pidió la recogida de la solicitud ya anulada. Nunca lanza.</summary>
        Task AvisarSolicitudAnulada(int idSolicitud, string usuario);
    }

    /// <summary>Caso 930553: lo que ha hecho la anulación de una recogida marcada por error.</summary>
    public class AnulacionRecogidaMarcada
    {
        /// <summary>Las solicitudes (#855) que han quedado Anuladas (normalmente una o ninguna).</summary>
        public List<int> SolicitudesAnuladas { get; set; } = new List<int>();
        public int BultosDescargados { get; set; }
    }

    /// <summary>Punto 5: con su propio contexto (como el resto de piezas que no viven en el del reparto).</summary>
    public class RecogidasParadaRepartoSql : IRecogidasParadaReparto
    {
        private const string SQL_CERRAR = @"
UPDATE dbo.EnviosAgencia SET Estado = @Estado, DetalleEstado = @Detalle, Usuario = @Usuario
WHERE Numero = @Numero AND Agencia = @Agencia AND Estado IN (@Tramitado, @Incidentado);";
        private const string SQL_HISTORIA = @"
INSERT INTO dbo.EnviosHistoria (NumeroEnvio, Campo, ValorAnterior, Observaciones, Usuario, FechaModificacion)
VALUES (@NumeroEnvio, @Campo, @ValorAnterior, @Observaciones, @UsuarioHistoria, @Fecha);";
        private const string SQL_RASTRO = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @UsuarioRastro);";

        private static ServicioRecogidasSolicitadas Solicitudes(NVEntities db)
            => new ServicioRecogidasSolicitadas(new RepositorioRecogidasSolicitadas(db), new AvisosRecogidasSolicitadas());

        public async Task<SeguimientoNotaRecogida> LeerSeguimiento(int pedido)
        {
            using (var db = new NVEntities())
            {
                return await new RepositorioRecogidasSolicitadas(db).LeerSeguimiento(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido).ConfigureAwait(false);
            }
        }

        public async Task<bool> CerrarParada(int envio, string detalle, IList<EnvioHistoria> historia, string tabla, string anterior, string nuevo, string usuario)
        {
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int filas = await db.Database.ExecuteSqlCommandAsync(SQL_CERRAR,
                        new SqlParameter("@Estado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_CERRADO_A_MANO },
                        new SqlParameter("@Detalle", SqlDbType.VarChar, 100) { Value = detalle },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                        new SqlParameter("@Numero", SqlDbType.Int) { Value = envio },
                        new SqlParameter("@Agencia", SqlDbType.Int) { Value = Constantes.Agencias.AGENCIA_REPARTO_PROPIO },
                        new SqlParameter("@Tramitado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_TRAMITADO },
                        new SqlParameter("@Incidentado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_INCIDENTADO }).ConfigureAwait(false);
                    if (filas != 1)
                    {
                        transaccion.Rollback();
                        return false;
                    }
                    foreach (EnvioHistoria fila in historia ?? new List<EnvioHistoria>())
                    {
                        NVEntities.AcotarHistoriaEnvio(fila);
                        _ = await db.Database.ExecuteSqlCommandAsync(SQL_HISTORIA,
                            new SqlParameter("@NumeroEnvio", SqlDbType.Int) { Value = fila.NumeroEnvio },
                            new SqlParameter("@Campo", SqlDbType.NVarChar, 50) { Value = fila.Campo ?? string.Empty },
                            new SqlParameter("@ValorAnterior", SqlDbType.NVarChar, 50) { Value = fila.ValorAnterior ?? string.Empty },
                            new SqlParameter("@Observaciones", SqlDbType.NVarChar, -1) { Value = (object)fila.Observaciones ?? DBNull.Value },
                            new SqlParameter("@UsuarioHistoria", SqlDbType.VarChar, 50) { Value = Recortar(fila.Usuario, 50) },
                            new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = fila.FechaModificacion }).ConfigureAwait(false);
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                        new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = tabla },
                        new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                        new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                        new SqlParameter("@UsuarioRastro", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) }).ConfigureAwait(false);
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

        public async Task AvisarSolicitud(int pedido, bool recogidaHecha, string texto, string usuario)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    await Solicitudes(db).AlPasarPorLaParada(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido, recogidaHecha, texto, usuario).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido avisar de la parada de la recogida {pedido}: {ex.Message}", ex));
            }
        }

        public async Task AnularSolicitud(int pedido, string motivo, string usuario)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    await Solicitudes(db).AlAnularRecogidaEnReparto(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido, motivo, usuario).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] La recogida {pedido} está anulada, pero no se ha podido anular su solicitud: {ex.Message}", ex));
            }
        }

        private static string Recortar(string valor, int maximo) => valor == null ? string.Empty : valor.Length <= maximo ? valor : valor.Substring(0, maximo);

        // ------------------------------------------------ Carlos 20/11/26: las recogidas no hechas que decide Almacén

        private const string T = "dbo." + ReglasParadaRecogida.TABLA_NO_RECOGIDAS;

        public bool PendientesDisponibles()
        {
            using (var db = new NVEntities())
            {
                return ColumnasOpcionales.ExisteTabla(db, ReglasParadaRecogida.TABLA_NO_RECOGIDAS);
            }
        }

        public async Task ApuntarNoRecogida(RecogidaNoRecogidaPendiente f)
        {
            using (var db = new NVEntities())
            {
                _ = await db.Database.ExecuteSqlCommandAsync($@"
IF NOT EXISTS (SELECT 1 FROM {T} WHERE ClaveIdempotencia = @Clave)
   AND NOT EXISTS (SELECT 1 FROM {T} WHERE EnvioId = @EnvioId AND Estado = @Pendiente)
    INSERT INTO {T} (Pedido, EnvioId, Motivo, Repartidor, FechaHora, FechaEntregaAnterior, ClaveIdempotencia, Estado, FechaRegistro)
    VALUES (@Pedido, @EnvioId, @Motivo, @Repartidor, @FechaHora, @FechaAnterior, @Clave, @Pendiente, GETDATE());",
                    new SqlParameter("@Clave", SqlDbType.UniqueIdentifier) { Value = f.ClaveIdempotencia },
                    new SqlParameter("@EnvioId", SqlDbType.Int) { Value = f.EnvioId },
                    new SqlParameter("@Pendiente", SqlDbType.VarChar, 15) { Value = ReglasParadaRecogida.DECISION_PENDIENTE },
                    new SqlParameter("@Pedido", SqlDbType.Int) { Value = f.Pedido },
                    new SqlParameter("@Motivo", SqlDbType.NVarChar, ReglasParadaRecogida.MAXIMO_TEXTO) { Value = Recortar(f.Motivo, ReglasParadaRecogida.MAXIMO_TEXTO) },
                    new SqlParameter("@Repartidor", SqlDbType.VarChar, 50) { Value = Recortar(f.Repartidor, 50) },
                    new SqlParameter("@FechaHora", SqlDbType.DateTime) { Value = f.FechaHora },
                    new SqlParameter("@FechaAnterior", SqlDbType.Date) { Value = (object)f.FechaEntregaAnterior?.Date ?? DBNull.Value }).ConfigureAwait(false);
            }
        }

        public async Task<List<RecogidaNoRecogidaDTO>> LeerNoRecogidas()
        {
            using (var db = new NVEntities())
            {
                List<RecogidaNoRecogidaDTO> filas = await db.Database.SqlQuery<RecogidaNoRecogidaDTO>($@"
SELECT r.Pedido, CAST(NULL AS varchar(20)) AS CodigoDocumento, r.EnvioId, RTRIM(e.Cliente) AS Cliente, RTRIM(e.Contacto) AS Contacto,
    RTRIM(e.Nombre) AS Nombre, RTRIM(e.Direccion) AS Direccion, RTRIM(e.Poblacion) AS Poblacion, r.Motivo, RTRIM(r.Repartidor) AS Repartidor, r.FechaHora
FROM {T} r
JOIN dbo.EnviosAgencia e ON e.Numero = r.EnvioId
WHERE r.Estado = @Pendiente AND e.Agencia = @Agencia AND e.Estado = @Incidentado
ORDER BY r.FechaHora, r.Id",
                    new SqlParameter("@Pendiente", SqlDbType.VarChar, 15) { Value = ReglasParadaRecogida.DECISION_PENDIENTE },
                    new SqlParameter("@Agencia", SqlDbType.Int) { Value = Constantes.Agencias.AGENCIA_REPARTO_PROPIO },
                    new SqlParameter("@Incidentado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_INCIDENTADO }).ToListAsync().ConfigureAwait(false);
                foreach (RecogidaNoRecogidaDTO fila in filas)
                {
                    fila.CodigoDocumento = CodigoNotaEntrega.Documento(true, fila.Pedido);
                    fila.Repartidor = ServicioRepartoPropio.NombreRepartidor(fila.Repartidor);
                }
                return filas;
            }
        }

        public async Task<RecogidaNoRecogidaPendiente> LeerPendiente(int pedido)
        {
            using (var db = new NVEntities())
            {
                return (await db.Database.SqlQuery<RecogidaNoRecogidaPendiente>($@"
SELECT TOP 1 r.Id, r.Pedido, r.EnvioId, r.Motivo, RTRIM(r.Repartidor) AS Repartidor, r.FechaHora, r.ClaveIdempotencia,
    CAST(r.FechaEntregaAnterior AS datetime) AS FechaEntregaAnterior, RTRIM(r.Estado) AS Estado,
    RTRIM(e.Empresa) AS Empresa, RTRIM(e.Cliente) AS Cliente, RTRIM(e.Contacto) AS Contacto, RTRIM(e.CodPostal) AS CodPostal, RTRIM(c.Ruta) AS Ruta
FROM {T} r
JOIN dbo.EnviosAgencia e ON e.Numero = r.EnvioId
LEFT JOIN dbo.CabPedidoVta c ON c.Empresa = @Empresa AND c.Número = r.Pedido
WHERE r.Pedido = @Pedido AND r.Estado = @Pendiente AND e.Agencia = @Agencia AND e.Estado = @Incidentado
ORDER BY r.Id DESC",
                    new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_POR_DEFECTO },
                    new SqlParameter("@Pedido", SqlDbType.Int) { Value = pedido },
                    new SqlParameter("@Pendiente", SqlDbType.VarChar, 15) { Value = ReglasParadaRecogida.DECISION_PENDIENTE },
                    new SqlParameter("@Agencia", SqlDbType.Int) { Value = Constantes.Agencias.AGENCIA_REPARTO_PROPIO },
                    new SqlParameter("@Incidentado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_INCIDENTADO }).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            }
        }

        public async Task<bool> Reprogramar(RecogidaNoRecogidaPendiente pendiente, DateTime fecha, IList<EnvioHistoria> historia,
            IReadOnlyList<MovimientoBultoReparto> descargas, string anterior, string nuevo, string usuario)
        {
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int filas = await db.Database.ExecuteSqlCommandAsync(@"
UPDATE dbo.EnviosAgencia SET Estado = @Tramitado, FechaEntrega = @Fecha, DetalleEstado = NULL, Usuario = @Usuario
WHERE Numero = @Numero AND Agencia = @Agencia AND Estado = @Incidentado;",
                        new SqlParameter("@Tramitado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_TRAMITADO },
                        new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = fecha.Date },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                        new SqlParameter("@Numero", SqlDbType.Int) { Value = pendiente.EnvioId },
                        new SqlParameter("@Agencia", SqlDbType.Int) { Value = Constantes.Agencias.AGENCIA_REPARTO_PROPIO },
                        new SqlParameter("@Incidentado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_INCIDENTADO }).ConfigureAwait(false);
                    filas += await db.Database.ExecuteSqlCommandAsync($@"
UPDATE {T} SET Estado = @Reprogramada, FechaDecision = GETDATE(), UsuarioDecision = @Usuario, FechaReprogramada = @Fecha
WHERE Id = @Id AND Estado = @Pendiente;",
                        new SqlParameter("@Reprogramada", SqlDbType.VarChar, 15) { Value = ReglasParadaRecogida.DECISION_REPROGRAMADA },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                        new SqlParameter("@Fecha", SqlDbType.Date) { Value = fecha.Date },
                        new SqlParameter("@Id", SqlDbType.Int) { Value = pendiente.Id },
                        new SqlParameter("@Pendiente", SqlDbType.VarChar, 15) { Value = ReglasParadaRecogida.DECISION_PENDIENTE }).ConfigureAwait(false);
                    if (filas != 2)
                    {
                        transaccion.Rollback();
                        return false;
                    }
                    foreach (EnvioHistoria fila in historia ?? new List<EnvioHistoria>())
                    {
                        NVEntities.AcotarHistoriaEnvio(fila);
                        _ = await db.Database.ExecuteSqlCommandAsync(SQL_HISTORIA,
                            new SqlParameter("@NumeroEnvio", SqlDbType.Int) { Value = fila.NumeroEnvio },
                            new SqlParameter("@Campo", SqlDbType.NVarChar, 50) { Value = fila.Campo ?? string.Empty },
                            new SqlParameter("@ValorAnterior", SqlDbType.NVarChar, 50) { Value = fila.ValorAnterior ?? string.Empty },
                            new SqlParameter("@Observaciones", SqlDbType.NVarChar, -1) { Value = (object)fila.Observaciones ?? DBNull.Value },
                            new SqlParameter("@UsuarioHistoria", SqlDbType.VarChar, 50) { Value = Recortar(fila.Usuario, 50) },
                            new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = fila.FechaModificacion }).ConfigureAwait(false);
                    }
                    if (descargas != null && descargas.Count > 0 && ColumnasOpcionales.ExisteTabla(db, RepositorioFurgoneta.TABLA))
                    {
                        foreach (MovimientoBultoReparto m in descargas)
                        {
                            _ = await db.Database.ExecuteSqlCommandAsync(RepositorioFurgoneta.SQL_INSERTAR, RepositorioFurgoneta.Parametros(m)).ConfigureAwait(false);
                        }
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                        new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = ReglasParadaRecogida.TABLA_MODIFICACIONES_REPROGRAMADA },
                        new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                        new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                        new SqlParameter("@UsuarioRastro", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) }).ConfigureAwait(false);
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

        public async Task CerrarPendiente(int pedido, string usuario)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    if (!ColumnasOpcionales.ExisteTabla(db, ReglasParadaRecogida.TABLA_NO_RECOGIDAS))
                    {
                        return;
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync($@"
UPDATE {T} SET Estado = @Anulada, FechaDecision = GETDATE(), UsuarioDecision = @Usuario WHERE Pedido = @Pedido AND Estado = @Pendiente;",
                        new SqlParameter("@Anulada", SqlDbType.VarChar, 15) { Value = ReglasParadaRecogida.DECISION_ANULADA },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                        new SqlParameter("@Pedido", SqlDbType.Int) { Value = pedido },
                        new SqlParameter("@Pendiente", SqlDbType.VarChar, 15) { Value = ReglasParadaRecogida.DECISION_PENDIENTE }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] La recogida {pedido} está anulada, pero no se ha podido cerrar su «No se ha recogido» pendiente: {ex.Message}", ex));
            }
        }

        public async Task AvisarAlmacen(NotificacionPushDTO notificacion)
        {
            try
            {
                List<string> deAlmacen = Verifactu.MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN) ?? new List<string>();
                var servicio = new Notificaciones.ServicioNotificacionesPush();
                foreach (string usuario in new HashSet<string>(deAlmacen.Select(AvisosFurgonetaSinDescargar.ConDominio), StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Reparto] No se pudo avisar a {usuario} de la recogida no hecha: {ex.Message}", ex));
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se pudo avisar a Almacén de la recogida no hecha: {ex.Message}", ex));
            }
        }

        // ------------------------------------------------ Caso 930553 (Carlos 26/11/26): la recogida marcada «Recogido» por error

        private const string SQL_CERRAR_ENTREGADA = @"
UPDATE dbo.EnviosAgencia SET Estado = @Estado, DetalleEstado = @Detalle, Usuario = @Usuario
WHERE Numero = @Numero AND Agencia = @Agencia AND Estado = @Entregado{0};";
        private const string SQL_SIN_ABONO = @"
  AND NOT EXISTS (SELECT 1 FROM dbo." + RepositorioNotasEntregaCabecera.TABLA + @" n WITH (UPDLOCK, HOLDLOCK)
                  WHERE n.Empresa = @Empresa AND n.Pedido = @Pedido AND n.PedidoAbono IS NOT NULL)";
        private const string SQL_SOLICITUDES_DE_LA_NOTA = "SELECT Id FROM dbo." + RepositorioRecogidasSolicitadas.TABLA + " WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE Empresa = @p0 AND PedidoNota = @p1 AND Estado = '" + EstadosRecogidaSolicitada.CONFIRMADA + "'";

        public async Task<AnulacionRecogidaMarcada> AnularRecogidaMarcada(int envio, int pedido, string detalle, string motivo, IList<EnvioHistoria> historia,
            IReadOnlyList<MovimientoBultoReparto> descargas, string anterior, string nuevo, string usuario)
        {
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    string sinAbono = ColumnasOpcionales.ExisteTabla(db, RepositorioNotasEntregaCabecera.TABLA) ? SQL_SIN_ABONO : string.Empty;
                    int filas = await db.Database.ExecuteSqlCommandAsync(string.Format(SQL_CERRAR_ENTREGADA, sinAbono),
                        new SqlParameter("@Estado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_CERRADO_A_MANO },
                        new SqlParameter("@Detalle", SqlDbType.VarChar, 100) { Value = detalle },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                        new SqlParameter("@Numero", SqlDbType.Int) { Value = envio },
                        new SqlParameter("@Agencia", SqlDbType.Int) { Value = Constantes.Agencias.AGENCIA_REPARTO_PROPIO },
                        new SqlParameter("@Entregado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_ENTREGADO },
                        new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_POR_DEFECTO },
                        new SqlParameter("@Pedido", SqlDbType.Int) { Value = pedido }).ConfigureAwait(false);
                    if (filas != 1)
                    {
                        transaccion.Rollback();
                        return null;
                    }
                    foreach (EnvioHistoria fila in historia ?? new List<EnvioHistoria>())
                    {
                        NVEntities.AcotarHistoriaEnvio(fila);
                        _ = await db.Database.ExecuteSqlCommandAsync(SQL_HISTORIA,
                            new SqlParameter("@NumeroEnvio", SqlDbType.Int) { Value = fila.NumeroEnvio },
                            new SqlParameter("@Campo", SqlDbType.NVarChar, 50) { Value = fila.Campo ?? string.Empty },
                            new SqlParameter("@ValorAnterior", SqlDbType.NVarChar, 50) { Value = fila.ValorAnterior ?? string.Empty },
                            new SqlParameter("@Observaciones", SqlDbType.NVarChar, -1) { Value = (object)fila.Observaciones ?? DBNull.Value },
                            new SqlParameter("@UsuarioHistoria", SqlDbType.VarChar, 50) { Value = Recortar(fila.Usuario, 50) },
                            new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = fila.FechaModificacion }).ConfigureAwait(false);
                    }
                    var resultado = new AnulacionRecogidaMarcada();
                    if (descargas != null && descargas.Count > 0 && ColumnasOpcionales.ExisteTabla(db, RepositorioFurgoneta.TABLA))
                    {
                        foreach (MovimientoBultoReparto m in descargas)
                        {
                            resultado.BultosDescargados += await db.Database.ExecuteSqlCommandAsync(RepositorioFurgoneta.SQL_INSERTAR, RepositorioFurgoneta.Parametros(m)).ConfigureAwait(false) > 0 ? 1 : 0;
                        }
                    }
                    if (ColumnasOpcionales.ExisteTabla(db, RepositorioRecogidasSolicitadas.TABLA))
                    {
                        List<int> solicitudes = await db.Database.SqlQuery<int>(SQL_SOLICITUDES_DE_LA_NOTA, Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido)
                            .ToListAsync().ConfigureAwait(false);
                        foreach (int id in solicitudes)
                        {
                            if (await db.Database.ExecuteSqlCommandAsync(RepositorioRecogidasSolicitadas.SQL_ANULAR,
                                new SqlParameter("@Motivo", SqlDbType.NVarChar, -1) { Value = (object)motivo ?? DBNull.Value },
                                new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                                new SqlParameter("@Id", SqlDbType.Int) { Value = id },
                                new SqlParameter("@Esperado", SqlDbType.VarChar, 20) { Value = EstadosRecogidaSolicitada.CONFIRMADA }).ConfigureAwait(false) == 1)
                            {
                                resultado.SolicitudesAnuladas.Add(id);
                            }
                        }
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                        new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = ReglasParadaRecogida.TABLA_MODIFICACIONES_ANULADA },
                        new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                        new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                        new SqlParameter("@UsuarioRastro", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) }).ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        public async Task AvisarSolicitudAnulada(int idSolicitud, string usuario)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    await Solicitudes(db).AvisarAnuladaEnReparto(idSolicitud, usuario).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido avisar de la solicitud {idSolicitud} anulada: {ex.Message}", ex));
            }
        }
    }

    /// <summary>Punto 5: «No se ha recogido», el aviso del comentario y «Anular recogida».</summary>
    public partial class ServicioRepartoPropio
    {
        /// <summary>Punto 5: seguimiento, cierre de la parada y avisos de la solicitud. Null: sin ellos (NoRecogida no mira el abono; Anular da 400).</summary>
        public IRecogidasParadaReparto Recogidas { get; set; }

        private async Task AvisarSolicitudRecogida(int? pedido, bool recogidaHecha, string texto, string usuario)
        {
            if (!pedido.HasValue || Recogidas == null)
            {
                return;
            }
            try
            {
                await Recogidas.AvisarSolicitud(pedido.Value, recogidaHecha, texto, usuario).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido avisar de la parada de la recogida {pedido}: {ex.Message}", ex));
            }
        }

        private async Task<SeguimientoNotaRecogida> SeguimientoRecogida(int pedido)
        {
            if (Recogidas == null)
            {
                return null;
            }
            try
            {
                return await Recogidas.LeerSeguimiento(pedido).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido leer el seguimiento de la recogida {pedido}: {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>
        /// POST api/RepartoPropio/Envios/{envioId}/NoRecogida: el repartidor no ha podido recoger la parada de una nota de recogida (motivo
        /// obligatorio). Sale del reparto (Cerrado a mano, «No recogido (reparto propio): motivo»), sin bultos NR. Idempotente por la clave.
        /// </summary>
        public async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> NoRecogida(int envioId, NoRecogidaRepartoDTO peticion, string usuario, OrigenPeticionReparto origen = null)
        {
            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = await NoRecogidaEnvio(envioId, peticion, usuario).ConfigureAwait(false);
            if (resultado.Tipo == TipoResultadoReparto.Ok)
            {
                resultado.Contenido.Estado = ReglasParadaRecogida.ESTADO_NO_RECOGIDO;
                if (resultado.Contenido.EsNueva)
                {
                    await ApuntarTiempoParada(envioId, new DatosTiempoParada
                    {
                        Clave = peticion.ClaveIdempotencia,
                        Resultado = TiempoParadaReparto.RESULTADO_INCIDENCIA,
                        Fin = peticion.FechaHora ?? ahora(),
                        Ir = peticion.FechaHoraIr,
                        Llegada = peticion.FechaHoraLlegada,
                        Latitud = peticion.Latitud,
                        Longitud = peticion.Longitud
                    }, usuario).ConfigureAwait(false);
                    await AvisarSolicitudRecogida(resultado.Contenido.EnvioEntregado?.Pedido, false, peticion.Motivo.Trim(), usuario).ConfigureAwait(false);
                }
            }
            Avisar(resultado.Tipo, usuario, RepartoCambiadoDTO.MOTIVO_INCIDENCIA, origen);
            return resultado;
        }

        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> NoRecogidaEnvio(int envioId, NoRecogidaRepartoDTO peticion, string usuario)
        {
            if (NucleoParadasReparto.EsReposicion(envioId))
            {
                return Invalido("«No se ha recogido» solo vale para las notas de recogida.");
            }
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la ClaveIdempotencia.");
            }
            string error = ReglasParadaRecogida.ValidarTexto(peticion.Motivo, true, "El motivo");
            if (error != null)
            {
                return Invalido(error);
            }
            string motivo = peticion.Motivo.Trim();
            EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return NoEncontrado(envioId);
            }
            ResultadoReparto<EstadoEnvioRepartoDTO> repetida = await YaRegistrada(peticion.ClaveIdempotencia, envioId, PruebaEntregaReparto.TIPO_INCIDENCIA).ConfigureAwait(false);
            if (repetida != null)
            {
                return repetida;
            }
            if (envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return Conflicto($"El envío {envio.Numero} ya está recogido.");
            }
            if (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO)
            {
                return Conflicto(NoEstaEnReparto(envio));
            }
            NotaParadaReparto nota = await NotaDe(envio.Pedido).ConfigureAwait(false);
            if (nota?.EsRecogida != true)
            {
                return Invalido("«No se ha recogido» solo vale para las notas de recogida.");
            }
            string yaRecogida = ReglasParadaRecogida.MotivoYaRecogida(nota.CodigoDocumento, await SeguimientoRecogida(nota.Pedido).ConfigureAwait(false));
            if (yaRecogida != null)
            {
                return new ResultadoReparto<EstadoEnvioRepartoDTO> { Tipo = TipoResultadoReparto.Conflicto, Mensaje = yaRecogida, Codigo = ReglasParadaRecogida.CODIGO_YA_RECOGIDA };
            }

            DateTime fechaHora = peticion.FechaHora ?? ahora();
            var prueba = new PruebaEntregaReparto
            {
                EnvioId = envio.Numero,
                ClaveIdempotencia = peticion.ClaveIdempotencia,
                Tipo = PruebaEntregaReparto.TIPO_INCIDENCIA,
                FechaHora = fechaHora,
                Motivo = IncidenciaRepartoDTO.OTRO,
                Texto = "No se ha recogido: " + motivo,
                Latitud = peticion.Latitud,
                Longitud = peticion.Longitud,
                Usuario = usuario,
                FechaRegistro = ahora()
            };
            // Carlos 20/11/26: con Scripts/RepartoRecogidasNoRecogidas.sql la recogida NO se cierra: queda Incidentada y fuera de todos los
            // días (FechaRepartoPropio.FECHA_PENDIENTE_DECIDIR) hasta que Almacén decida si vuelve al reparto o se anula. Sin él, como antes.
            bool almacenDecide = RecogidasPendientesDisponibles();
            List<EnvioHistoria> historia = Historia(envio, "Reparto propio: no se ha recogido. Motivo: " + motivo
                + (almacenDecide ? ". Pendiente de que Almacén decida si vuelve al reparto." : string.Empty), usuario);
            DateTime? fechaAnterior = envio.FechaEntrega;
            if (almacenDecide)
            {
                // Antes que el envío: si algo falla después, la fila sola no sale en la lista (exige el envío Incidentado).
                await Recogidas.ApuntarNoRecogida(new RecogidaNoRecogidaPendiente
                {
                    Pedido = nota.Pedido,
                    EnvioId = envio.Numero,
                    Motivo = motivo,
                    Repartidor = usuario,
                    FechaHora = fechaHora,
                    FechaEntregaAnterior = fechaAnterior,
                    ClaveIdempotencia = peticion.ClaveIdempotencia
                }).ConfigureAwait(false);
                envio.Estado = Constantes.Agencias.ESTADO_INCIDENTADO;
                envio.FechaEntrega = FechaRepartoPropio.FECHA_PENDIENTE_DECIDIR;
            }
            else
            {
                envio.Estado = Constantes.Agencias.ESTADO_CERRADO_A_MANO;
            }
            envio.DetalleEstado = ReglasParadaRecogida.Detalle(ReglasParadaRecogida.PREFIJO_NO_RECOGIDO, motivo);
            envio.Usuario = usuario;
            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = await Registrar(prueba, null, envio, historia, null, usuario, ReglasParadaRecogida.ESTADO_NO_RECOGIDO).ConfigureAwait(false);
            if (resultado.Tipo == TipoResultadoReparto.Ok && resultado.Contenido != null && resultado.Contenido.EsNueva)
            {
                resultado.Contenido.EnvioEntregado = envio;
                if (almacenDecide)
                {
                    await Recogidas.AvisarAlmacen(ReglasParadaRecogida.NotificacionNoRecogida(envio, nota.CodigoDocumento, motivo, usuario)).ConfigureAwait(false);
                }
            }
            return resultado;
        }

        /// <summary>Carlos 20/11/26: ¿está Scripts/RepartoRecogidasNoRecogidas.sql? Si no se puede saber, no (como antes).</summary>
        private bool RecogidasPendientesDisponibles()
        {
            if (Recogidas == null)
            {
                return false;
            }
            try
            {
                return Recogidas.PendientesDisponibles();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido saber si están las recogidas no hechas: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>Tests: el día de ruta interna del cliente (ruta, CP, empresa, cliente, contacto). Null: DiasRepartoCodigosPostales.DiaRutaInterna.</summary>
        public Func<string, string, string, string, string, DayOfWeek?> DiaRutaInternaCliente { get; set; }

        private DayOfWeek? DiaRutaDe(RecogidaNoRecogidaPendiente p)
        {
            try
            {
                return DiaRutaInternaCliente != null
                    ? DiaRutaInternaCliente(p.Ruta, p.CodPostal, p.Empresa, p.Cliente, p.Contacto)
                    : DiasRepartoCodigosPostales.DiaRutaInterna(p.Ruta?.Trim(), p.CodPostal?.Trim(), p.Empresa?.Trim(), p.Cliente?.Trim(), p.Contacto?.Trim());
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido leer el día de reparto de la recogida {p.Pedido}: {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>
        /// GET api/RepartoPropio/Recogidas/NoRecogidas: las recogidas no hechas que esperan a que Almacén decida. Sin el script, 503.
        /// </summary>
        public async Task<ResultadoReparto<List<RecogidaNoRecogidaDTO>>> RecogidasNoRecogidas()
        {
            if (!RecogidasPendientesDisponibles())
            {
                return ResultadoReparto<List<RecogidaNoRecogidaDTO>>.Mal(TipoResultadoReparto.NoDisponible, ReglasParadaRecogida.MENSAJE_FALTA_SCRIPT_NO_RECOGIDAS);
            }
            return ResultadoReparto<List<RecogidaNoRecogidaDTO>>.Bien(await Recogidas.LeerNoRecogidas().ConfigureAwait(false) ?? new List<RecogidaNoRecogidaDTO>());
        }

        /// <summary>
        /// POST api/RepartoPropio/Recogidas/{pedido}/Reprogramar: la MISMA nota y la misma parada vuelven al reparto, el día pedido o, sin él, el
        /// siguiente día de reparto propio del cliente (<see cref="ReglasParadaRecogida.FechaReprogramada"/>), sin repartidor (sus cargas se dan
        /// por descargadas: hay que volver a leer el papel al cargar), con historia, rastro y aviso «repartoCambiado» (Parada). 409 si no está
        /// pendiente de decidir; 503 sin el script.
        /// </summary>
        public async Task<ResultadoReparto<ResultadoReprogramarRecogidaDTO>> ReprogramarRecogida(int pedido, ReprogramarRecogidaDTO peticion, string usuario,
            OrigenPeticionReparto origen = null)
        {
            if (!RecogidasPendientesDisponibles())
            {
                return ResultadoReparto<ResultadoReprogramarRecogidaDTO>.Mal(TipoResultadoReparto.NoDisponible, ReglasParadaRecogida.MENSAJE_FALTA_SCRIPT_NO_RECOGIDAS);
            }
            DateTime hoy = ahora().Date;
            string error = ReglasParadaRecogida.ValidarFechaReprogramar(peticion?.Fecha, hoy);
            if (error != null)
            {
                return ResultadoReparto<ResultadoReprogramarRecogidaDTO>.Mal(TipoResultadoReparto.Invalido, error);
            }
            string documento = CodigoNotaEntrega.Documento(true, pedido);
            RecogidaNoRecogidaPendiente pendiente = await Recogidas.LeerPendiente(pedido).ConfigureAwait(false);
            if (pendiente == null)
            {
                return ResultadoReparto<ResultadoReprogramarRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto,
                    $"La recogida {documento} no está pendiente de decidir: actualice la lista.");
            }
            DateTime fecha = peticion?.Fecha?.Date
                ?? ReglasParadaRecogida.FechaReprogramada(hoy, DiaRutaDe(pendiente), paradas.EsDiaSinReparto ?? EsFinDeSemana);
            EnviosAgencia envio = await repositorio.LeerEnvio(pendiente.EnvioId).ConfigureAwait(false);
            if (envio == null)
            {
                return ResultadoReparto<ResultadoReprogramarRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto,
                    $"La parada de la recogida {documento} ya no existe: actualice la lista.");
            }
            List<MovimientoBultoReparto> descargas = await DescargasAlReprogramar(pendiente, usuario).ConfigureAwait(false);
            List<EnvioHistoria> historia = Historia(envio, $"Recogida {documento}: Almacén decide que vuelve al reparto del {fecha:dd/MM/yyyy}" +
                $" (no se recogió: {pendiente.Motivo?.Trim()}). Sin repartidor: hay que leer el papel al cargarla.", usuario);
            historia.Add(new EnvioHistoria
            {
                NumeroEnvio = envio.Numero,
                Campo = "FechaEntrega",
                ValorAnterior = (envio.FechaEntrega ?? envio.Fecha).ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
                Observaciones = historia[0].Observaciones,
                Usuario = usuario,
                FechaModificacion = historia[0].FechaModificacion
            });
            string anterior = JsonConvert.SerializeObject(new { Pedido = pedido, Envio = envio.Numero, envio.Estado, envio.FechaEntrega, DetalleEstado = envio.DetalleEstado?.Trim(), pendiente.Motivo });
            string nuevo = JsonConvert.SerializeObject(new { Pedido = pedido, Envio = envio.Numero, Estado = Constantes.Agencias.ESTADO_TRAMITADO, FechaEntrega = fecha,
                Descargados = descargas.Select(d => d.Codigo).ToList() });
            if (!await Recogidas.Reprogramar(pendiente, fecha, historia, descargas, anterior, nuevo, usuario).ConfigureAwait(false))
            {
                return ResultadoReparto<ResultadoReprogramarRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto,
                    $"La recogida {documento} ha cambiado mientras: actualice la lista.");
            }
            Avisar(TipoResultadoReparto.Ok, usuario, RepartoCambiadoDTO.MOTIVO_PARADA, origen);
            return ResultadoReparto<ResultadoReprogramarRecogidaDTO>.Bien(new ResultadoReprogramarRecogidaDTO
            {
                Pedido = pedido,
                CodigoDocumento = documento,
                EnvioId = envio.Numero,
                FechaEntrega = fecha,
                Mensaje = ReglasParadaRecogida.MensajeReprogramada(documento, fecha)
            });
        }

        private static bool EsFinDeSemana(DateTime dia) => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday;

        /// <summary>Las cargas que siguen valiendo de la parada se dan por descargadas en Algete (Origen Almacen): vuelve sin repartidor.</summary>
        private async Task<List<MovimientoBultoReparto>> DescargasAlReprogramar(RecogidaNoRecogidaPendiente pendiente, string usuario)
        {
            var descargas = new List<MovimientoBultoReparto>();
            if (!CargaDisponible() || !FurgonetaDisponible())
            {
                return descargas;
            }
            List<CargaBultoReparto> cargas = await SinLasDescargadas(await carga.LeerCargas(new[] { pendiente.EnvioId }).ConfigureAwait(false)).ConfigureAwait(false);
            DateTime momento = ahora();
            foreach (int bulto in cargas.Select(c => c.Bulto).Distinct().OrderBy(b => b))
            {
                string codigo = CodigoBultoFurgoneta.CodigoRp(pendiente.EnvioId, bulto);
                descargas.Add(new MovimientoBultoReparto
                {
                    Codigo = codigo,
                    TipoCodigo = CodigoBultoFurgoneta.RP,
                    EnvioId = pendiente.EnvioId,
                    Pedido = pendiente.Pedido,
                    Bulto = bulto,
                    Tipo = TiposMovimientoBulto.DESCARGADO,
                    Usuario = usuario?.Trim(),
                    FechaHora = momento,
                    FechaRegistro = momento,
                    Origen = OrigenesMovimientoBulto.ALMACEN,
                    Descripcion = $"Recogida {pendiente.Pedido} reprogramada: vuelve sin repartidor",
                    ClaveIdempotencia = NucleoFurgoneta.ClaveDerivada(pendiente.ClaveIdempotencia, codigo, ReglasParadaRecogida.TIPO_CLAVE_DESCARGA_REPROGRAMAR)
                });
            }
            return descargas;
        }

        /// <summary>
        /// POST api/RepartoPropio/Recogidas/{pedido}/Anular: una nota de recogida YA en reparto que ya no hace falta. La parada sale del
        /// reparto (Cerrado a mano, «Recogida anulada: motivo») con historia y rastro en Modificaciones; si viene de una solicitud, la
        /// solicitud queda Anulada. 409 si sus bultos NR ya están en una furgoneta o está abonada. La nota (sus líneas) no se toca.
        /// </summary>
        /// <param name="puedeAnularMarcada">Caso 930553: quien llama es de Almacén o Dirección (los únicos que pueden anular una recogida ya
        /// marcada «Recogido»).</param>
        public async Task<ResultadoReparto<ResultadoAnularRecogidaDTO>> AnularRecogida(int pedido, AnularRecogidaRepartoDTO peticion, string usuario,
            OrigenPeticionReparto origen = null, bool puedeAnularMarcada = false)
        {
            string error = ReglasParadaRecogida.ValidarTexto(peticion?.Motivo, true, "El motivo");
            if (error != null)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Invalido, error);
            }
            if (Recogidas == null || paradas.Notas == null || paradas.NotasParada == null)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Invalido, "No se pueden anular recogidas desde aquí. Avise a Informática.");
            }
            string motivo = peticion.Motivo.Trim();
            string documento = CodigoNotaEntrega.Documento(true, pedido);
            NotaParadaReparto nota = await NotaDe(pedido).ConfigureAwait(false);
            if (nota?.EsRecogida != true)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe la nota de recogida {documento}.");
            }
            ResolucionCodigoNota resolucion = await paradas.Notas.Resolver(documento).ConfigureAwait(false);
            if (resolucion?.EnvioId == null && resolucion?.EnvioEntregado != null)
            {
                return await AnularRecogidaMarcada(pedido, documento, resolucion.EnvioEntregado.Value, motivo, usuario, origen, puedeAnularMarcada).ConfigureAwait(false);
            }
            if (resolucion?.EnvioId == null)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto, resolucion?.EnvioEntregado != null
                    ? $"La recogida {documento} ya está hecha: no se puede anular."
                    : $"La nota {documento} no tiene parada en el reparto: no hay nada que anular aquí (si viene de una solicitud, anule la solicitud).");
            }
            string yaRecogida = ReglasParadaRecogida.MotivoYaRecogida(documento, await SeguimientoRecogida(pedido).ConfigureAwait(false));
            if (yaRecogida != null)
            {
                return new ResultadoReparto<ResultadoAnularRecogidaDTO> { Tipo = TipoResultadoReparto.Conflicto, Mensaje = yaRecogida, Codigo = ReglasParadaRecogida.CODIGO_YA_RECOGIDA };
            }
            EnviosAgencia envio = await repositorio.LeerEnvio(resolucion.EnvioId.Value).ConfigureAwait(false);
            if (envio == null || (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO))
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto, $"La parada de la recogida {documento} ya no está en el reparto: actualice la lista.");
            }
            string detalle = ReglasParadaRecogida.Detalle(ReglasParadaRecogida.PREFIJO_ANULADA, motivo);
            List<EnvioHistoria> historia = Historia(envio, $"Recogida {documento} anulada: {motivo}", usuario);
            string anterior = JsonConvert.SerializeObject(new { Pedido = pedido, Envio = envio.Numero, envio.Estado, DetalleEstado = envio.DetalleEstado?.Trim() });
            string nuevo = JsonConvert.SerializeObject(new { Pedido = pedido, Envio = envio.Numero, Estado = Constantes.Agencias.ESTADO_CERRADO_A_MANO, DetalleEstado = detalle, Motivo = motivo });
            if (!await Recogidas.CerrarParada(envio.Numero, detalle, historia, ReglasParadaRecogida.TABLA_MODIFICACIONES_ANULADA, anterior, nuevo, usuario).ConfigureAwait(false))
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto, $"La parada de la recogida {documento} ha cambiado mientras: actualice la lista.");
            }
            await Recogidas.AnularSolicitud(pedido, motivo, usuario).ConfigureAwait(false);
            // Carlos 20/11/26: si esperaba a que Almacén decidiera tras «No se ha recogido», se cierra del todo
            if (RecogidasPendientesDisponibles())
            {
                await Recogidas.CerrarPendiente(pedido, usuario).ConfigureAwait(false);
            }
            // NestoAPI#914: señal de calidad (en segundo plano; nunca lanza)
            Calidad.RegistroCalidad.Apuntar(Calidad.TiposEventoCalidad.RECOGIDA_ANULADA, usuario, envio.Empresa, pedido, envio.Cliente, envio.Contacto,
                $"Envio {envio.Numero}", detalle: new { Motivo = motivo });
            Avisar(TipoResultadoReparto.Ok, usuario, RepartoCambiadoDTO.MOTIVO_PARADA, origen);
            return ResultadoReparto<ResultadoAnularRecogidaDTO>.Bien(new ResultadoAnularRecogidaDTO
            {
                Pedido = pedido,
                CodigoDocumento = documento,
                EnvioId = envio.Numero,
                Estado = ReglasParadaRecogida.ESTADO_ANULADA,
                Mensaje = $"Recogida {documento} anulada: la parada {envio.Numero} ya no está en el reparto."
            });
        }

        /// <summary>
        /// Caso 930553 (Carlos 26/11/26): el repartidor marcó «Recogido» por error (la parada quedó Entregada) y la nota se queda esperando
        /// un abono que no llegará. Solo Almacén o Dirección (403) y sin abono de la recogida (409 RECOGIDA_ABONADA). En UNA transacción:
        /// Cerrado a mano con «Recogida anulada (marcada por error): motivo», historia, rastro RecogidaAnulada, la solicitud a Anulada y los
        /// bultos NR que sigan en una furgoneta, Descargado (Origen Almacen). Las líneas de la nota no se tocan.
        /// </summary>
        private async Task<ResultadoReparto<ResultadoAnularRecogidaDTO>> AnularRecogidaMarcada(int pedido, string documento, int envioId, string motivo,
            string usuario, OrigenPeticionReparto origen, bool puedeAnularMarcada)
        {
            if (!puedeAnularMarcada)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Prohibido,
                    ReglasParadaRecogida.MotivoNoAnulableMarcada(documento, false, null));
            }
            SeguimientoNotaRecogida seguimiento = await SeguimientoRecogida(pedido).ConfigureAwait(false);
            if (seguimiento == null)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.NoDisponible,
                    $"No se ha podido comprobar si la recogida {documento} está abonada. Inténtelo de nuevo y, si sigue, avise a Informática.");
            }
            string noSePuede = ReglasParadaRecogida.MotivoNoAnulableMarcada(documento, true, seguimiento);
            if (noSePuede != null)
            {
                return new ResultadoReparto<ResultadoAnularRecogidaDTO> { Tipo = TipoResultadoReparto.Conflicto, Mensaje = noSePuede, Codigo = ReglasParadaRecogida.CODIGO_ABONADA };
            }
            EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
            if (envio == null || envio.Estado != Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto, $"La parada de la recogida {documento} ha cambiado mientras: actualice la lista.");
            }
            List<MovimientoBultoReparto> descargas = await DescargasAlAnularMarcada(pedido, envioId, usuario).ConfigureAwait(false);
            string detalle = ReglasParadaRecogida.Detalle(ReglasParadaRecogida.PREFIJO_ANULADA_MARCADA, motivo);
            List<EnvioHistoria> historia = Historia(envio, $"Recogida {documento} anulada: estaba marcada como recogida por error. Motivo: {motivo}" +
                (descargas.Count > 0 ? $". Se dan por descargados en Algete: {string.Join(", ", descargas.Select(d => d.Codigo))}." : string.Empty), usuario);
            string anterior = JsonConvert.SerializeObject(new { Pedido = pedido, Envio = envio.Numero, envio.Estado, DetalleEstado = envio.DetalleEstado?.Trim() });
            string nuevo = JsonConvert.SerializeObject(new { Pedido = pedido, Envio = envio.Numero, Estado = Constantes.Agencias.ESTADO_CERRADO_A_MANO, DetalleEstado = detalle,
                Motivo = motivo, MarcadaPorError = true, Descargados = descargas.Select(d => d.Codigo).ToList() });
            AnulacionRecogidaMarcada hecho = await Recogidas.AnularRecogidaMarcada(envio.Numero, pedido, detalle, motivo, historia, descargas, anterior, nuevo, usuario).ConfigureAwait(false);
            if (hecho == null)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto,
                    $"La recogida {documento} ha cambiado mientras (o ya está abonada): actualice la lista.");
            }
            foreach (int solicitud in hecho.SolicitudesAnuladas ?? new List<int>())
            {
                await Recogidas.AvisarSolicitudAnulada(solicitud, usuario).ConfigureAwait(false);
            }
            // NestoAPI#914: señal de calidad (en segundo plano; nunca lanza). A quién se imputa (quién marcó «Recogido») no se sabe aquí.
            Calidad.RegistroCalidad.Apuntar(Calidad.TiposEventoCalidad.RECOGIDA_MARCADA_POR_ERROR, usuario, envio.Empresa, pedido, envio.Cliente, envio.Contacto,
                $"Envio {envio.Numero}", detalle: new { Motivo = motivo });
            Avisar(TipoResultadoReparto.Ok, usuario, RepartoCambiadoDTO.MOTIVO_PARADA, origen);
            return ResultadoReparto<ResultadoAnularRecogidaDTO>.Bien(new ResultadoAnularRecogidaDTO
            {
                Pedido = pedido,
                CodigoDocumento = documento,
                EnvioId = envio.Numero,
                Estado = ReglasParadaRecogida.ESTADO_ANULADA,
                Mensaje = $"Recogida {documento} anulada: estaba marcada como recogida por error y ya no espera ningún abono." +
                    (descargas.Count > 0 ? $" Sus bultos ({string.Join(", ", descargas.Select(d => d.Codigo))}) se dan por descargados en Algete." : string.Empty)
            });
        }

        /// <summary>Caso 930553: los bultos NR de la nota que siguen en una furgoneta (último movimiento Cargado o Recogido) → Descargado (Almacén).</summary>
        private async Task<List<MovimientoBultoReparto>> DescargasAlAnularMarcada(int pedido, int envioId, string usuario)
        {
            if (!FurgonetaDisponible())
            {
                return new List<MovimientoBultoReparto>();
            }
            List<MovimientoBultoReparto> historia = await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Pedidos = new[] { pedido } }).ConfigureAwait(false)
                ?? new List<MovimientoBultoReparto>();
            DateTime momento = ahora();
            return NucleoFurgoneta.Ultimos(historia.Where(m => m.TipoCodigo == CodigoBultoFurgoneta.NR && m.Pedido == pedido))
                .Where(u => TiposMovimientoBulto.DejaEnFurgoneta(u.Tipo))
                .OrderBy(u => u.Bulto)
                .Select(u =>
                {
                    MovimientoBultoReparto d = Copia(u, TiposMovimientoBulto.DESCARGADO, usuario?.Trim(), momento, momento, OrigenesMovimientoBulto.ALMACEN,
                        NucleoFurgoneta.ClaveDerivada(u.ClaveIdempotencia, u.Codigo, ReglasParadaRecogida.TIPO_CLAVE_DESCARGA_ANULAR));
                    d.EnvioId = d.EnvioId ?? envioId;
                    d.Descripcion = $"Recogida {pedido} anulada (marcada por error): se da por descargado";
                    return d;
                })
                .ToList();
        }
    }
}
