using System;
using System.Collections.Concurrent;
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
    /// <summary>Qué hacer con una entrega según la carga de su parada (<see cref="ReglaCargaObligatoria.Decidir"/>).</summary>
    public enum DecisionCargaEntrega
    {
        /// <summary>La lleva quien entrega, el interruptor está apagado o la parada no se carga: la entrega sigue como hoy.</summary>
        Seguir,
        /// <summary>No la lleva quien entrega, pero ha leído algún bulto suyo al entregar: esa lectura cuenta como cargarla.</summary>
        CargarAlEntregar,
        /// <summary>No la lleva quien entrega y no ha leído nada de ella: 409 <see cref="ReglaCargaObligatoria.CODIGO_NO_CARGADA"/>.</summary>
        Bloquear
    }

    /// <summary>
    /// Carlos 20/11/26 (punto 6): una parada solo es de un repartidor cuando ha leído alguno de sus bultos (o el papel NE/NR de una nota,
    /// que solo se le da a él; las notas de cobro nacen asignadas). LAS REGLAS, puras:
    /// <list type="bullet">
    /// <item>«Mis paradas» para entregar (soloMios + soloCargados): solo las suyas (<see cref="NucleoParadasReparto.Visible(string, string, bool, bool)"/>).</item>
    /// <item>Terminar de cargar: las paradas del día que no ha cargado NADIE (<see cref="SinCargarPorNadie"/>), con un aviso a Almacén como
    /// mucho cada <see cref="MINUTOS_ENTRE_AVISOS"/> minutos por el mismo conjunto (<see cref="TocaAvisar"/>).</item>
    /// <item>Con <see cref="PARAMETRO_CARGA_OBLIGATORIA"/> = «1», no se entrega lo que no se ha cargado (<see cref="Decidir"/>).</item>
    /// </list>
    /// </summary>
    public static class ReglaCargaObligatoria
    {
        /// <summary>ParametrosUsuario empresa 1, «(defecto)»: SOLO «1» = encendido; sin fila, apagado.</summary>
        public const string PARAMETRO_CARGA_OBLIGATORIA = "Reparto.CargaObligatoria";
        public const string CODIGO_NO_CARGADA = "NO_CARGADA";
        public const int MINUTOS_ENTRE_AVISOS = 30;
        /// <summary>El tipo de la clave derivada de la carga apuntada al entregar (una por bulto leído; un reintento no la duplica).</summary>
        internal const string TIPO_CLAVE_CARGA_AL_ENTREGAR = "CargaAlEntregar";

        public static bool Encendido(string valor) => string.Equals(valor?.Trim(), "1", StringComparison.Ordinal);

        /// <param name="encendido">El interruptor <see cref="PARAMETRO_CARGA_OBLIGATORIA"/>.</param>
        /// <param name="seCarga">False en las notas de cobro y las paradas de reposición (no les afecta).</param>
        /// <param name="repartidor">Quien lleva la parada (la última carga vigente); null si nadie.</param>
        /// <param name="usuario">Quien entrega.</param>
        /// <param name="leidoAlgunoDeLaParada">Si en BultosEntregados viene algún bulto (o papel) de ESA parada.</param>
        public static DecisionCargaEntrega Decidir(bool encendido, bool seCarga, string repartidor, string usuario, bool leidoAlgunoDeLaParada)
        {
            if (!encendido || !seCarga || ServicioRepartoPropio.MismoUsuario(repartidor, usuario))
            {
                return DecisionCargaEntrega.Seguir;
            }
            return leidoAlgunoDeLaParada ? DecisionCargaEntrega.CargarAlEntregar : DecisionCargaEntrega.Bloquear;
        }

        /// <summary>El 409 de usted: de nadie o de otro repartidor.</summary>
        public static string MensajeNoCargada(string repartidor)
            => string.IsNullOrWhiteSpace(repartidor)
                ? "Esta parada no la ha cargado nadie: lea alguno de sus bultos (o el papel de la nota) para entregarla."
                : $"Esta parada la lleva {ServicioRepartoPropio.NombreRepartidor(repartidor)}, no usted: lea alguno de sus bultos (o el papel de la nota) para entregarla usted.";

        /// <summary>De la vista «Sin cargar», las paradas que no ha cargado nadie (las que tienen repartidor, aunque les falte un bulto, no).</summary>
        public static List<ParadaSinCargarDTO> SinCargarPorNadie(IEnumerable<EnvioSinCargarDTO> sinCargar)
            => (sinCargar ?? Enumerable.Empty<EnvioSinCargarDTO>())
                .Where(e => e != null && string.IsNullOrWhiteSpace(e.Repartidor))
                .Select(e => new ParadaSinCargarDTO
                {
                    EnvioId = e.EnvioId,
                    CodigoDocumento = e.CodigoDocumento,
                    Nombre = e.Nombre,
                    Poblacion = e.Poblacion,
                    Bultos = e.Bultos,
                    BultosSinCargar = Math.Max(e.Bultos - e.BultosCargados, 0)
                })
                .ToList();

        /// <summary>Para la PDA: null si no queda ninguna.</summary>
        public static string Mensaje(int paradas)
            => paradas <= 0 ? null
                : paradas == 1 ? "Queda 1 parada sin cargar por nadie: no sabemos quién la lleva."
                : $"Quedan {paradas} paradas sin cargar por nadie: no sabemos quién las lleva.";

        /// <summary>«RP251300 PELUQUERÍA LA BONITA (ALGETE)» o «NE930001 …».</summary>
        public static string Describir(ParadaSinCargarDTO parada)
        {
            string codigo = string.IsNullOrWhiteSpace(parada.CodigoDocumento) ? "RP" + parada.EnvioId.ToString(CultureInfo.InvariantCulture) : parada.CodigoDocumento.Trim();
            string destino = ServicioRepartoPropio.Descripcion(parada.Nombre, parada.Poblacion);
            return string.IsNullOrWhiteSpace(destino) ? codigo : $"{codigo} {destino}";
        }

        /// <summary>«Andre ha terminado de cargar y quedan 2 paradas sin cargar por nadie: RP251300 …, NE930001 ….»</summary>
        public static NotificacionPushDTO Notificacion(string repartidor, IReadOnlyList<ParadaSinCargarDTO> paradas)
        {
            string nombre = ServicioRepartoPropio.NombreRepartidor(repartidor) ?? "Un repartidor";
            string cuantas = paradas.Count == 1 ? "queda 1 parada sin cargar por nadie" : $"quedan {paradas.Count} paradas sin cargar por nadie";
            return new NotificacionPushDTO
            {
                Titulo = $"Paradas sin cargar: {nombre}",
                Cuerpo = $"{nombre} ha terminado de cargar y {cuantas}: {NucleoFurgoneta.ListaCorta(paradas.Select(Describir))}. Compruebe quién las lleva.",
                Tipo = AvisosRepartoSinCargar.TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = AvisosRepartoSinCargar.TIPO_NOTIFICACION,
                    ["repartidor"] = nombre,
                    ["paradas"] = paradas.Count.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        /// <summary>El mismo conjunto de paradas del mismo día da la misma clave (sin importar el orden ni quién termina).</summary>
        public static string ClaveAviso(DateTime dia, IEnumerable<int> envios)
            => dia.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ":" +
               string.Join(",", (envios ?? Enumerable.Empty<int>()).Distinct().OrderBy(e => e).Select(e => e.ToString(CultureInfo.InvariantCulture)));

        public static bool TocaAvisar(DateTime? ultimoAviso, DateTime ahora)
            => !ultimoAviso.HasValue || ahora - ultimoAviso.Value >= TimeSpan.FromMinutes(MINUTOS_ENTRE_AVISOS);
    }

    /// <summary>Cuándo se avisó por última vez de cada conjunto de paradas sin cargar. En memoria (si se reinicia, como mucho un aviso de más).</summary>
    public class MemoriaAvisosSinCargar
    {
        /// <summary>La de toda la aplicación.</summary>
        public static readonly MemoriaAvisosSinCargar Compartida = new MemoriaAvisosSinCargar();

        private readonly ConcurrentDictionary<string, DateTime> avisos = new ConcurrentDictionary<string, DateTime>();

        /// <summary>True (y lo apunta) si toca avisar de ese conjunto ahora.</summary>
        public bool Reservar(string clave, DateTime ahora)
        {
            lock (avisos)
            {
                if (avisos.TryGetValue(clave, out DateTime ultimo) && !ReglaCargaObligatoria.TocaAvisar(ultimo, ahora))
                {
                    return false;
                }
                avisos[clave] = ahora;
                // Lo de días anteriores ya no sirve.
                foreach (string vieja in avisos.Where(a => ahora - a.Value > TimeSpan.FromDays(1)).Select(a => a.Key).ToList())
                {
                    _ = avisos.TryRemove(vieja, out _);
                }
                return true;
            }
        }
    }

    /// <summary>La campana de Nesto a Almacén cuando un repartidor termina de cargar y quedan paradas que no ha cargado nadie.</summary>
    public interface IAvisosRepartoSinCargar
    {
        /// <returns>A quién se ha avisado («Almacén»); vacía si a nadie.</returns>
        Task<List<string>> Avisar(string repartidor, IReadOnlyList<ParadaSinCargarDTO> paradas);
    }

    /// <summary>Campana (buzón + SignalR, tipo <see cref="TIPO_NOTIFICACION"/>) a los miembros de Almacén, como <see cref="AvisosFurgonetaSinDescargar"/>.</summary>
    public class AvisosRepartoSinCargar : IAvisosRepartoSinCargar
    {
        public const string TIPO_NOTIFICACION = "RepartoSinCargar";

        private readonly Func<IServicioNotificacionesPush> notificaciones;
        private readonly Func<string, List<string>> miembros;

        public AvisosRepartoSinCargar() : this(() => new ServicioNotificacionesPush(), MiembrosGrupoDominio.Leer)
        {
        }

        internal AvisosRepartoSinCargar(Func<IServicioNotificacionesPush> notificaciones, Func<string, List<string>> miembros)
        {
            this.notificaciones = notificaciones;
            this.miembros = miembros;
        }

        public async Task<List<string>> Avisar(string repartidor, IReadOnlyList<ParadaSinCargarDTO> paradas)
        {
            var avisados = new List<string>();
            if (paradas == null || paradas.Count == 0)
            {
                return avisados;
            }
            List<string> deAlmacen;
            try
            {
                deAlmacen = miembros(Constantes.GruposSeguridad.ALMACEN) ?? new List<string>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se pudieron leer los miembros de {Constantes.GruposSeguridad.ALMACEN}: {ex.Message}", ex));
                return avisados;
            }
            if (deAlmacen.Count == 0)
            {
                return avisados;
            }
            avisados.Add(Constantes.GruposSeguridad.ALMACEN);
            NotificacionPushDTO notificacion = ReglaCargaObligatoria.Notificacion(repartidor, paradas);
            IServicioNotificacionesPush servicio = notificaciones();
            foreach (string usuario in new HashSet<string>(deAlmacen.Select(AvisosFurgonetaSinDescargar.ConDominio), StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reparto] No se pudo avisar a {usuario} de las paradas sin cargar: {ex.Message}", ex));
                }
            }
            return avisados;
        }
    }

    /// <summary>Punto 6 (20/11/26): terminar de cargar y la carga obligatoria al entregar.</summary>
    public partial class ServicioRepartoPropio
    {
        /// <summary>Tests: el interruptor <see cref="ReglaCargaObligatoria.PARAMETRO_CARGA_OBLIGATORIA"/> sin leer ParametrosUsuario. Null: se lee.</summary>
        public Func<bool> CargaObligatoriaEncendida { get; set; }

        private bool CargaObligatoria()
        {
            if (CargaObligatoriaEncendida != null)
            {
                return CargaObligatoriaEncendida();
            }
            try
            {
                return ReglaCargaObligatoria.Encendido(parametros.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, ReglaCargaObligatoria.PARAMETRO_CARGA_OBLIGATORIA));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// POST api/RepartoPropio/Carga/Terminar: las paradas del día que no ha cargado nadie (la vista «Sin cargar» sin las que ya llevan
        /// repartidor) y, si hay alguna, la campana a Almacén (como mucho una vez cada 30 minutos por el mismo conjunto). No guarda nada.
        /// </summary>
        public async Task<ResultadoReparto<ResultadoTerminarCargaDTO>> TerminarCarga(TerminarCargaRepartoDTO peticion, string usuario)
        {
            DateTime dia = (peticion?.Fecha ?? ahora()).Date;
            ResultadoReparto<List<EnvioSinCargarDTO>> sinCargar = await SinCargar(dia).ConfigureAwait(false);
            if (sinCargar.Tipo != TipoResultadoReparto.Ok)
            {
                return ResultadoReparto<ResultadoTerminarCargaDTO>.Mal(sinCargar.Tipo, sinCargar.Mensaje);
            }
            List<ParadaSinCargarDTO> deNadie = ReglaCargaObligatoria.SinCargarPorNadie(sinCargar.Contenido);
            if (deNadie.Count > 0 && paradas.AvisosSinCargar != null)
            {
                try
                {
                    MemoriaAvisosSinCargar memoria = paradas.MemoriaSinCargar ?? MemoriaAvisosSinCargar.Compartida;
                    if (memoria.Reservar(ReglaCargaObligatoria.ClaveAviso(dia, deNadie.Select(p => p.EnvioId)), ahora()))
                    {
                        _ = await paradas.AvisosSinCargar.Avisar(usuario, deNadie).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reparto] No se pudo avisar a Almacén de las paradas sin cargar al terminar la carga de {NombreRepartidor(usuario)}: {ex.Message}", ex));
                }
            }
            return ResultadoReparto<ResultadoTerminarCargaDTO>.Bien(new ResultadoTerminarCargaDTO
            {
                SinCargar = deNadie,
                Total = deNadie.Count,
                Mensaje = ReglaCargaObligatoria.Mensaje(deNadie.Count)
            });
        }

        /// <summary>
        /// Con <see cref="ReglaCargaObligatoria.PARAMETRO_CARGA_OBLIGATORIA"/> encendido, antes de entregar un envío o una nota de entrega o de
        /// recogida: si no la lleva quien entrega, lo leído de esa parada en BultosEntregados se carga a su nombre (como «Cargar» con
        /// Reasignar: mismo rastro) y sigue; sin nada leído, 409 NO_CARGADA. Null: se puede seguir. Ni notas de cobro ni reposiciones;
        /// un reintento de una entrega ya registrada, tampoco.
        /// </summary>
        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> ComprobarCargaObligatoria(int envioId, EntregaRepartoDTO peticion, string usuario)
        {
            if (envioId <= 0 || peticion == null || peticion.ClaveIdempotencia == Guid.Empty || string.IsNullOrWhiteSpace(usuario)
                || !CargaDisponible() || !repositorio.ScriptAplicado() || !CargaObligatoria())
            {
                return null;
            }
            if (await repositorio.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false) != null)
            {
                return null; // el reintento: misma respuesta que la primera vez
            }
            EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                || (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO))
            {
                return null; // el error lo da la entrega
            }
            NotaParadaReparto nota = await NotaDe(envio.Pedido).ConfigureAwait(false);
            string repartidor = Repartidor(await SinLasDescargadas(await carga.LeerCargas(new[] { envioId }).ConfigureAwait(false)).ConfigureAwait(false));
            var leidos = new List<string>();
            foreach (string leido in (peticion.BultosEntregados ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToUpperInvariant()).Distinct())
            {
                CodigoParadaResuelto resuelto = await ResolverCodigo(leido).ConfigureAwait(false);
                if (!resuelto.Error.HasValue && resuelto.Envio == envioId)
                {
                    leidos.Add(leido);
                }
            }
            switch (ReglaCargaObligatoria.Decidir(true, nota?.EsCobro != true, repartidor, usuario, leidos.Count > 0))
            {
                case DecisionCargaEntrega.Seguir:
                    return null;
                case DecisionCargaEntrega.Bloquear:
                    return new ResultadoReparto<EstadoEnvioRepartoDTO>
                    {
                        Tipo = TipoResultadoReparto.Conflicto,
                        Mensaje = ReglaCargaObligatoria.MensajeNoCargada(repartidor),
                        Codigo = ReglaCargaObligatoria.CODIGO_NO_CARGADA,
                        Detalles = new { repartidor = NombreRepartidor(repartidor) }
                    };
            }
            foreach (string leido in leidos)
            {
                var cargaDto = new CargaBultoRepartoDTO
                {
                    Codigo = leido,
                    ClaveIdempotencia = NucleoFurgoneta.ClaveDerivada(peticion.ClaveIdempotencia, leido, ReglaCargaObligatoria.TIPO_CLAVE_CARGA_AL_ENTREGAR),
                    FechaHora = peticion.FechaHora,
                    Latitud = peticion.Latitud,
                    Longitud = peticion.Longitud,
                    Reasignar = true
                };
                ResultadoReparto<ResultadoCargaBultoDTO> cargado = await CargarEnvio(cargaDto, usuario).ConfigureAwait(false);
                if (cargado.Tipo != TipoResultadoReparto.Ok)
                {
                    return new ResultadoReparto<EstadoEnvioRepartoDTO> { Tipo = cargado.Tipo, Mensaje = cargado.Mensaje, Codigo = cargado.Codigo, Detalles = cargado.Detalles };
                }
                await AnotarCarga(cargaDto, cargado.Contenido, usuario).ConfigureAwait(false);
            }
            return null;
        }
    }
}
