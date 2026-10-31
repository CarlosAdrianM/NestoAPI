using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    // NestoAPI#773 (Carlos 31/10/26): una entrega con reembolso que no se cobra necesita autorización de Administración (o Dirección).

    public static class EstadosAutorizacionReembolso
    {
        public const string PENDIENTE = "Pendiente";
        public const string AUTORIZADA = "Autorizada";
        public const string DENEGADA = "Denegada";
        /// <summary>Nadie contestó y el repartidor dice quién se lo ha autorizado por teléfono: vale, pero Administración lo revisa.</summary>
        public const string TELEFONICA = "Telefonica";

        /// <summary>Con esta, se puede entregar sin cobrar.</summary>
        public static bool PermiteEntregar(string estado) => estado == AUTORIZADA || estado == TELEFONICA;
    }

    /// <summary>POST api/RepartoPropio/Envios/{envioId}/Autorizacion.</summary>
    public class PeticionAutorizacionReembolsoDTO
    {
        /// <summary>Por qué no se cobra (obligatorio): «El cliente dice que paga por transferencia», «No tiene dinero»…</summary>
        public string Motivo { get; set; }
    }

    /// <summary>POST api/RepartoPropio/Autorizaciones/{id}/Responder (solo Administración y Dirección).</summary>
    public class RespuestaAutorizacionReembolsoDTO
    {
        public bool Autorizar { get; set; }
        public string Comentario { get; set; }
    }

    /// <summary>Una autorización (fila de RepartoAutorizacionesReembolso) como la ven la PDA, Nesto y el evento «autorizacionRespondida».</summary>
    public class AutorizacionReembolsoDTO
    {
        public int Id { get; set; }
        public int EnvioId { get; set; }
        public int? Pedido { get; set; }
        public string Cliente { get; set; }
        public string Nombre { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Quien la pide, sin dominio.</summary>
        public string Repartidor { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public string Motivo { get; set; }
        /// <summary><see cref="EstadosAutorizacionReembolso"/>.</summary>
        public string Estado { get; set; }
        /// <summary>Quién contestó (sin dominio) o, en las telefónicas, lo que escribió el repartidor.</summary>
        public string Respondio { get; set; }
        public DateTime? FechaRespuesta { get; set; }
        public string Comentario { get; set; }
        /// <summary>«AutorizacionReembolso:12»: el grupo del AlmacenHub en el que se avisa de la respuesta.</summary>
        public string GrupoAvisos { get; set; }
    }

    /// <summary>Una fila de RepartoAutorizacionesReembolso (Scripts/Issue773_CobroTarjetaReparto.sql).</summary>
    public class AutorizacionReembolsoReparto
    {
        public int Id { get; set; }
        public int EnvioId { get; set; }
        public decimal Importe { get; set; }
        public string Repartidor { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public string Motivo { get; set; }
        public string Estado { get; set; }
        public string Respondio { get; set; }
        public DateTime? FechaRespuesta { get; set; }
        public string Comentario { get; set; }
    }

    public interface IRepositorioAutorizacionesReembolso
    {
        bool ScriptAplicado();
        Task<AutorizacionReembolsoReparto> Leer(int id);
        /// <summary>La última del envío, o null.</summary>
        Task<AutorizacionReembolsoReparto> LeerUltimaDelEnvio(int envioId);
        /// <summary>Devuelve el Id.</summary>
        Task<int> Crear(AutorizacionReembolsoReparto autorizacion);
        /// <summary>
        /// Contesta SOLO si sigue en <paramref name="estadoEsperado"/> (dos administrativos a la vez: gana el primero). False si ya no
        /// lo estaba.
        /// </summary>
        Task<bool> Responder(int id, string estadoEsperado, string estado, string respondio, DateTime fecha, string comentario);
    }

    /// <summary>NestoAPI#773: a quién se avisa (campana de Nesto) y cómo llega la respuesta a la PDA (SignalR).</summary>
    public interface IAvisadorAutorizacionesReembolso
    {
        /// <summary>Campana de Nesto a Administración y Dirección. Nunca lanza.</summary>
        Task AvisarAdministracion(NotificacionPushDTO notificacion);
        /// <summary>«autorizacionRespondida» al grupo de la autorización. Nunca lanza.</summary>
        void Respondida(AutorizacionReembolsoDTO autorizacion);
    }

    public class AvisadorAutorizacionesReembolso : IAvisadorAutorizacionesReembolso
    {
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<string, List<string>> miembros;
        private readonly IAvisosCobroReparto avisos;

        public AvisadorAutorizacionesReembolso() : this(null, null, null)
        {
        }

        internal AvisadorAutorizacionesReembolso(IServicioNotificacionesPush notificaciones, Func<string, List<string>> miembros, IAvisosCobroReparto avisos)
        {
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.miembros = miembros ?? MiembrosGrupoDominio.Leer;
            this.avisos = avisos ?? new AvisosAlmacenSignalR();
        }

        /// <summary>Administración y Dirección, limpios, con dominio y sin repetidos.</summary>
        internal List<string> Destinatarios()
            => new[] { Constantes.GruposSeguridad.ADMINISTRACION, Constantes.GruposSeguridad.DIRECCION }
                .SelectMany(g => miembros(g) ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        public async Task AvisarAdministracion(NotificacionPushDTO notificacion)
        {
            try
            {
                foreach (string usuario in Destinatarios())
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorización de reembolso #773] No se ha podido avisar a Administración: {ex.Message}", ex));
            }
        }

        public void Respondida(AutorizacionReembolsoDTO autorizacion) => avisos.AutorizacionRespondida(autorizacion);
    }

    /// <summary>Los textos de la campana de Nesto y de la PDA (de usted).</summary>
    public static class TextosAutorizacionReembolso
    {
        public const string TIPO_NOTIFICACION = "AutorizacionReembolsoReparto";
        public const string TIPO_NOTIFICACION_TELEFONICA = "AutorizacionReembolsoTelefonica";
        public const int MAXIMO_TEXTO = 500;
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        public static string Euros(decimal importe) => importe.ToString("N2", Espanol) + " €";

        public static NotificacionPushDTO Solicitud(AutorizacionReembolsoDTO a) => new NotificacionPushDTO
        {
            Titulo = $"Autorización: entregar sin cobrar {Euros(a.Importe)} (envío {a.EnvioId})",
            Cuerpo = $"{a.Repartidor} pide entregar sin cobrar el reembolso de {Euros(a.Importe)} del envío {a.EnvioId}" +
                (a.Pedido.HasValue ? $" (pedido {a.Pedido.Value})" : string.Empty) +
                (string.IsNullOrWhiteSpace(a.Nombre) ? string.Empty : $", {a.Nombre}") +
                (string.IsNullOrWhiteSpace(a.Cliente) ? string.Empty : $", cliente {a.Cliente}") +
                $". Motivo: {a.Motivo}. Autorice o deniegue aquí mismo: el repartidor está esperando la respuesta.",
            Tipo = TIPO_NOTIFICACION,
            Datos = Datos(a, TIPO_NOTIFICACION)
        };

        public static NotificacionPushDTO Telefonica(AutorizacionReembolsoDTO a) => new NotificacionPushDTO
        {
            Titulo = $"Revisar: entregado sin cobrar {Euros(a.Importe)} con autorización por teléfono (envío {a.EnvioId})",
            Cuerpo = $"{a.Repartidor} ha entregado el envío {a.EnvioId}" + (a.Pedido.HasValue ? $" (pedido {a.Pedido.Value})" : string.Empty) +
                (string.IsNullOrWhiteSpace(a.Nombre) ? string.Empty : $", {a.Nombre}") +
                $" sin cobrar el reembolso de {Euros(a.Importe)}. Dice: «{a.Respondio}»" +
                (string.IsNullOrWhiteSpace(a.Motivo) ? string.Empty : $". Motivo: {a.Motivo}") + ". Revíselo, por favor.",
            Tipo = TIPO_NOTIFICACION_TELEFONICA,
            Datos = Datos(a, TIPO_NOTIFICACION_TELEFONICA)
        };

        private static Dictionary<string, string> Datos(AutorizacionReembolsoDTO a, string tipo) => new Dictionary<string, string>
        {
            ["tipo"] = tipo,
            ["autorizacionId"] = a.Id.ToString(CultureInfo.InvariantCulture),
            ["envio"] = a.EnvioId.ToString(CultureInfo.InvariantCulture),
            ["pedido"] = a.Pedido?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["empresa"] = Constantes.Empresas.EMPRESA_POR_DEFECTO,
            ["cliente"] = a.Cliente ?? string.Empty,
            ["importe"] = a.Importe.ToString("0.00", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// NestoAPI#773: pedir, leer y contestar las autorizaciones para entregar un reembolso sin cobrar. La comprobación al entregar está
    /// en <see cref="ComprobadorCobroEntrega"/>.
    /// </summary>
    public class ServicioAutorizacionesReembolso
    {
        public const string MENSAJE_FALTA_SCRIPT = "Falta lanzar el script Scripts/Issue773_CobroTarjetaReparto.sql (autorizaciones de reembolso). " +
            "Avise a Informática.";

        private readonly IRepositorioRepartoPropio reparto;
        private readonly IRepositorioAutorizacionesReembolso repositorio;
        private readonly IAvisadorAutorizacionesReembolso avisador;
        private readonly Func<DateTime> ahora;

        public ServicioAutorizacionesReembolso(IRepositorioRepartoPropio reparto, IRepositorioAutorizacionesReembolso repositorio,
            IAvisadorAutorizacionesReembolso avisador = null, Func<DateTime> ahora = null)
        {
            this.reparto = reparto ?? throw new ArgumentNullException(nameof(reparto));
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.avisador = avisador ?? new AvisadorAutorizacionesReembolso();
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<ResultadoReparto<AutorizacionReembolsoDTO>> Pedir(int envioId, PeticionAutorizacionReembolsoDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            string motivo = Limpio(peticion?.Motivo);
            if (motivo == null)
            {
                return Invalido("Escriba por qué no se cobra el reembolso (lo leerá Administración para decidir).");
            }
            if (motivo.Length > TextosAutorizacionReembolso.MAXIMO_TEXTO)
            {
                return Invalido($"El motivo no puede pasar de {TextosAutorizacionReembolso.MAXIMO_TEXTO} caracteres.");
            }
            EnviosAgencia envio = await reparto.LeerEnvio(envioId).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return ResultadoReparto<AutorizacionReembolsoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe el envío {envioId} del reparto propio.");
            }
            if (!(envio.Reembolso > 0))
            {
                return Invalido($"El envío {envio.Numero} no lleva reembolso: no hace falta autorización.");
            }
            if (envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return ResultadoReparto<AutorizacionReembolsoDTO>.Mal(TipoResultadoReparto.Conflicto, $"El envío {envio.Numero} ya está entregado.");
            }
            AutorizacionReembolsoReparto ultima = await repositorio.LeerUltimaDelEnvio(envio.Numero).ConfigureAwait(false);
            if (ultima != null && (ultima.Estado == EstadosAutorizacionReembolso.PENDIENTE || EstadosAutorizacionReembolso.PermiteEntregar(ultima.Estado)))
            {
                // Ya pedida (o ya autorizada): la misma, sin volver a avisar a nadie
                return ResultadoReparto<AutorizacionReembolsoDTO>.Bien(ADto(ultima, envio));
            }
            var nueva = new AutorizacionReembolsoReparto
            {
                EnvioId = envio.Numero,
                Importe = Math.Round(envio.Reembolso, 2, MidpointRounding.AwayFromZero),
                Repartidor = ServicioRepartoPropio.NombreRepartidor(usuario),
                FechaSolicitud = ahora(),
                Motivo = motivo,
                Estado = EstadosAutorizacionReembolso.PENDIENTE
            };
            nueva.Id = await repositorio.Crear(nueva).ConfigureAwait(false);
            AutorizacionReembolsoDTO dto = ADto(nueva, envio);
            await avisador.AvisarAdministracion(TextosAutorizacionReembolso.Solicitud(dto)).ConfigureAwait(false);
            return ResultadoReparto<AutorizacionReembolsoDTO>.Bien(dto);
        }

        public async Task<ResultadoReparto<AutorizacionReembolsoDTO>> Leer(int id)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            AutorizacionReembolsoReparto autorizacion = await repositorio.Leer(id).ConfigureAwait(false);
            if (autorizacion == null)
            {
                return ResultadoReparto<AutorizacionReembolsoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe la autorización {id}.");
            }
            return ResultadoReparto<AutorizacionReembolsoDTO>.Bien(ADto(autorizacion, await reparto.LeerEnvio(autorizacion.EnvioId).ConfigureAwait(false)));
        }

        /// <summary>Administración o Dirección (lo comprueba el controlador) autoriza o deniega. 409 si ya está contestada.</summary>
        public async Task<ResultadoReparto<AutorizacionReembolsoDTO>> Responder(int id, RespuestaAutorizacionReembolsoDTO respuesta, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            if (respuesta == null)
            {
                return Invalido("Diga si autoriza o deniega.");
            }
            string comentario = Limpio(respuesta.Comentario);
            if (comentario != null && comentario.Length > TextosAutorizacionReembolso.MAXIMO_TEXTO)
            {
                return Invalido($"El comentario no puede pasar de {TextosAutorizacionReembolso.MAXIMO_TEXTO} caracteres.");
            }
            AutorizacionReembolsoReparto autorizacion = await repositorio.Leer(id).ConfigureAwait(false);
            if (autorizacion == null)
            {
                return ResultadoReparto<AutorizacionReembolsoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe la autorización {id}.");
            }
            if (autorizacion.Estado != EstadosAutorizacionReembolso.PENDIENTE)
            {
                return YaContestada(autorizacion);
            }
            string estado = respuesta.Autorizar ? EstadosAutorizacionReembolso.AUTORIZADA : EstadosAutorizacionReembolso.DENEGADA;
            string quien = ServicioRepartoPropio.NombreRepartidor(usuario);
            DateTime fecha = ahora();
            if (!await repositorio.Responder(id, EstadosAutorizacionReembolso.PENDIENTE, estado, quien, fecha, comentario).ConfigureAwait(false))
            {
                return YaContestada(await repositorio.Leer(id).ConfigureAwait(false) ?? autorizacion);
            }
            autorizacion.Estado = estado;
            autorizacion.Respondio = quien;
            autorizacion.FechaRespuesta = fecha;
            autorizacion.Comentario = comentario;
            AutorizacionReembolsoDTO dto = ADto(autorizacion, await reparto.LeerEnvio(autorizacion.EnvioId).ConfigureAwait(false));
            avisador.Respondida(dto);
            return ResultadoReparto<AutorizacionReembolsoDTO>.Bien(dto);
        }

        internal static AutorizacionReembolsoDTO ADto(AutorizacionReembolsoReparto a, EnviosAgencia envio) => new AutorizacionReembolsoDTO
        {
            Id = a.Id,
            EnvioId = a.EnvioId,
            Pedido = envio?.Pedido,
            Cliente = envio?.Cliente?.Trim(),
            Nombre = envio?.Nombre?.Trim(),
            Importe = a.Importe,
            Repartidor = a.Repartidor?.Trim(),
            FechaSolicitud = a.FechaSolicitud,
            Motivo = a.Motivo?.Trim(),
            Estado = a.Estado?.Trim(),
            Respondio = a.Respondio?.Trim(),
            FechaRespuesta = a.FechaRespuesta,
            Comentario = a.Comentario?.Trim(),
            GrupoAvisos = AvisosAlmacen.GrupoAutorizacion(a.Id)
        };

        private static ResultadoReparto<AutorizacionReembolsoDTO> YaContestada(AutorizacionReembolsoReparto a)
            => ResultadoReparto<AutorizacionReembolsoDTO>.Mal(TipoResultadoReparto.Conflicto,
                a.Estado == EstadosAutorizacionReembolso.TELEFONICA
                    ? $"Esta autorización ya no está pendiente: el repartidor la dio por autorizada por teléfono («{a.Respondio?.Trim()}»)."
                    : $"Esta autorización ya está contestada ({(a.Estado == EstadosAutorizacionReembolso.AUTORIZADA ? "autorizada" : "denegada")} por {a.Respondio?.Trim()}).");

        private static string Limpio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        private static ResultadoReparto<AutorizacionReembolsoDTO> Invalido(string mensaje)
            => ResultadoReparto<AutorizacionReembolsoDTO>.Mal(TipoResultadoReparto.Invalido, mensaje);
    }

    public class RepositorioAutorizacionesReembolso : IRepositorioAutorizacionesReembolso
    {
        public const string TABLA = "RepartoAutorizacionesReembolso";
        private const string COLUMNAS = "Id, EnvioId, Importe, Repartidor, FechaSolicitud, Motivo, Estado, Respondio, FechaRespuesta, Comentario";

        private readonly NVEntities db;

        public RepositorioAutorizacionesReembolso(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public async Task<AutorizacionReembolsoReparto> Leer(int id)
            => (await db.Database.SqlQuery<AutorizacionReembolsoReparto>(
                $"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE Id = @p0", id).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<AutorizacionReembolsoReparto> LeerUltimaDelEnvio(int envioId)
            => (await db.Database.SqlQuery<AutorizacionReembolsoReparto>(
                $"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE EnvioId = @p0 ORDER BY Id DESC", envioId).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<int> Crear(AutorizacionReembolsoReparto a)
            => (await db.Database.SqlQuery<int>(
                $"INSERT INTO dbo.{TABLA} (EnvioId, Importe, Repartidor, FechaSolicitud, Motivo, Estado, Respondio, FechaRespuesta, Comentario) " +
                "OUTPUT INSERTED.Id VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8)",
                a.EnvioId, a.Importe, Recortar(a.Repartidor, 50) ?? "NestoAPI", a.FechaSolicitud, (object)Recortar(a.Motivo, 500) ?? DBNull.Value,
                a.Estado, (object)Recortar(a.Respondio, 100) ?? DBNull.Value, (object)a.FechaRespuesta ?? DBNull.Value,
                (object)Recortar(a.Comentario, 500) ?? DBNull.Value).ToListAsync().ConfigureAwait(false)).Single();

        public async Task<bool> Responder(int id, string estadoEsperado, string estado, string respondio, DateTime fecha, string comentario)
            => await db.Database.ExecuteSqlCommandAsync(
                $"UPDATE dbo.{TABLA} SET Estado = @p2, Respondio = @p3, FechaRespuesta = @p4, Comentario = @p5 WHERE Id = @p0 AND Estado = @p1",
                id, estadoEsperado, estado, Recortar(respondio, 100) ?? "NestoAPI", fecha, (object)Recortar(comentario, 500) ?? DBNull.Value)
                .ConfigureAwait(false) == 1;

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    /// <summary>
    /// NestoAPI#773: lo que se comprueba del cobro al ENTREGAR un envío con reembolso (lo llama ServicioRepartoPropio.EntregarEnvio).
    /// <list type="bullet">
    /// <item>«Tarjeta» solo con el PagoTPVId de un enlace del reparto de ESE envío que esté pagado; el importe cobrado es el pagado. No se
    /// contabiliza nada al entregar: ya lo ha apuntado ServicioPagos al pagarse.</item>
    /// <item>Con el enlace del envío pagado, ni «Efectivo» ni «No cobrado» (sería cobrar dos veces o perder el pago de vista).</item>
    /// <item>Sin cobrar («NoCobrado», o el «Enlace» antiguo, que tampoco está cobrado): hace falta la última autorización del envío
    /// Autorizada o Telefonica, o que la entrega traiga <see cref="EntregaRepartoDTO.AutorizacionTelefonica"/> (se registra al entregar y se
    /// avisa a Administración para revisarla).</item>
    /// </list>
    /// Sin el script de #773, todo como antes (sin tarjeta y sin exigir autorización).
    /// </summary>
    public class ComprobadorCobroEntrega
    {
        public const string MENSAJE_TARJETA_SIN_ENLACE = "Para cobrar con tarjeta use «Cobrar con tarjeta» (manda el enlace de pago al cliente por " +
            "WhatsApp o por correo). Si su Ariadna no lo tiene, actualícela.";

        private readonly IRepositorioCobrosTarjetaReparto tarjetas;
        private readonly IRepositorioAutorizacionesReembolso autorizaciones;
        private readonly IAvisadorAutorizacionesReembolso avisador;
        private readonly Func<DateTime> ahora;

        public ComprobadorCobroEntrega(IRepositorioCobrosTarjetaReparto tarjetas, IRepositorioAutorizacionesReembolso autorizaciones,
            IAvisadorAutorizacionesReembolso avisador = null, Func<DateTime> ahora = null)
        {
            this.tarjetas = tarjetas ?? throw new ArgumentNullException(nameof(tarjetas));
            this.autorizaciones = autorizaciones ?? throw new ArgumentNullException(nameof(autorizaciones));
            this.avisador = avisador ?? new AvisadorAutorizacionesReembolso();
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>¿Hay tarjeta (script de #773)? Sin él, «Tarjeta» sigue dando 400 como antes.</summary>
        public bool AdmiteTarjeta() => tarjetas.ScriptAplicado();

        /// <summary>¿Está todo el script de #773 (las dos tablas y, con ellas, las CHECK de cobro y de motivo ampliadas)?</summary>
        public bool ScriptAplicado() => tarjetas.ScriptAplicado() && autorizaciones.ScriptAplicado();

        /// <param name="cobro">El cobro ya normalizado (con reembolso y sin decir nada, «NoCobrado»).</param>
        public async Task<ComprobacionCobroEntrega> Comprobar(EnviosAgencia envio, string cobro, EntregaRepartoDTO peticion, string usuario)
        {
            decimal reembolso = envio.Reembolso > 0 ? envio.Reembolso : 0m;
            if (reembolso <= 0 || cobro == null)
            {
                return new ComprobacionCobroEntrega();
            }
            bool conTarjetas = tarjetas.ScriptAplicado();
            EstadoEnlaceDelEnvio enlace = conTarjetas ? await EnlaceDelEnvio(envio.Numero).ConfigureAwait(false) : null;

            if (cobro == PruebaEntregaReparto.COBRO_TARJETA)
            {
                if (!conTarjetas || !peticion.PagoTPVId.HasValue)
                {
                    return ComprobacionCobroEntrega.Fallo(MENSAJE_TARJETA_SIN_ENLACE);
                }
                EnlaceTarjetaReparto suyo = await tarjetas.LeerPorPago(peticion.PagoTPVId.Value).ConfigureAwait(false);
                if (suyo == null || suyo.EnvioId != envio.Numero || suyo.Tipo != TiposCobroTarjetaReparto.REEMBOLSO)
                {
                    return ComprobacionCobroEntrega.Fallo($"El enlace de pago {peticion.PagoTPVId.Value} no es el del envío {envio.Numero}.");
                }
                List<PagoCadenaReparto> cadena = await tarjetas.LeerCadena(suyo.PagoTPVId).ConfigureAwait(false);
                if (ServicioCobroTarjetaReparto.EstadoDeLaCadena(cadena) != EstadosEnlaceTarjeta.PAGADO)
                {
                    return ComprobacionCobroEntrega.Fallo("El cliente todavía no ha pagado el enlace con tarjeta: espere a que salga «PAGADO» " +
                        "o cobre en efectivo.");
                }
                return new ComprobacionCobroEntrega
                {
                    ImporteCobrado = ServicioCobroTarjetaReparto.ImporteDeLaCadena(cadena) ?? suyo.Importe,
                    PagoTPVId = suyo.PagoTPVId,
                    UrlPago = cadena.Where(p => p.Id == suyo.PagoTPVId).Select(p => Pagos.ServicioPagos.UrlPaginaPago(p.TokenAcceso)).FirstOrDefault()
                };
            }

            if (enlace != null && enlace.Pagado)
            {
                // El cliente ya ha pagado con tarjeta: no se puede cobrar otra vez ni dejarlo «sin cobrar»
                return ComprobacionCobroEntrega.Fallo($"Este envío ya está pagado con tarjeta ({TextosAutorizacionReembolso.Euros(enlace.Importe)}): " +
                    "entréguelo como «Tarjeta (enlace pagado)», sin cobrar nada más.");
            }
            if (cobro == PruebaEntregaReparto.COBRO_EFECTIVO)
            {
                return new ComprobacionCobroEntrega();
            }

            // Sin cobrar (NoCobrado o el Enlace de antes): hace falta autorización (sin el script de #773, como antes)
            if (!autorizaciones.ScriptAplicado())
            {
                return new ComprobacionCobroEntrega();
            }
            string telefonica = Limpio(peticion.AutorizacionTelefonica);
            if (telefonica != null && telefonica.Length > TextosAutorizacionReembolso.MAXIMO_TEXTO)
            {
                return ComprobacionCobroEntrega.Fallo($"La autorización por teléfono no puede pasar de {TextosAutorizacionReembolso.MAXIMO_TEXTO} caracteres.");
            }
            AutorizacionReembolsoReparto ultima = await autorizaciones.LeerUltimaDelEnvio(envio.Numero).ConfigureAwait(false);
            if (ultima != null && EstadosAutorizacionReembolso.PermiteEntregar(ultima.Estado))
            {
                return new ComprobacionCobroEntrega { Autorizacion = ultima };
            }
            if (telefonica != null)
            {
                return new ComprobacionCobroEntrega { Autorizacion = ultima, Telefonica = telefonica };
            }
            string importe = TextosAutorizacionReembolso.Euros(reembolso);
            if (ultima?.Estado == EstadosAutorizacionReembolso.PENDIENTE)
            {
                return ComprobacionCobroEntrega.Fallo($"La autorización para no cobrar {importe} está pendiente de Administración: espere la respuesta " +
                    "o, si se la dan por teléfono, escriba quién.");
            }
            if (ultima?.Estado == EstadosAutorizacionReembolso.DENEGADA)
            {
                return ComprobacionCobroEntrega.Fallo($"{ultima.Respondio?.Trim() ?? "Administración"} ha denegado entregar sin cobrar: cobre el reembolso " +
                    $"de {importe} o apunte la incidencia «Reembolso no cobrado».");
            }
            return ComprobacionCobroEntrega.Fallo($"Esta entrega lleva un reembolso de {importe} sin cobrar: cóbrelo (efectivo o tarjeta) o pida " +
                "autorización a Administración. Si su Ariadna no tiene «Pedir autorización», actualícela.");
        }

        /// <summary>
        /// Después de registrar la entrega: la autorización por teléfono queda apuntada (la pendiente pasa a Telefonica, o una nueva) y se
        /// avisa a Administración para revisarla. Nunca lanza (la entrega ya está hecha).
        /// </summary>
        public async Task Confirmar(ComprobacionCobroEntrega comprobacion, EnviosAgencia envio, string usuario)
        {
            if (comprobacion?.Telefonica == null)
            {
                return;
            }
            try
            {
                string repartidor = ServicioRepartoPropio.NombreRepartidor(usuario);
                DateTime momento = ahora();
                AutorizacionReembolsoReparto fila = comprobacion.Autorizacion;
                if (fila != null && fila.Estado == EstadosAutorizacionReembolso.PENDIENTE
                    && await autorizaciones.Responder(fila.Id, EstadosAutorizacionReembolso.PENDIENTE, EstadosAutorizacionReembolso.TELEFONICA,
                        comprobacion.Telefonica, momento, null).ConfigureAwait(false))
                {
                    fila.Estado = EstadosAutorizacionReembolso.TELEFONICA;
                    fila.Respondio = comprobacion.Telefonica;
                    fila.FechaRespuesta = momento;
                }
                else
                {
                    fila = new AutorizacionReembolsoReparto
                    {
                        EnvioId = envio.Numero,
                        Importe = Math.Round(envio.Reembolso, 2, MidpointRounding.AwayFromZero),
                        Repartidor = repartidor,
                        FechaSolicitud = momento,
                        Motivo = null,
                        Estado = EstadosAutorizacionReembolso.TELEFONICA,
                        Respondio = comprobacion.Telefonica,
                        FechaRespuesta = momento
                    };
                    fila.Id = await autorizaciones.Crear(fila).ConfigureAwait(false);
                }
                AutorizacionReembolsoDTO dto = ServicioAutorizacionesReembolso.ADto(fila, envio);
                await avisador.AvisarAdministracion(TextosAutorizacionReembolso.Telefonica(dto)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorización de reembolso #773] Entregado el envío {envio?.Numero} con autorización por teléfono, " +
                    $"pero no se ha podido apuntar ni avisar: {ex.Message}", ex));
            }
        }

        /// <summary>El último enlace del reparto del envío y si está pagado; null si no tiene.</summary>
        private async Task<EstadoEnlaceDelEnvio> EnlaceDelEnvio(int envioId)
        {
            EnlaceTarjetaReparto enlace = await tarjetas.LeerUltimoDelEnvio(envioId).ConfigureAwait(false);
            if (enlace == null)
            {
                return null;
            }
            List<PagoCadenaReparto> cadena = await tarjetas.LeerCadena(enlace.PagoTPVId).ConfigureAwait(false);
            return new EstadoEnlaceDelEnvio
            {
                Pagado = ServicioCobroTarjetaReparto.EstadoDeLaCadena(cadena) == EstadosEnlaceTarjeta.PAGADO,
                Importe = ServicioCobroTarjetaReparto.ImporteDeLaCadena(cadena) ?? enlace.Importe
            };
        }

        private sealed class EstadoEnlaceDelEnvio
        {
            public bool Pagado { get; set; }
            public decimal Importe { get; set; }
        }

        private static string Limpio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    /// <summary>Lo que dice <see cref="ComprobadorCobroEntrega.Comprobar"/>: un error (400) o con qué se entrega.</summary>
    public class ComprobacionCobroEntrega
    {
        public string Error { get; set; }
        /// <summary>Con «Tarjeta»: lo pagado (es lo cobrado).</summary>
        public decimal? ImporteCobrado { get; set; }
        public int? PagoTPVId { get; set; }
        public string UrlPago { get; set; }
        /// <summary>La autorización con la que se entrega sin cobrar (o la pendiente que pasará a Telefonica).</summary>
        public AutorizacionReembolsoReparto Autorizacion { get; set; }
        /// <summary>«Autoriza Pepe por teléfono»: se apunta al entregar.</summary>
        public string Telefonica { get; set; }

        public static ComprobacionCobroEntrega Fallo(string error) => new ComprobacionCobroEntrega { Error = error };
    }
}
