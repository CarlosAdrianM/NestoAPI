using Hangfire;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#906: POST api/Reposiciones/Diferencias/{id}/Discusion.</summary>
    public class AbrirDiscusionDiferenciaDTO
    {
        /// <summary>Lo que ha comprobado el origen («la saqué del hueco 005/005/004 y la metí en el bulto 2»). Obligatorio.</summary>
        public string Comentario { get; set; }
    }

    /// <summary>NestoAPI#906: POST api/Reposiciones/Diferencias/{id}/Discusion/Respuesta.</summary>
    public class ResponderDiscusionDiferenciaDTO
    {
        /// <summary>«LoHeEncontrado» (= Error al recibir) o «NoEsta» (los dos mantienen su versión: decide la regla).</summary>
        public string Respuesta { get; set; }
        public string Comentario { get; set; }
    }

    public class ResultadoDiscusionDiferenciaDTO
    {
        public DiscusionDiferenciaDTO Discusion { get; set; }
        public DiferenciaReposicionDTO Diferencia { get; set; }
        /// <summary>Qué ha pasado (con el stock, y a quién se ha imputado), para enseñárselo a quien contesta.</summary>
        public string Mensaje { get; set; }
        /// <summary>Si ha decidido la regla: una por unidad.</summary>
        public List<ImputacionUnidadDesacuerdo> Imputaciones { get; set; } = new List<ImputacionUnidadDesacuerdo>();
    }

    /// <summary>NestoAPI#906: a quién se avisa (campana de Nesto y buzón de Ariadna). Nunca debe romper nada.</summary>
    public interface IAvisadorDiscusionesDiferencias
    {
        Task Avisar(string empresa, IEnumerable<string> almacenes, bool direccion, string titulo, string cuerpo, int diferenciaId);
    }

    /// <summary>
    /// NestoAPI#906: «En discusión». La abre el ORIGEN de una falta pendiente (mismo permiso que resolverla) con lo que ha comprobado; avisa
    /// al destino, que contesta «Lo he encontrado» (se resuelve como Error al recibir: error RECONOCIDO del destino) o «No está»; si no
    /// contesta en el plazo (ParametrosUsuario «(defecto)» <see cref="CLAVE_PLAZO"/>, 48 h por defecto; recordatorio 24 h antes) o contesta
    /// «No está», decide la regla (<see cref="ReglaImputacionDesacuerdo"/>), unidad a unidad, la baja va por el cauce de las diferencias
    /// en el almacén imputado (<see cref="PlanStockDesacuerdo"/>) y se avisa a los dos. Si el origen resuelve como siempre mientras tanto
    /// («Confirmada» / «No estaba»), la discusión queda Acordada con error reconocido del origen. Al abrir se guardan los RASGOS del momento
    /// (una fila por unidad) para entrenar algún día un modelo. Sin el script, 503 y todo lo demás como antes.
    /// </summary>
    public class ServicioDiscusionesDiferencias
    {
        internal const string CLAVE_PLAZO = "Reposiciones.DiscusionPlazoHoras";
        internal const int PLAZO_HORAS_POR_DEFECTO = 48;
        internal const int HORAS_RECORDATORIO = 24;
        internal const int MESES_INDICADOR = 12;
        internal const int LONGITUD_COMENTARIO = 500;
        public const string TIPO_NOTIFICACION = "DiscusionDiferenciaReposicion";
        internal const string USUARIO_SISTEMA = "Sistema (desacuerdo)";
        internal const string MENSAJE_SIN_SCRIPT = "La discusión de diferencias de reposición todavía no está disponible (falta lanzar Scripts/Issue906_DiferenciasReposicionDiscusiones.sql).";

        private readonly IRepositorioDiscusionesDiferencias repositorio;
        private readonly IRepositorioDiferenciasReposicion diferenciasRepositorio;
        private readonly ServicioDiferenciasReposicion diferencias;
        private readonly IAvisadorDiscusionesDiferencias avisador;
        private readonly Func<DateTime> ahora;
        private readonly Func<int> plazoHoras;

        internal ServicioDiscusionesDiferencias(IRepositorioDiscusionesDiferencias repositorio, IRepositorioDiferenciasReposicion diferenciasRepositorio,
            ServicioDiferenciasReposicion diferencias, IAvisadorDiscusionesDiferencias avisador, Func<DateTime> ahora = null, Func<int> plazoHoras = null)
        {
            this.repositorio = repositorio;
            this.diferenciasRepositorio = diferenciasRepositorio;
            this.diferencias = diferencias;
            this.avisador = avisador;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.plazoHoras = plazoHoras ?? LeerPlazoHoras;
        }

        /// <summary>Todo sobre el MISMO contexto: lo de la discusión va en la transacción de la diferencia.</summary>
        public static ServicioDiscusionesDiferencias Crear(NVEntities db)
        {
            var repositorioDiferencias = new RepositorioDiferenciasReposicionSql(db);
            return new ServicioDiscusionesDiferencias(new RepositorioDiscusionesDiferenciasSql(db), repositorioDiferencias,
                new ServicioDiferenciasReposicion(repositorioDiferencias), new AvisadorDiscusionesDiferencias(new ServicioNotificacionesPush()));
        }

        private static int LeerPlazoHoras()
        {
            try
            {
                string valor = Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, "(defecto)", CLAVE_PLAZO);
                return int.TryParse(valor?.Trim(), out int horas) && horas >= 1 && horas <= 24 * 30 ? horas : PLAZO_HORAS_POR_DEFECTO;
            }
            catch
            {
                return PLAZO_HORAS_POR_DEFECTO;
            }
        }

        private void ExigirScript()
        {
            if (!repositorio.TablasCreadas())
            {
                throw new NestoBusinessException(MENSAJE_SIN_SCRIPT) { StatusCode = HttpStatusCode.ServiceUnavailable };
            }
        }

        // ---------------------------------------------------------------- la lista de diferencias

        /// <summary>Lo de la discusión en cada diferencia (aditivo). Sin el script o si falla, como antes. Nunca lanza.</summary>
        public async Task Completar(string empresa, IReadOnlyCollection<DiferenciaReposicionDTO> filas)
        {
            try
            {
                if (filas == null || !filas.Any(f => f != null) || !repositorio.TablasCreadas())
                {
                    return;
                }
                List<DiscusionDiferenciaDTO> discusiones = await repositorio.LeerPorDiferencias(empresa, filas.Where(f => f != null).Select(f => f.Id))
                    .ConfigureAwait(false) ?? new List<DiscusionDiferenciaDTO>();
                foreach (DiferenciaReposicionDTO fila in filas.Where(f => f != null))
                {
                    DiscusionDiferenciaDTO discusion = discusiones.FirstOrDefault(d => d.DiferenciaId == fila.Id);
                    fila.EstadoDiscusion = discusion?.Estado;
                    fila.PlazoDiscusion = discusion?.Estado == EstadosDiscusionDiferencia.ABIERTA ? discusion.Plazo : (DateTime?)null;
                    fila.ResumenDiscusion = discusion?.Resumen;
                    fila.PuedeAbrirDiscusion = SePuedeAbrir(fila, discusion);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Desacuerdo de reposición #906] No se ha podido leer la discusión de las diferencias: {ex.Message}", ex));
            }
        }

        /// <summary>Solo una FALTA pendiente, con origen conocido y sin discusión. Puro.</summary>
        internal static bool SePuedeAbrir(DiferenciaReposicionDTO fila, DiscusionDiferenciaDTO discusion)
            => fila != null && discusion == null && fila.Diferencia < 0 && fila.Estado == EstadosDiferenciaReposicion.PENDIENTE
                && !string.IsNullOrWhiteSpace(fila.Origen) && !string.IsNullOrWhiteSpace(fila.Destino);

        // ---------------------------------------------------------------- abrir

        public async Task<ResultadoDiscusionDiferenciaDTO> Abrir(string empresa, int id, AbrirDiscusionDiferenciaDTO peticion, IPrincipal usuario)
        {
            ExigirScript();
            string comentario = Comentario(peticion?.Comentario, obligatorio: true);
            DiferenciaReposicionDTO fila = await diferenciasRepositorio.Leer(empresa, id).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe la diferencia de reposición {id}.") { StatusCode = HttpStatusCode.NotFound };
            diferencias.ComprobarPuedeResolver(usuario, empresa, fila);
            DiscusionDiferenciaDTO existente = await repositorio.LeerPorDiferencia(empresa, id).ConfigureAwait(false);
            if (existente != null)
            {
                throw new NestoBusinessException($"La diferencia de {fila.Producto} de la reposición {fila.Traspaso} ya está en discusión ({existente.Estado}).")
                { StatusCode = HttpStatusCode.Conflict };
            }
            if (fila.Estado != EstadosDiferenciaReposicion.PENDIENTE)
            {
                throw new NestoBusinessException($"La diferencia de {fila.Producto} de la reposición {fila.Traspaso} ya está resuelta.") { StatusCode = HttpStatusCode.Conflict };
            }
            if (!SePuedeAbrir(fila, null))
            {
                throw new NestoBusinessException("«En discusión» es solo para cuando el destino recibió de menos y el origen dice que salió.");
            }
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null) ?? UsuarioAuditoriaHelper.DESCONOCIDO;
            DateTime momento = ahora();
            int unidades = -fila.Diferencia;

            // Los rasgos del momento (una fila por unidad): para medir la regla y, algún día, entrenar un modelo
            PruebasDesacuerdo pruebas = await repositorio.LeerPruebas(empresa, fila.Traspaso, fila.Producto, fila.Enviado).ConfigureAwait(false) ?? new PruebasDesacuerdo();
            List<IndicadorErroresAlmacen> indicadores = ReglaIndicadorErrores.PorAlmacen(
                await repositorio.LeerParaIndicador(empresa, momento.AddMonths(-MESES_INDICADOR)).ConfigureAwait(false));
            decimal? pvp = await repositorio.LeerPvp(empresa, fila.Producto).ConfigureAwait(false);
            string repartidor = await repositorio.LeerRepartidor(empresa, fila.Traspaso).ConfigureAwait(false);
            var discusion = new DiscusionDiferenciaDTO
            {
                DiferenciaId = id,
                Empresa = empresa,
                Traspaso = fila.Traspaso,
                Origen = fila.Origen?.Trim(),
                Destino = fila.Destino?.Trim(),
                Producto = fila.Producto?.Trim(),
                Unidades = unidades,
                Estado = EstadosDiscusionDiferencia.ABIERTA,
                AbiertaPor = quien,
                FechaApertura = momento,
                ComentarioOrigen = comentario,
                Plazo = momento.AddHours(plazoHoras())
            };
            List<ImputacionDesacuerdoFila> filas = Rasgos(fila, unidades, pruebas, indicadores, pvp, repartidor);
            int nuevo = await repositorio.Abrir(discusion, filas).ConfigureAwait(false);
            if (nuevo <= 0)
            {
                throw new NestoBusinessException($"La diferencia de {fila.Producto} de la reposición {fila.Traspaso} ya está en discusión.") { StatusCode = HttpStatusCode.Conflict };
            }
            discusion.Id = nuevo;
            await Avisar(empresa, new[] { discusion.Destino }, false, TituloAbierta(discusion), TextoAbierta(discusion, fila.Descripcion), id).ConfigureAwait(false);
            await Completar(empresa, new[] { fila }).ConfigureAwait(false);
            return new ResultadoDiscusionDiferenciaDTO
            {
                Discusion = discusion,
                Diferencia = fila,
                Mensaje = $"Abierta la discusión: {ServicioDiferenciasReposicion.NombreAlmacen(discusion.Destino)} tiene hasta el " +
                    $"{discusion.Plazo:dd/MM HH:mm} para contestar. Si no lo encuentra, se decide con la regla."
            };
        }

        /// <summary>Una fila por unidad con lo que se sabe al abrir. Puro.</summary>
        internal static List<ImputacionDesacuerdoFila> Rasgos(DiferenciaReposicionDTO fila, int unidades, PruebasDesacuerdo pruebas,
            List<IndicadorErroresAlmacen> indicadores, decimal? pvp, string repartidor)
        {
            IndicadorErroresAlmacen De(string almacen) => indicadores?.FirstOrDefault(i => string.Equals(i.Almacen, almacen?.Trim(), StringComparison.OrdinalIgnoreCase));
            return Enumerable.Range(1, Math.Max(0, unidades)).Select(u => new ImputacionDesacuerdoFila
            {
                Unidad = (short)u,
                Pvp = pvp,
                Preparador = fila.Preparador,
                UsuarioRecepcion = fila.UsuarioRecepcion,
                Repartidor = repartidor,
                LecturaOrigen = u > pruebas.SinLecturaOrigen,
                EscaneadasOrigen = pruebas.EscaneadasOrigen,
                Enviado = pruebas.Enviado,
                LecturaDestino = pruebas.Bultos != null && pruebas.Bultos.Any(b => b.LeidoAlRecibir.HasValue),
                Bultos = (short)(pruebas.Bultos?.Count ?? 0),
                BultosEnDestino = (short)(pruebas.Bultos?.Count(b => b.EnDestino) ?? 0),
                FechaRecepcion = fila.FechaRecepcion,
                IndicadorOrigen = De(fila.Origen)?.Valor,
                IndicadorDestino = De(fila.Destino)?.Valor
            }).ToList();
        }

        // ---------------------------------------------------------------- contestar (el destino)

        public async Task<ResultadoDiscusionDiferenciaDTO> Responder(string empresa, int id, ResponderDiscusionDiferenciaDTO peticion, IPrincipal usuario)
        {
            ExigirScript();
            string respuesta = RespuestasDiscusionDiferencia.Normalizar(peticion?.Respuesta)
                ?? throw new NestoBusinessException($"La respuesta «{peticion?.Respuesta?.Trim()}» no existe: tiene que ser LoHeEncontrado o NoEsta.");
            string comentario = Comentario(peticion.Comentario, obligatorio: false);
            DiscusionDiferenciaDTO discusion = await repositorio.LeerPorDiferencia(empresa, id).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"La diferencia {id} no está en discusión.") { StatusCode = HttpStatusCode.NotFound };
            ComprobarPuedeResponder(usuario, empresa, discusion);
            if (discusion.Estado != EstadosDiscusionDiferencia.ABIERTA)
            {
                throw new NestoBusinessException($"La discusión de {discusion.Producto} de la reposición {discusion.Traspaso} ya está cerrada ({discusion.Estado}).")
                { StatusCode = HttpStatusCode.Conflict };
            }
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null) ?? UsuarioAuditoriaHelper.DESCONOCIDO;
            await repositorio.GuardarRespuesta(discusion.Id, respuesta, quien, comentario).ConfigureAwait(false);
            if (respuesta == RespuestasDiscusionDiferencia.LO_HE_ENCONTRADO)
            {
                string resumen = $"{ServicioDiferenciasReposicion.NombreAlmacen(discusion.Destino)} lo ha encontrado: error al recibir.";
                ResultadoResolverDiferenciaReposicionDTO resuelta = await diferencias.ResolverSinComprobarPermiso(empresa, id,
                    ResolucionesDiferenciaReposicion.ERROR_RECEPCION, Recortar(("Lo he encontrado (en discusión). " + comentario).Trim(), LONGITUD_COMENTARIO), quien,
                    f => repositorio.Acordar(discusion.Id, resumen, discusion.Destino, ComoSeDemostroError.RECONOCIDO)).ConfigureAwait(false);
                await Avisar(empresa, new[] { discusion.Origen }, false, TituloCerrada(discusion), resumen, id).ConfigureAwait(false);
                discusion.Estado = EstadosDiscusionDiferencia.ACORDADA;
                discusion.Resumen = resumen;
                return new ResultadoDiscusionDiferenciaDTO { Discusion = discusion, Diferencia = resuelta?.Diferencia, Mensaje = resuelta?.Mensaje };
            }
            return await Decidir(empresa, discusion, quien).ConfigureAwait(false);
        }

        private void ComprobarPuedeResponder(IPrincipal usuario, string empresa, DiscusionDiferenciaDTO discusion)
        {
            if (ServicioDiferenciasReposicion.EsDeAlmacenODireccion(usuario))
            {
                return;
            }
            string suyo = diferencias.AlmacenDe(usuario, empresa);
            if (string.IsNullOrWhiteSpace(suyo) || !string.Equals(suyo.Trim(), discusion.Destino?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"La discusión de la reposición {discusion.Traspaso} la contesta {ServicioDiferenciasReposicion.NombreAlmacen(discusion.Destino)} (o Almacén y Dirección).");
            }
        }

        // ---------------------------------------------------------------- decidir (la regla)

        internal async Task<ResultadoDiscusionDiferenciaDTO> Decidir(string empresa, DiscusionDiferenciaDTO discusion, string quien)
        {
            DiferenciaReposicionDTO fila = await diferenciasRepositorio.Leer(empresa, discusion.DiferenciaId).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe la diferencia de reposición {discusion.DiferenciaId}.") { StatusCode = HttpStatusCode.NotFound };
            int unidades = Math.Max(0, -fila.Diferencia);
            PruebasDesacuerdo pruebas = await repositorio.LeerPruebas(empresa, fila.Traspaso, fila.Producto, fila.Enviado).ConfigureAwait(false) ?? new PruebasDesacuerdo();
            List<IndicadorErroresAlmacen> indicadores = ReglaIndicadorErrores.PorAlmacen(
                await repositorio.LeerParaIndicador(empresa, ahora().AddMonths(-MESES_INDICADOR)).ConfigureAwait(false));
            var caso = new CasoImputacionDesacuerdo
            {
                Origen = fila.Origen,
                Destino = fila.Destino,
                Unidades = unidades,
                Pruebas = pruebas,
                IndicadorOrigen = indicadores.FirstOrDefault(i => string.Equals(i.Almacen, fila.Origen?.Trim(), StringComparison.OrdinalIgnoreCase)),
                IndicadorDestino = indicadores.FirstOrDefault(i => string.Equals(i.Almacen, fila.Destino?.Trim(), StringComparison.OrdinalIgnoreCase)),
                UltimoTurno = await repositorio.UltimoTurno(fila.Origen, fila.Destino).ConfigureAwait(false)
            };
            // NestoAPI#906: aquí iría el modelo (ModeloImputacionDesacuerdo.QuienManda); hoy siempre la regla
            List<ImputacionUnidadDesacuerdo> decisiones = ReglaImputacionDesacuerdo.Decidir(caso);
            string origen = fila.Origen?.Trim().ToUpperInvariant();
            int alOrigen = decisiones.Count(d => d.Almacen == origen);
            int alDestino = decisiones.Count - alOrigen;
            string resumen = ReglaImputacionDesacuerdo.Resumen(decisiones);
            ResultadoResolverDiferenciaReposicionDTO resuelta = await diferencias.ResolverPorDesacuerdo(empresa, discusion.DiferenciaId, quien,
                Recortar("En desacuerdo, lo ha decidido la regla. " + resumen, LONGITUD_COMENTARIO),
                f => PlanStockDesacuerdo.Movimientos(empresa, f.Traspaso, f.Origen?.Trim(), f.Destino?.Trim(), f.Producto?.Trim(),
                    f.Ajuste == AjustesDiferenciaReposicion.HECHO, Math.Max(0, -f.Diferencia), alOrigen, alDestino, quien),
                f => repositorio.GuardarDecision(discusion.Id, resumen, decisiones, caso.IndicadorOrigen?.Valor, caso.IndicadorDestino?.Valor))
                .ConfigureAwait(false);
            discusion.Estado = EstadosDiscusionDiferencia.DECIDIDA;
            discusion.Resumen = resumen;
            await Avisar(empresa, new[] { discusion.Origen, discusion.Destino }, false, TituloCerrada(discusion), resumen, discusion.DiferenciaId).ConfigureAwait(false);
            return new ResultadoDiscusionDiferenciaDTO { Discusion = discusion, Diferencia = resuelta?.Diferencia, Mensaje = resuelta?.Mensaje, Imputaciones = decisiones };
        }

        // ---------------------------------------------------------------- cuando se resuelve como siempre

        /// <summary>
        /// El origen ha resuelto la diferencia como siempre con la discusión abierta: queda Acordada. «Confirmada» y «No estaba» son
        /// error RECONOCIDO del origen; «Error al recibir», del destino. Nunca lanza.
        /// </summary>
        public async Task AlResolver(string empresa, int id, string resolucion)
        {
            try
            {
                if (!repositorio.TablasCreadas())
                {
                    return;
                }
                DiscusionDiferenciaDTO discusion = await repositorio.LeerPorDiferencia(empresa, id).ConfigureAwait(false);
                if (discusion == null || discusion.Estado != EstadosDiscusionDiferencia.ABIERTA)
                {
                    return;
                }
                string errorDe = ErrorReconocidoAlResolver(resolucion, discusion.Origen, discusion.Destino);
                string resumen = $"Resuelta como «{resolucion}» con la discusión abierta.";
                await repositorio.Acordar(discusion.Id, resumen, errorDe, errorDe == null ? null : ComoSeDemostroError.RECONOCIDO).ConfigureAwait(false);
                await Avisar(empresa, new[] { discusion.Destino }, false, TituloCerrada(discusion), resumen, id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Desacuerdo de reposición #906] No se ha podido cerrar la discusión de la diferencia {id}: {ex.Message}", ex));
            }
        }

        internal static string ErrorReconocidoAlResolver(string resolucion, string origen, string destino)
        {
            switch (resolucion)
            {
                case ResolucionesDiferenciaReposicion.CONFIRMADA:
                case ResolucionesDiferenciaReposicion.NO_ESTABA:
                    return origen?.Trim().ToUpperInvariant();
                case ResolucionesDiferenciaReposicion.ERROR_RECEPCION:
                    return destino?.Trim().ToUpperInvariant();
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------- consultas

        public async Task<List<DiscusionDiferenciaDTO>> Listar(string empresa, string almacen, bool soloAbiertas, IPrincipal usuario)
        {
            ExigirScript();
            string puedeVer = diferencias.AlmacenQuePuedeVer(usuario, empresa, almacen);
            return await repositorio.Listar(empresa, puedeVer, soloAbiertas).ConfigureAwait(false) ?? new List<DiscusionDiferenciaDTO>();
        }

        public async Task<IndicadorErroresDTO> Indicador(string empresa, int meses)
        {
            DateTime hasta = ahora();
            DateTime desde = hasta.Date.AddMonths(-Math.Max(1, Math.Min(meses, 60)));
            List<DiferenciaParaIndicador> filas = await repositorio.LeerParaIndicador(empresa, desde).ConfigureAwait(false) ?? new List<DiferenciaParaIndicador>();
            return new IndicadorErroresDTO
            {
                Desde = desde,
                Hasta = hasta,
                Almacenes = ReglaIndicadorErrores.PorAlmacen(filas),
                Usuarios = ReglaIndicadorErrores.PorUsuario(filas)
            };
        }

        public async Task<ResumenVerdadDesacuerdoDTO> Verdad(int meses)
        {
            ExigirScript();
            DateTime desde = ahora().Date.AddMonths(-Math.Max(1, Math.Min(meses, 60)));
            return ResumenVerdadDesacuerdo.Calcular(await repositorio.Imputaciones(desde).ConfigureAwait(false), desde);
        }

        // ---------------------------------------------------------------- jobs

        /// <summary>El job: recordatorio a falta de <see cref="HORAS_RECORDATORIO"/> h y, vencido el plazo, decide la regla. Nunca lanza.</summary>
        public async Task<int> Revisar(string empresa)
        {
            int decididas = 0;
            if (!repositorio.TablasCreadas())
            {
                return 0;
            }
            DateTime momento = ahora();
            foreach (DiscusionDiferenciaDTO discusion in await repositorio.Abiertas(empresa).ConfigureAwait(false) ?? new List<DiscusionDiferenciaDTO>())
            {
                try
                {
                    if (momento >= discusion.Plazo)
                    {
                        _ = await Decidir(empresa, discusion, USUARIO_SISTEMA).ConfigureAwait(false);
                        decididas++;
                    }
                    else if (!discusion.RecordatorioEnviado.HasValue && momento >= discusion.Plazo.AddHours(-HORAS_RECORDATORIO))
                    {
                        await repositorio.MarcarRecordatorio(discusion.Id).ConfigureAwait(false);
                        await Avisar(empresa, new[] { discusion.Destino }, false, "Recordatorio: " + TituloAbierta(discusion),
                            $"Si no contesta antes del {discusion.Plazo:dd/MM HH:mm}, se decidirá con la regla. " + TextoAbierta(discusion, null), discusion.DiferenciaId)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Desacuerdo de reposición #906] Discusión {discusion.Id} (diferencia {discusion.DiferenciaId}): {ex.Message}", ex));
                }
            }
            return decididas;
        }

        /// <summary>
        /// El job nocturno de la verdad: a los casos decididos sin verdad de los últimos 24 meses, un inventario contabilizado posterior con
        /// sobrante (<see cref="ReglaVerdadInventario"/>). Si contradice a la regla, ReglaAcerto = 0 y campana a Dirección; no se deshace nada.
        /// </summary>
        public async Task<int> BuscarVerdad(string empresa, int meses = 24)
        {
            if (!repositorio.TablasCreadas())
            {
                return 0;
            }
            DateTime desde = ahora().Date.AddMonths(-meses);
            List<ImputacionDesacuerdoFila> filas = await repositorio.Imputaciones(desde).ConfigureAwait(false) ?? new List<ImputacionDesacuerdoFila>();
            List<CasoSinVerdad> casos = CasosSinVerdad(filas);
            if (!casos.Any())
            {
                return 0;
            }
            List<SobranteInventario> sobrantes = await repositorio.Sobrantes(empresa, casos.Select(c => c.Producto), casos.Min(c => c.Fecha)).ConfigureAwait(false);
            List<VerdadAsignada> verdades = ReglaVerdadInventario.Asignar(casos, sobrantes,
                filas.Where(f => f.InventarioDemostracion.HasValue).Select(f => f.InventarioDemostracion.Value));
            var contradicen = new List<string>();
            foreach (VerdadAsignada verdad in verdades)
            {
                await repositorio.GuardarVerdad(verdad.DiscusionId, verdad.Almacen, ComoSeDemostroError.INVENTARIO, verdad.Fecha, verdad.Inventario).ConfigureAwait(false);
                List<ImputacionDesacuerdoFila> delCaso = filas.Where(f => f.DiscusionId == verdad.DiscusionId).ToList();
                if (delCaso.Any(f => !string.Equals(f.AlmacenImputado?.Trim(), verdad.Almacen, StringComparison.OrdinalIgnoreCase)))
                {
                    ImputacionDesacuerdoFila primera = delCaso.First();
                    contradicen.Add($"Reposición {primera.Traspaso}, {primera.Producto}: la regla la imputó a {string.Join(" y ", delCaso.Select(f => ServicioDiferenciasReposicion.NombreAlmacen(f.AlmacenImputado)).Distinct())}, " +
                        $"pero el inventario del {verdad.Fecha:dd/MM/yyyy} de {ServicioDiferenciasReposicion.NombreAlmacen(verdad.Almacen)} tenía sobrante.");
                }
            }
            if (contradicen.Any())
            {
                await Avisar(empresa, new string[0], true, "Desacuerdos de reposición: el inventario contradice a la regla",
                    string.Join(Environment.NewLine, contradicen) + " No se ha deshecho nada.", 0).ConfigureAwait(false);
            }
            return verdades.Count;
        }

        /// <summary>Casos decididos por la regla en los que ninguna unidad tiene todavía verdad. Puro.</summary>
        internal static List<CasoSinVerdad> CasosSinVerdad(IEnumerable<ImputacionDesacuerdoFila> filas)
            => (filas ?? Enumerable.Empty<ImputacionDesacuerdoFila>())
                .Where(f => f != null)
                .GroupBy(f => f.DiscusionId)
                .Where(g => g.All(f => !string.IsNullOrWhiteSpace(f.AlmacenImputado)) && g.All(f => string.IsNullOrWhiteSpace(f.ErrorDemostradoDe)))
                .Select(g => new CasoSinVerdad
                {
                    DiscusionId = g.Key,
                    Producto = g.First().Producto,
                    Origen = g.First().Origen,
                    Destino = g.First().Destino,
                    Unidades = g.Count(),
                    Fecha = g.First().FechaRecepcion ?? g.Min(f => f.FechaDecision) ?? DateTime.MinValue
                }).ToList();

        // ---------------------------------------------------------------- textos y avisos

        internal static string TituloAbierta(DiscusionDiferenciaDTO d)
            => $"Reposición {d.Traspaso}: {ServicioDiferenciasReposicion.NombreAlmacen(d.Origen)} dice que el {d.Producto} salió";

        internal static string TextoAbierta(DiscusionDiferenciaDTO d, string descripcion)
        {
            string ud = d.Unidades == 1 ? "1 ud." : $"{d.Unidades} ud.";
            string producto = string.IsNullOrWhiteSpace(descripcion) ? d.Producto : $"{d.Producto} ({descripcion.Trim()})";
            return $"En {ServicioDiferenciasReposicion.NombreAlmacen(d.Destino)} se recibió {ud} menos de {producto}, pero " +
                $"{ServicioDiferenciasReposicion.NombreAlmacen(d.Origen)} ha comprobado que salió: «{d.ComentarioOrigen}». Búsquelo y conteste " +
                "«Lo he encontrado» o «No está» en Diferencias de reposición.";
        }

        internal static string TituloCerrada(DiscusionDiferenciaDTO d)
            => $"Reposición {d.Traspaso}: cerrada la discusión del {d.Producto}";

        private async Task Avisar(string empresa, IEnumerable<string> almacenes, bool direccion, string titulo, string cuerpo, int diferenciaId)
        {
            try
            {
                await avisador.Avisar(empresa, almacenes, direccion, titulo, cuerpo, diferenciaId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Desacuerdo de reposición #906] No se ha podido avisar ({titulo}): {ex.Message}", ex));
            }
        }

        private static string Comentario(string texto, bool obligatorio)
        {
            string limpio = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
            if (obligatorio && limpio == null)
            {
                throw new NestoBusinessException("Diga qué ha comprobado (por ejemplo, de qué hueco salió y en qué bulto la metió).");
            }
            if (limpio != null && limpio.Length > LONGITUD_COMENTARIO)
            {
                throw new NestoBusinessException($"El comentario no puede pasar de {LONGITUD_COMENTARIO} caracteres.");
            }
            return limpio;
        }

        private static string Recortar(string texto, int maximo) => texto == null ? null : (texto.Length > maximo ? texto.Substring(0, maximo) : texto);
    }

    /// <summary>NestoAPI#906: campana de Nesto y buzón de Ariadna. Algete = el grupo Almacén; una tienda = su gente (AlmacénPedidoVta).</summary>
    public class AvisadorDiscusionesDiferencias : IAvisadorDiscusionesDiferencias
    {
        private const string DOMINIO = "NUEVAVISION\\";
        private readonly IServicioNotificacionesPush notificaciones;

        public AvisadorDiscusionesDiferencias(IServicioNotificacionesPush notificaciones)
        {
            this.notificaciones = notificaciones;
        }

        public async Task Avisar(string empresa, IEnumerable<string> almacenes, bool direccion, string titulo, string cuerpo, int diferenciaId)
        {
            var candidatos = new List<string>();
            foreach (string almacen in (almacenes ?? Enumerable.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim().ToUpperInvariant()).Distinct())
            {
                if (almacen == Constantes.Almacenes.ALGETE)
                {
                    candidatos.AddRange(Verifactu.MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN) ?? new List<string>());
                }
                else
                {
                    using (var db = new NVEntities())
                    {
                        candidatos.AddRange(await AvisadorReposicionAutomatica.UsuariosDelAlmacen(db, empresa, almacen).ConfigureAwait(false) ?? new List<string>());
                    }
                }
            }
            if (direccion)
            {
                candidatos.AddRange(Verifactu.MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION) ?? new List<string>());
            }
            List<string> usuarios = candidatos.Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = cuerpo,
                Tipo = ServicioDiscusionesDiferencias.TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = ServicioDiscusionesDiferencias.TIPO_NOTIFICACION,
                    ["vista"] = PreparacionAlmacen.AvisadorReposiciones.VISTA_DIFERENCIAS,
                    ["referencia"] = diferenciaId.ToString()
                }
            };
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                await notificaciones.GuardarEnBuzonDeUsuario(usuario.Substring(usuario.LastIndexOf('\\') + 1), Constantes.Aplicaciones.ARIADNA, notificacion).ConfigureAwait(false);
            }
        }
    }

    /// <summary>NestoAPI#906: los dos jobs (Hangfire). Nunca rompen (ELMAH).</summary>
    public static class DiscusionesDiferenciasJobsService
    {
        public const string ID_JOB_REVISAR = "reposiciones-discusiones-diferencias";
        /// <summary>Cada hora de 8:05 a 21:05, todos los días (los plazos corren también en fin de semana).</summary>
        public const string CRON_REVISAR = "5 8-21 * * *";
        public const string ID_JOB_VERDAD = "reposiciones-desacuerdos-verdad";
        /// <summary>3:35 Madrid, fuera de horario.</summary>
        public const string CRON_VERDAD = "35 3 * * *";

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task Revisar()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    _ = await ServicioDiscusionesDiferencias.Crear(db).Revisar(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Desacuerdo de reposición #906] Job de discusiones: {ex.Message}", ex));
            }
        }

        [Queue(IndicadoresStock.IndicadoresStockJobsService.COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static async Task BuscarVerdad()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    _ = await ServicioDiscusionesDiferencias.Crear(db).BuscarVerdad(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Desacuerdo de reposición #906] Job de la verdad a posteriori: {ex.Message}", ex));
            }
        }
    }
}
