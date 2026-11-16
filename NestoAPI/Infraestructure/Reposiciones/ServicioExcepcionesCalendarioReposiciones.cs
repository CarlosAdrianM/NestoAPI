using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#867: acceso a dbo.ReposicionesCalendarioExcepciones (sustituible en los tests).</summary>
    public interface IRepositorioExcepcionesCalendarioReposiciones
    {
        /// <summary>¿Se ha lanzado Scripts/Issue867_ReposicionesCalendarioExcepciones.sql?</summary>
        Task<bool> TablaCreada();
        /// <summary>Las de la empresa con llegada desde <paramref name="desde"/> (incluido).</summary>
        Task<List<ExcepcionCalendarioReposicionDTO>> Leer(string empresa, DateTime desde);
        Task<ExcepcionCalendarioReposicionDTO> LeerPorId(int id);
        /// <summary>Las filas ACTIVAS del calendario semanal de la empresa.</summary>
        Task<List<ReposicionCalendario>> LeerCalendario(string empresa);
        /// <summary>Alta o cambio (la clave es empresa + origen + destino + fecha), con su fila en Modificaciones. Devuelve el Id.</summary>
        Task<int> Guardar(ExcepcionCalendarioReposicionDTO excepcion, string usuario);
        /// <summary>Baja, con su fila en Modificaciones. False si ya no estaba.</summary>
        Task<bool> Borrar(int id, string usuario);
    }

    public interface IServicioExcepcionesCalendarioReposiciones
    {
        Task<List<ExcepcionCalendarioReposicionDTO>> Leer(string empresa, DateTime? desde);
        Task<ExcepcionCalendarioReposicionDTO> Guardar(ExcepcionCalendarioReposicionDTO excepcion, string usuario);
        Task<ExcepcionCalendarioReposicionDTO> Borrar(int id, string usuario);
    }

    /// <summary>
    /// NestoAPI#867 (sugerencia 1275): excepciones por fecha del calendario de reposiciones: quitar un día concreto que el
    /// calendario semanal sí tiene (inventario, cierre…) o añadir uno que no tiene. Lo que valen las aplica
    /// <see cref="ReglaCalendarioReposiciones"/>; aquí se validan y se guardan. Validaciones:
    /// <list type="bullet">
    /// <item>La fecha (de llegada), de hoy en adelante; los almacenes, los de reposición y distintos; motivo obligatorio.</item>
    /// <item>Quitar: solo un día que el calendario tenga (fila activa de ese día de la semana) y en que haya viaje (no festivo).</item>
    /// <item>Añadir: solo en una ruta con calendario y un día que no tenga; con la antelación de la ruta (las horas, si no
    /// vienen, las de la ruta); y que ese día pueda haber viaje (laborable en los dos, con reparto propio).</item>
    /// <item>Nada que toque un viaje cuya hora de cierre ya ha pasado (ya se ha rellenado, o se tenía que haber rellenado):
    /// 409. Ni crear, ni cambiar, ni borrar.</item>
    /// </list>
    /// </summary>
    public class ServicioExcepcionesCalendarioReposiciones : IServicioExcepcionesCalendarioReposiciones
    {
        public const string MENSAJE_SIN_TABLA =
            "Falta lanzar Scripts/Issue867_ReposicionesCalendarioExcepciones.sql en la base de datos: todavía no se pueden poner excepciones en el calendario de reposiciones.";

        public const int LONGITUD_MAXIMA_MOTIVO = 200;

        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        private readonly IRepositorioExcepcionesCalendarioReposiciones repositorio;
        private readonly Func<DateTime, string, bool> esFestivo;
        private readonly Func<DateTime> reloj;
        private readonly Action invalidar;

        public ServicioExcepcionesCalendarioReposiciones(NVEntities db)
            : this(new RepositorioExcepcionesCalendarioReposicionesSql(db), GestorFestivos.EsFestivo, () => DateTime.Now,
                  ExcepcionesCalendarioReposiciones.Invalidar)
        {
        }

        internal ServicioExcepcionesCalendarioReposiciones(IRepositorioExcepcionesCalendarioReposiciones repositorio,
            Func<DateTime, string, bool> esFestivo, Func<DateTime> reloj, Action invalidar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.esFestivo = esFestivo ?? GestorFestivos.EsFestivo;
            this.reloj = reloj ?? (() => DateTime.Now);
            this.invalidar = invalidar ?? (() => { });
        }

        public async Task<List<ExcepcionCalendarioReposicionDTO>> Leer(string empresa, DateTime? desde)
        {
            await ComprobarTabla().ConfigureAwait(false);
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            DateTime ahora = reloj();
            DateTime desdeLimpio = (desde ?? ahora).Date;
            List<ExcepcionCalendarioReposicionDTO> todas = await repositorio.Leer(empresaLimpia, MinimaFecha(desdeLimpio, ahora)).ConfigureAwait(false)
                ?? new List<ExcepcionCalendarioReposicionDTO>();
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(empresaLimpia).ConfigureAwait(false)
                ?? new List<ReposicionCalendario>();
            foreach (ExcepcionCalendarioReposicionDTO excepcion in todas)
            {
                excepcion.CierraEl = CierreQueManda(excepcion, todas, calendario);
            }
            return todas
                .Where(e => e.FechaLlegada.Date >= desdeLimpio)
                .OrderBy(e => e.FechaLlegada).ThenBy(e => e.Origen).ThenBy(e => e.Destino)
                .ToList();
        }

        public async Task<ExcepcionCalendarioReposicionDTO> Guardar(ExcepcionCalendarioReposicionDTO excepcion, string usuario)
        {
            if (excepcion == null)
            {
                throw new NestoBusinessException("Faltan los datos de la excepción.");
            }
            await ComprobarTabla().ConfigureAwait(false);
            ExcepcionCalendarioReposicionDTO nueva = ValidarDatos(excepcion);
            DateTime ahora = reloj();
            if (nueva.FechaLlegada < ahora.Date)
            {
                throw new NestoBusinessException("No se puede poner una excepción en un día que ya ha pasado.");
            }

            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(nueva.Empresa).ConfigureAwait(false)
                ?? new List<ReposicionCalendario>();
            List<ReposicionCalendario> filasRuta = calendario
                .Where(f => f != null && f.Activo
                    && ReglaCalendarioReposiciones.Limpiar(f.AlmacenOrigen) == nueva.Origen
                    && ReglaCalendarioReposiciones.Limpiar(f.AlmacenDestino) == nueva.Destino)
                .ToList();
            if (!filasRuta.Any())
            {
                throw new NestoBusinessException($"No hay ninguna reposición de {nueva.Origen} a {nueva.Destino} en el calendario: " +
                    "las excepciones solo se pueden poner en una ruta que tenga calendario.");
            }

            List<ExcepcionCalendarioReposicionDTO> existentes = await repositorio.Leer(nueva.Empresa, MinimaFecha(nueva.FechaLlegada, ahora)).ConfigureAwait(false)
                ?? new List<ExcepcionCalendarioReposicionDTO>();
            ExcepcionCalendarioReposicionDTO anterior = existentes.FirstOrDefault(e => MismaClave(e, nueva));
            List<ExcepcionCalendarioReposicionDTO> otras = existentes.Where(e => !MismaClave(e, nueva)).ToList();
            byte diaSemana = CalculadoraFechaReposicion.DiaSemana(nueva.FechaLlegada);
            bool estaEnElCalendario = filasRuta.Any(f => f.DiaSemana == diaSemana);
            string elDia = ElDia(nueva.FechaLlegada);

            if (nueva.Tipo == TiposExcepcionCalendarioReposicion.SIN_REPOSICION)
            {
                if (!estaEnElCalendario)
                {
                    throw new NestoBusinessException($"{Mayuscula(elDia)} no hay reposición de {nueva.Origen} a {nueva.Destino} en el calendario: no hay nada que quitar.");
                }
                if (Viaje(filasRuta, otras, nueva) == null)
                {
                    throw new NestoBusinessException($"{Mayuscula(elDia)} ya no hay reposición de {nueva.Origen} a {nueva.Destino} (es festivo en alguno de los dos almacenes " +
                        "o no hay reparto propio): no hace falta quitarla.");
                }
            }
            else
            {
                if (estaEnElCalendario)
                {
                    throw new NestoBusinessException($"{Mayuscula(elDia)} ya hay reposición de {nueva.Origen} a {nueva.Destino} en el calendario: no hace falta un día extra " +
                        "(si quiere otra hora, cámbiela en el calendario).");
                }
                CompletarHorasExtra(nueva, filasRuta);
                if (Viaje(filasRuta, otras.Concat(new[] { nueva }), nueva) == null)
                {
                    throw new NestoBusinessException($"{Mayuscula(elDia)} no puede haber reposición de {nueva.Origen} a {nueva.Destino}: es festivo en alguno de los dos " +
                        "almacenes o no hay reparto propio.");
                }
            }

            // Ni el viaje que había (el del calendario o el de la excepción anterior) ni el que va a haber pueden haber cerrado ya.
            ComprobarQueNoHaCerrado(nueva, ahora,
                Viaje(filasRuta, otras, nueva),
                anterior == null ? null : Viaje(filasRuta, otras.Concat(new[] { anterior }), nueva),
                Viaje(filasRuta, otras.Concat(new[] { nueva }), nueva));

            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(SinDominio(usuario));
            nueva.Id = await repositorio.Guardar(nueva, usuarioAuditoria).ConfigureAwait(false);
            nueva.Usuario = usuarioAuditoria;
            nueva.FechaModificacion = ahora;
            nueva.CierraEl = CierreQueManda(nueva, otras.Concat(new[] { nueva }).ToList(), calendario);
            invalidar();
            return nueva;
        }

        public async Task<ExcepcionCalendarioReposicionDTO> Borrar(int id, string usuario)
        {
            await ComprobarTabla().ConfigureAwait(false);
            ExcepcionCalendarioReposicionDTO excepcion = await repositorio.LeerPorId(id).ConfigureAwait(false);
            if (excepcion == null)
            {
                throw new NestoBusinessException($"No existe la excepción {id} del calendario de reposiciones (puede que ya la haya borrado otra persona).")
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }
            DateTime ahora = reloj();
            excepcion.Empresa = EmpresaOPorDefecto(excepcion.Empresa);
            excepcion.Origen = ReglaCalendarioReposiciones.Limpiar(excepcion.Origen);
            excepcion.Destino = ReglaCalendarioReposiciones.Limpiar(excepcion.Destino);
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(excepcion.Empresa).ConfigureAwait(false)
                ?? new List<ReposicionCalendario>();
            List<ExcepcionCalendarioReposicionDTO> otras = ((await repositorio.Leer(excepcion.Empresa, MinimaFecha(excepcion.FechaLlegada, ahora)).ConfigureAwait(false))
                    ?? new List<ExcepcionCalendarioReposicionDTO>())
                .Where(e => !MismaClave(e, excepcion))
                .ToList();
            ComprobarQueNoHaCerrado(excepcion, ahora,
                Viaje(calendario, otras, excepcion),
                Viaje(calendario, otras.Concat(new[] { excepcion }), excepcion));

            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(SinDominio(usuario));
            if (!await repositorio.Borrar(id, usuarioAuditoria).ConfigureAwait(false))
            {
                throw new NestoBusinessException($"No existe la excepción {id} del calendario de reposiciones (puede que ya la haya borrado otra persona).")
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }
            invalidar();
            return excepcion;
        }

        /// <summary>El viaje (corte y llegada) de la ruta de <paramref name="clave"/> que llega ese día con esas excepciones; null si no hay.</summary>
        private CorteReposicion Viaje(IEnumerable<ReposicionCalendario> calendario, IEnumerable<ExcepcionCalendarioReposicionDTO> excepciones,
            ExcepcionCalendarioReposicionDTO clave)
        {
            List<ExcepcionCalendarioReposicionDTO> lista = excepciones.ToList();
            return new CalculadoraFechaReposicion(esFestivo, () => lista)
                .CortesQueLleganElDia(calendario, clave.FechaLlegada)
                .FirstOrDefault(c => c.Empresa == clave.Empresa && c.Origen == clave.Origen && c.Destino == clave.Destino);
        }

        /// <summary>
        /// Para enseñarlo: un día quitado, cuándo cerraba el viaje del calendario; uno extra, cuándo cierra el suyo. Desde ese
        /// momento la excepción ya no se puede tocar.
        /// </summary>
        private DateTime? CierreQueManda(ExcepcionCalendarioReposicionDTO excepcion, IEnumerable<ExcepcionCalendarioReposicionDTO> todas,
            List<ReposicionCalendario> calendario)
        {
            ExcepcionCalendarioReposicionDTO clave = new ExcepcionCalendarioReposicionDTO
            {
                Empresa = EmpresaOPorDefecto(excepcion.Empresa),
                Origen = ReglaCalendarioReposiciones.Limpiar(excepcion.Origen),
                Destino = ReglaCalendarioReposiciones.Limpiar(excepcion.Destino),
                FechaLlegada = excepcion.FechaLlegada.Date
            };
            IEnumerable<ExcepcionCalendarioReposicionDTO> sinEsta = todas.Where(e => !MismaClave(e, clave));
            IEnumerable<ExcepcionCalendarioReposicionDTO> conEsta = sinEsta.Concat(new[] { excepcion });
            bool quitado = TiposExcepcionCalendarioReposicion.Normalizar(excepcion.Tipo) == TiposExcepcionCalendarioReposicion.SIN_REPOSICION;
            return Viaje(calendario, quitado ? sinEsta : conEsta, clave)?.Corte;
        }

        private static void ComprobarQueNoHaCerrado(ExcepcionCalendarioReposicionDTO excepcion, DateTime ahora, params CorteReposicion[] viajes)
        {
            CorteReposicion cerrado = viajes.Where(v => v != null && v.Corte <= ahora).OrderBy(v => v.Corte).FirstOrDefault();
            if (cerrado != null)
            {
                throw new NestoBusinessException(TextoYaCerrada(excepcion.Origen, excepcion.Destino, excepcion.FechaLlegada, cerrado.Corte))
                {
                    StatusCode = HttpStatusCode.Conflict
                };
            }
        }

        internal static string TextoYaCerrada(string origen, string destino, DateTime fechaLlegada, DateTime corte)
        {
            return $"La reposición de {origen} a {destino} que llega {ElDia(fechaLlegada)} cerraba {ElDia(corte)} a las {corte:HH:mm} y esa hora ya ha pasado: " +
                "ya se ha rellenado (o se tenía que haber rellenado), así que ese día ya no se puede cambiar.";
        }

        private static void CompletarHorasExtra(ExcepcionCalendarioReposicionDTO extra, List<ReposicionCalendario> filasRuta)
        {
            ReposicionCalendario modelo = filasRuta.OrderBy(f => f.DiaSemana).ThenBy(f => f.HoraCierre).First();
            extra.HoraCierre = SinSegundos(extra.HoraCierre ?? modelo.HoraCierre);
            extra.HoraLlegadaHabitual = SinSegundos(extra.HoraLlegadaHabitual ?? modelo.HoraLlegadaHabitual);
            extra.LaborablesAntelacionCierre = extra.LaborablesAntelacionCierre ?? modelo.LaborablesAntelacionCierre;
            if (!EsHoraDelDia(extra.HoraCierre.Value) || !EsHoraDelDia(extra.HoraLlegadaHabitual.Value))
            {
                throw new NestoBusinessException("Las horas tienen que estar entre las 00:00 y las 23:59.");
            }
            if (extra.LaborablesAntelacionCierre != modelo.LaborablesAntelacionCierre)
            {
                throw new NestoBusinessException($"El día extra se tiene que cerrar con la misma antelación que el resto de la reposición de {extra.Origen} a {extra.Destino} " +
                    $"({TextoAntelacion(modelo.LaborablesAntelacionCierre)}).");
            }
            if (extra.LaborablesAntelacionCierre == 0 && extra.HoraLlegadaHabitual < extra.HoraCierre)
            {
                throw new NestoBusinessException("La hora de llegada no puede ser anterior a la hora de cierre (la reposición llega el mismo día).");
            }
        }

        private static string TextoAntelacion(byte antelacion) => antelacion == 0
            ? "el mismo día"
            : antelacion == 1 ? "el laborable anterior" : $"{antelacion} laborables antes";

        private static ExcepcionCalendarioReposicionDTO ValidarDatos(ExcepcionCalendarioReposicionDTO excepcion)
        {
            string tipo = TiposExcepcionCalendarioReposicion.Normalizar(excepcion.Tipo);
            if (tipo == null)
            {
                throw new NestoBusinessException($"El tipo de la excepción tiene que ser {TiposExcepcionCalendarioReposicion.SIN_REPOSICION} (quitar un día) " +
                    $"o {TiposExcepcionCalendarioReposicion.REPOSICION_EXTRA} (añadir uno).");
            }
            string origen = ReglaCalendarioReposiciones.Limpiar(excepcion.Origen);
            string destino = ReglaCalendarioReposiciones.Limpiar(excepcion.Destino);
            if (origen == null || destino == null)
            {
                throw new NestoBusinessException("Faltan el almacén de origen y el de destino.");
            }
            if (!ServicioPreparacionReposicion.ALMACENES_REPOSICION.Contains(origen) || !ServicioPreparacionReposicion.ALMACENES_REPOSICION.Contains(destino))
            {
                throw new NestoBusinessException($"Los almacenes de una reposición tienen que ser {string.Join(", ", ServicioPreparacionReposicion.ALMACENES_REPOSICION)}.");
            }
            if (origen == destino)
            {
                throw new NestoBusinessException("El almacén de origen y el de destino no pueden ser el mismo.");
            }
            if (excepcion.FechaLlegada == default(DateTime))
            {
                throw new NestoBusinessException("Falta el día de llegada.");
            }
            string motivo = excepcion.Motivo?.Trim();
            if (string.IsNullOrEmpty(motivo))
            {
                throw new NestoBusinessException("Indique el motivo (por ejemplo, «Inventario del sábado»).");
            }
            if (motivo.Length > LONGITUD_MAXIMA_MOTIVO)
            {
                throw new NestoBusinessException($"El motivo no puede pasar de {LONGITUD_MAXIMA_MOTIVO} caracteres.");
            }
            bool extra = tipo == TiposExcepcionCalendarioReposicion.REPOSICION_EXTRA;
            return new ExcepcionCalendarioReposicionDTO
            {
                Empresa = EmpresaOPorDefecto(excepcion.Empresa),
                Origen = origen,
                Destino = destino,
                FechaLlegada = excepcion.FechaLlegada.Date,
                Tipo = tipo,
                HoraCierre = extra ? excepcion.HoraCierre : null,
                LaborablesAntelacionCierre = extra ? excepcion.LaborablesAntelacionCierre : null,
                HoraLlegadaHabitual = extra ? excepcion.HoraLlegadaHabitual : null,
                Motivo = motivo
            };
        }

        private async Task ComprobarTabla()
        {
            if (!await repositorio.TablaCreada().ConfigureAwait(false))
            {
                throw new NestoBusinessException(MENSAJE_SIN_TABLA) { StatusCode = HttpStatusCode.ServiceUnavailable };
            }
        }

        /// <summary>Para calcular el viaje de un día con antelación hacen falta también las excepciones de unos días antes.</summary>
        private static DateTime MinimaFecha(DateTime fecha, DateTime ahora)
        {
            DateTime desde = fecha.Date.AddDays(-ExcepcionesCalendarioReposiciones.DIAS_ATRAS);
            DateTime hoy = ahora.Date.AddDays(-ExcepcionesCalendarioReposiciones.DIAS_ATRAS);
            return desde < hoy ? desde : hoy;
        }

        private static bool MismaClave(ExcepcionCalendarioReposicionDTO a, ExcepcionCalendarioReposicionDTO b)
        {
            return a != null && b != null
                && EmpresaOPorDefecto(a.Empresa) == EmpresaOPorDefecto(b.Empresa)
                && ReglaCalendarioReposiciones.Limpiar(a.Origen) == ReglaCalendarioReposiciones.Limpiar(b.Origen)
                && ReglaCalendarioReposiciones.Limpiar(a.Destino) == ReglaCalendarioReposiciones.Limpiar(b.Destino)
                && a.FechaLlegada.Date == b.FechaLlegada.Date;
        }

        /// <summary>«el viernes 20/11/2026».</summary>
        internal static string ElDia(DateTime fecha) => "el " + fecha.ToString("dddd dd/MM/yyyy", castellano);

        private static string Mayuscula(string texto) => string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0], castellano) + texto.Substring(1);

        private static TimeSpan SinSegundos(TimeSpan hora) => new TimeSpan(hora.Hours, hora.Minutes, 0);

        private static bool EsHoraDelDia(TimeSpan hora) => hora >= TimeSpan.Zero && hora < TimeSpan.FromDays(1);

        private static string EmpresaOPorDefecto(string empresa) => ReglaCalendarioReposiciones.Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private static string SinDominio(string usuario) => usuario?.Substring(usuario.LastIndexOf('\\') + 1);
    }

    /// <summary>NestoAPI#867: <see cref="IRepositorioExcepcionesCalendarioReposiciones"/> con SQL a mano (la tabla no está en el EDMX).</summary>
    public class RepositorioExcepcionesCalendarioReposicionesSql : IRepositorioExcepcionesCalendarioReposiciones
    {
        internal const string SQL_TABLA_CREADA =
            "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.ReposicionesCalendarioExcepciones', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        private const string COLUMNAS =
            "Id, RTRIM(Empresa) AS Empresa, RTRIM(Origen) AS Origen, RTRIM(Destino) AS Destino, CAST(FechaLlegada AS datetime) AS FechaLlegada, " +
            "RTRIM(Tipo) AS Tipo, HoraCierre, LaborablesAntelacionCierre, HoraLlegadaHabitual, Motivo, Usuario, FechaModificacion";

        internal const string SQL_LEER =
            "SELECT " + COLUMNAS + " FROM dbo.ReposicionesCalendarioExcepciones WHERE Empresa = @empresa AND FechaLlegada >= @desde " +
            "ORDER BY FechaLlegada, Origen, Destino";

        internal const string SQL_LEER_ID = "SELECT " + COLUMNAS + " FROM dbo.ReposicionesCalendarioExcepciones WHERE Id = @id";

        /// <summary>Alta o cambio por la clave única, con el rastro en Modificaciones, en una transacción. Devuelve el Id.</summary>
        internal const string SQL_GUARDAR = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @id int, @anterior nvarchar(400);
SELECT @id = Id,
       @anterior = CONCAT(RTRIM(Tipo), ISNULL(N' cierre ' + CONVERT(char(5), HoraCierre, 108), N''), ISNULL(N' llegada ' + CONVERT(char(5), HoraLlegadaHabitual, 108), N''), N': ', Motivo)
FROM dbo.ReposicionesCalendarioExcepciones WITH (UPDLOCK, HOLDLOCK)
WHERE Empresa = @empresa AND Origen = @origen AND Destino = @destino AND FechaLlegada = @fecha;
IF @id IS NULL
BEGIN
    INSERT INTO dbo.ReposicionesCalendarioExcepciones
        (Empresa, Origen, Destino, FechaLlegada, Tipo, HoraCierre, LaborablesAntelacionCierre, HoraLlegadaHabitual, Motivo, Usuario, FechaModificacion)
    VALUES (@empresa, @origen, @destino, @fecha, @tipo, @horaCierre, @antelacion, @horaLlegada, @motivo, @usuario, GETDATE());
    SET @id = CAST(SCOPE_IDENTITY() AS int);
END
ELSE
BEGIN
    UPDATE dbo.ReposicionesCalendarioExcepciones
    SET Tipo = @tipo, HoraCierre = @horaCierre, LaborablesAntelacionCierre = @antelacion, HoraLlegadaHabitual = @horaLlegada,
        Motivo = @motivo, Usuario = @usuario, FechaModificacion = GETDATE()
    WHERE Id = @id;
END
DECLARE @clave nvarchar(100) = CONCAT(RTRIM(@origen), N'->', RTRIM(@destino), N' llegada ', CONVERT(char(10), @fecha, 103), N' ');
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
VALUES (N'ReposicionesCalendarioExcepciones',
        CONCAT(@clave, ISNULL(@anterior, N'(sin excepción)')),
        CONCAT(@clave, @tipo, ISNULL(N' cierre ' + CONVERT(char(5), @horaCierre, 108), N''), ISNULL(N' llegada ' + CONVERT(char(5), @horaLlegada, 108), N''), N': ', @motivo),
        @usuario);
COMMIT TRANSACTION;
SELECT @id;";

        internal const string SQL_BORRAR = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @anterior nvarchar(400), @clave nvarchar(100);
SELECT @clave = CONCAT(RTRIM(Origen), N'->', RTRIM(Destino), N' llegada ', CONVERT(char(10), FechaLlegada, 103), N' '),
       @anterior = CONCAT(RTRIM(Tipo), ISNULL(N' cierre ' + CONVERT(char(5), HoraCierre, 108), N''), ISNULL(N' llegada ' + CONVERT(char(5), HoraLlegadaHabitual, 108), N''), N': ', Motivo)
FROM dbo.ReposicionesCalendarioExcepciones WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @id;
DECLARE @resultado int = 0;
IF @clave IS NOT NULL
BEGIN
    DELETE FROM dbo.ReposicionesCalendarioExcepciones WHERE Id = @id;
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
    VALUES (N'ReposicionesCalendarioExcepciones', CONCAT(@clave, @anterior), CONCAT(@clave, N'(excepción borrada)'), @usuario);
    SET @resultado = 1;
END
COMMIT TRANSACTION;
SELECT @resultado;";

        private readonly NVEntities db;

        public RepositorioExcepcionesCalendarioReposicionesSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private class FilaLeida
        {
            public int Id { get; set; }
            public string Empresa { get; set; }
            public string Origen { get; set; }
            public string Destino { get; set; }
            public DateTime FechaLlegada { get; set; }
            public string Tipo { get; set; }
            public TimeSpan? HoraCierre { get; set; }
            public byte? LaborablesAntelacionCierre { get; set; }
            public TimeSpan? HoraLlegadaHabitual { get; set; }
            public string Motivo { get; set; }
            public string Usuario { get; set; }
            public DateTime? FechaModificacion { get; set; }
        }

        public Task<bool> TablaCreada()
        {
            return db.Database.SqlQuery<bool>(SQL_TABLA_CREADA).SingleAsync();
        }

        public async Task<List<ExcepcionCalendarioReposicionDTO>> Leer(string empresa, DateTime desde)
        {
            List<FilaLeida> filas = await db.Database.SqlQuery<FilaLeida>(SQL_LEER,
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                new SqlParameter("@desde", SqlDbType.Date) { Value = desde.Date }).ToListAsync().ConfigureAwait(false);
            return filas.Select(ADTO).ToList();
        }

        public async Task<ExcepcionCalendarioReposicionDTO> LeerPorId(int id)
        {
            FilaLeida fila = await db.Database.SqlQuery<FilaLeida>(SQL_LEER_ID, new SqlParameter("@id", id)).FirstOrDefaultAsync().ConfigureAwait(false);
            return fila == null ? null : ADTO(fila);
        }

        public async Task<List<ReposicionCalendario>> LeerCalendario(string empresa)
        {
            return await db.ReposicionesCalendario.AsNoTracking()
                .Where(f => f.Empresa == empresa && f.Activo)
                .ToListAsync().ConfigureAwait(false);
        }

        public Task<int> Guardar(ExcepcionCalendarioReposicionDTO excepcion, string usuario)
        {
            return db.Database.SqlQuery<int>(SQL_GUARDAR,
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = excepcion.Empresa },
                new SqlParameter("@origen", SqlDbType.Char, 3) { Value = excepcion.Origen },
                new SqlParameter("@destino", SqlDbType.Char, 3) { Value = excepcion.Destino },
                new SqlParameter("@fecha", SqlDbType.Date) { Value = excepcion.FechaLlegada.Date },
                new SqlParameter("@tipo", SqlDbType.VarChar, 20) { Value = excepcion.Tipo },
                new SqlParameter("@horaCierre", SqlDbType.Time) { Value = (object)excepcion.HoraCierre ?? DBNull.Value },
                new SqlParameter("@antelacion", SqlDbType.TinyInt) { Value = (object)excepcion.LaborablesAntelacionCierre ?? DBNull.Value },
                new SqlParameter("@horaLlegada", SqlDbType.Time) { Value = (object)excepcion.HoraLlegadaHabitual ?? DBNull.Value },
                new SqlParameter("@motivo", SqlDbType.NVarChar, 200) { Value = excepcion.Motivo ?? string.Empty },
                new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = usuario ?? string.Empty }).SingleAsync();
        }

        public async Task<bool> Borrar(int id, string usuario)
        {
            int resultado = await db.Database.SqlQuery<int>(SQL_BORRAR,
                new SqlParameter("@id", id),
                new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = usuario ?? string.Empty }).SingleAsync().ConfigureAwait(false);
            return resultado == 1;
        }

        private static ExcepcionCalendarioReposicionDTO ADTO(FilaLeida f)
        {
            return new ExcepcionCalendarioReposicionDTO
            {
                Id = f.Id,
                Empresa = f.Empresa?.Trim(),
                Origen = f.Origen?.Trim(),
                Destino = f.Destino?.Trim(),
                FechaLlegada = f.FechaLlegada.Date,
                Tipo = f.Tipo?.Trim(),
                HoraCierre = f.HoraCierre,
                LaborablesAntelacionCierre = f.LaborablesAntelacionCierre,
                HoraLlegadaHabitual = f.HoraLlegadaHabitual,
                Motivo = f.Motivo,
                Usuario = f.Usuario?.Trim(),
                FechaModificacion = f.FechaModificacion
            };
        }
    }
}
