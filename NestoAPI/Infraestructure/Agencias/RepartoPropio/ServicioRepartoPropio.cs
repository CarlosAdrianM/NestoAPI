using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Informes;
using NestoAPI.Models.Pagos;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>Lo que el servicio lee y escribe, detrás de una interfaz para probar las reglas sin BD.</summary>
    public interface IRepositorioRepartoPropio
    {
        /// <summary>¿Se ha lanzado Scripts/Issue739_RepartoPropio.sql (tabla EnviosAgenciaPruebasEntrega)?</summary>
        bool ScriptAplicado();
        /// <summary>Envíos de la agencia 14 en estado Tramitado o Incidentado con Fecha en [desde, hasta).</summary>
        Task<List<EnviosAgencia>> LeerEnviosEnReparto(DateTime desde, DateTime hasta);
        /// <summary>El envío, con su agencia (cuenta de caja) y su empresa, para modificarlo. Null si no existe.</summary>
        Task<EnviosAgencia> LeerEnvio(int numero);
        Task<PruebaEntregaReparto> LeerPorClave(Guid clave);
        /// <summary>La última entrega registrada del envío (sin la firma), o null.</summary>
        Task<PruebaEntregaReparto> LeerUltimaEntrega(int envio);
        /// <summary>La firma de la última entrega con firma, o null.</summary>
        Task<byte[]> LeerFirma(int envio);
        /// <summary>
        /// En UNA transacción: contabiliza el reembolso cobrado (si <paramref name="importeAContabilizar"/> tiene valor, por la
        /// puerta de siempre, TramitacionEnviosService.ContabilizarReembolsoAsync), inserta la prueba con su asiento, la historia
        /// y el cambio de estado del envío. O todo o nada. Devuelve el asiento (0 sin contabilizar).
        /// </summary>
        /// <exception cref="ClaveIdempotenciaRepetidaException">Otra petición ya registró esa clave.</exception>
        Task<int> Registrar(PruebaEntregaReparto prueba, byte[] firma, EnviosAgencia envio, IList<EnvioHistoria> historia,
            decimal? importeAContabilizar, string usuario);
    }

    /// <summary>La clave de idempotencia ya está en la tabla (dos peticiones a la vez con la misma clave).</summary>
    public class ClaveIdempotenciaRepetidaException : Exception
    {
        public ClaveIdempotenciaRepetidaException(Exception interior) : base("Clave de idempotencia repetida.", interior) { }
    }

    public enum TipoResultadoReparto
    {
        Ok,
        SinContenido,
        Invalido,
        NoEncontrado,
        Conflicto
    }

    public class ResultadoReparto<T>
    {
        public TipoResultadoReparto Tipo { get; set; }
        public T Contenido { get; set; }
        /// <summary>Para el usuario, de usted.</summary>
        public string Mensaje { get; set; }
        /// <summary>Ariadna#63: código del error para la app (va en error.code). Null: el error de siempre ({ Message }).</summary>
        public string Codigo { get; set; }
        /// <summary>Ariadna#63: datos del error para la app (van en error.details, p. ej. { repartidor }).</summary>
        public object Detalles { get; set; }

        public static ResultadoReparto<T> Bien(T contenido) => new ResultadoReparto<T> { Tipo = TipoResultadoReparto.Ok, Contenido = contenido };
        public static ResultadoReparto<T> Vacio() => new ResultadoReparto<T> { Tipo = TipoResultadoReparto.SinContenido };
        public static ResultadoReparto<T> Mal(TipoResultadoReparto tipo, string mensaje) => new ResultadoReparto<T> { Tipo = tipo, Mensaje = mensaje };
    }

    /// <summary>
    /// NestoAPI#739: las reglas de api/RepartoPropio (contrato en el comentario de la issue del 29/10/26 y cambios de Carlos del
    /// mismo día). La entrega es por ENVÍO; idempotente por ClaveIdempotencia (Ariadna reintenta desde su cola sin cobertura).
    /// Entregar e incidentar cambian el estado del envío con la misma regla que el seguimiento de las agencias
    /// (<see cref="SeguimientoEnviosJobsService.AplicarSeguimiento"/>), quedan en EnviosHistoria y una incidencia nueva avisa a los
    /// comerciales como las de las agencias (#666). El reembolso del reparto propio se contabiliza AL ENTREGAR (lo cobrado, contra
    /// la caja del usuario que entrega: su parámetro CajaDefecto, nunca la CuentaReembolsos de la agencia 14). La firma y el DNI solo van a EnviosAgenciaPruebasEntrega: nunca a ELMAH ni a los
    /// mensajes ni a la historia.
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        public const string MENSAJE_FALTA_SCRIPT = "Falta lanzar el script Scripts/Issue739_RepartoPropio.sql (reparto propio): " +
            "hasta entonces no se pueden registrar entregas ni incidencias. Avise a Informática.";
        public const string MENSAJE_SIN_CAJA = "No tiene caja asignada (parámetro CajaDefecto): pida a Administración que se la ponga " +
            "para poder cobrar reembolsos.";
        /// <summary>El mismo parámetro de usuario que usa Nesto en la ventana de Cajas (Parametros.Claves.CajaDefecto).</summary>
        public const string PARAMETRO_CAJA = "CajaDefecto";
        public const string MENSAJE_SIN_DATAFONO = "Sin datáfono: use Efectivo o Enlace.";
        internal const int DIAS_ATRAS_LISTA = 30;
        internal const int MAXIMO_RECEPTOR = 100;
        internal const int MAXIMO_DNI = 20;
        internal const int MAXIMO_TEXTO = 1000;
        internal const int MAXIMO_FIRMA_BYTES = 1024 * 1024;
        internal const string DETALLE_ENTREGADO = "Entregado (reparto propio)";
        private static readonly byte[] CabeceraPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static readonly Dictionary<string, string> Motivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { IncidenciaRepartoDTO.AUSENTE, "Ausente" },
            { IncidenciaRepartoDTO.DIRECCION_INCORRECTA, "Dirección incorrecta" },
            { IncidenciaRepartoDTO.RECHAZADO, "Rechazado" },
            { IncidenciaRepartoDTO.FALTAN_BULTOS, "Faltan bultos" },
            { IncidenciaRepartoDTO.NADA_QUE_RECOGER, "No había nada que recoger" },
            { IncidenciaRepartoDTO.OTRO, "Otro" }
        };

        private static readonly string[] Cobros =
        {
            PruebaEntregaReparto.COBRO_EFECTIVO, PruebaEntregaReparto.COBRO_ENLACE, PruebaEntregaReparto.COBRO_NO_COBRADO
        };

        private readonly IRepositorioRepartoPropio repositorio;
        private readonly Func<DateTime> ahora;
        private readonly Func<EnviosAgencia, Task> avisarIncidencia;
        private readonly ILectorParametrosUsuario parametros;
        private readonly Func<IServicioPagos> pagos;
        private readonly Func<IInformesService> informes;
        /// <summary>Ariadna#63: la carga de la furgoneta (EnviosAgenciaCargaBultos). Null: no hay carga (Envios sin repartidor).</summary>
        private readonly IRepositorioCargaFurgoneta carga;

        /// <summary>NestoAPI#759: notas, orden, reposiciones y avisos de las paradas (vacío: como antes del corte 3).</summary>
        private readonly DependenciasParadasReparto paradas;

        /// <param name="carga">Ariadna#63: quién lleva cada envío y qué bultos ha cargado. Null: como antes de la carga.</param>
        /// <param name="paradas">NestoAPI#759: lo de las paradas (notas, orden, reposiciones, SignalR). Null: como antes.</param>
        /// <param name="pagos">Los enlaces de pago (cobro por enlace): el mismo IniciarPago que el enlace que manda Nesto desde el pedido.</param>
        /// <param name="informes">El extracto contable de la caja (MisCobros): el mismo que el de la ventana de Cajas de Nesto.</param>
        public ServicioRepartoPropio(IRepositorioRepartoPropio repositorio, Func<DateTime> ahora = null, Func<EnviosAgencia, Task> avisarIncidencia = null,
            ILectorParametrosUsuario parametros = null, Func<IServicioPagos> pagos = null, Func<IInformesService> informes = null,
            IRepositorioCargaFurgoneta carga = null, DependenciasParadasReparto paradas = null)
        {
            this.carga = carga;
            this.paradas = paradas ?? new DependenciasParadasReparto();
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.avisarIncidencia = avisarIncidencia;
            this.parametros = parametros ?? new LectorParametrosUsuario();
            this.pagos = pagos ?? (() => new ServicioPagos(new RedsysService(), new global::NestoAPI.Infraestructure.Contabilidad.ContabilidadService(), new LectorParametrosUsuario()));
            this.informes = informes ?? (() => new InformesService());
        }

        // ---------------------------------------------------------------- lecturas

        /// <summary>
        /// Los envíos en reparto (Tramitado o Incidentado) con fecha hasta <paramref name="fecha"/> (hoy por defecto) y desde
        /// 30 días antes: un incidentado de ayer sigue en la lista para reintentarlo. Ariadna#63: con quién lo lleva y cuántos
        /// bultos ha cargado; con <paramref name="soloMios"/>, solo los que lleva <paramref name="usuario"/> (sin el script de la
        /// carga no hay repartidores y se ignora: la lista de siempre).
        /// </summary>
        public async Task<ResultadoReparto<List<EnvioRepartoDTO>>> Envios(DateTime? fecha, bool soloMios = false, string usuario = null)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<List<EnvioRepartoDTO>>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            DateTime dia = (fecha ?? ahora()).Date;
            List<EnviosAgencia> envios = await repositorio.LeerEnviosEnReparto(dia.AddDays(-DIAS_ATRAS_LISTA), dia.AddDays(1)).ConfigureAwait(false)
                ?? new List<EnviosAgencia>();
            List<EnvioRepartoDTO> lista = envios
                .Where(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && EstadoTexto(e.Estado) != null && e.Estado != Constantes.Agencias.ESTADO_ENTREGADO)
                .OrderBy(e => e.Fecha).ThenBy(e => e.Numero)
                .Select(e => ADto(e, new EnvioRepartoDTO()))
                .ToList();
            await CompletarNotas(lista).ConfigureAwait(false);
            if (CargaDisponible())
            {
                await CompletarCarga(lista).ConfigureAwait(false);
                if (soloMios)
                {
                    // NestoAPI#759: las suyas y las que no ha cargado nadie (para poder cargarlas).
                    lista = lista.Where(e => NucleoParadasReparto.Visible(e.Repartidor, usuario, true)).ToList();
                }
            }
            lista.AddRange((await ParadasReposicionEnReparto(dia).ConfigureAwait(false))
                .Where(p => NucleoParadasReparto.Visible(p.Repartidor, usuario, soloMios)));
            await CompletarOrden(lista).ConfigureAwait(false);
            await CompletarCoordenadas(lista).ConfigureAwait(false);
            await CompletarHorarios(lista, dia).ConfigureAwait(false);
            return ResultadoReparto<List<EnvioRepartoDTO>>.Bien(NucleoParadasReparto.Ordenar(lista));
        }

        /// <summary>
        /// La parada de un bulto escaneado: RP{envío}-{N}, NestoAPI#759 NE{pedido}, NR{pedido}, NR{pedido}-{N} y R{traspaso}-{N}
        /// (este, solo si ya está cargado en una parada de reposición).
        /// </summary>
        public async Task<ResultadoReparto<EnvioRepartoBultoDTO>> Bulto(string codigo)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            if (EsCodigoReposicion(codigo, out int traspaso, out int bultoReposicion))
            {
                return await BultoReposicion(traspaso, bultoReposicion).ConfigureAwait(false);
            }
            CodigoParadaResuelto resuelto = await ResolverCodigo(codigo).ConfigureAwait(false);
            if (resuelto.Error.HasValue)
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(resuelto.Error.Value, resuelto.Mensaje);
            }
            int numero = resuelto.Envio;
            int bulto = resuelto.Bulto;
            EnviosAgencia envio = await repositorio.LeerEnvio(numero).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe el envío {numero} del reparto propio.");
            }
            if (bulto > Math.Max((int)envio.Bultos, 1))
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.NoEncontrado,
                    $"El envío {numero} tiene {envio.Bultos} {(envio.Bultos == 1 ? "bulto" : "bultos")}: no existe el bulto {bulto}.");
            }
            if (EstadoTexto(envio.Estado) == null)
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.Conflicto, NoEstaEnReparto(envio));
            }
            EnvioRepartoBultoDTO dto = ADto(envio, new EnvioRepartoBultoDTO());
            dto.Bulto = bulto;
            await CompletarNotas(new List<EnvioRepartoDTO> { dto }).ConfigureAwait(false);
            if (CargaDisponible())
            {
                await CompletarCarga(new List<EnvioRepartoDTO> { dto }).ConfigureAwait(false);
            }
            await CompletarOrden(new List<EnvioRepartoDTO> { dto }).ConfigureAwait(false);
            await CompletarCoordenadas(new List<EnvioRepartoDTO> { dto }).ConfigureAwait(false);
            await CompletarHorarios(new List<EnvioRepartoDTO> { dto }, ahora().Date).ConfigureAwait(false);
            return ResultadoReparto<EnvioRepartoBultoDTO>.Bien(dto);
        }

        public async Task<ResultadoReparto<PruebaEntregaDTO>> PruebaEntrega(int envioId)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<PruebaEntregaDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            PruebaEntregaReparto prueba = await repositorio.LeerUltimaEntrega(envioId).ConfigureAwait(false);
            if (prueba == null)
            {
                return ResultadoReparto<PruebaEntregaDTO>.Vacio();
            }
            return ResultadoReparto<PruebaEntregaDTO>.Bien(new PruebaEntregaDTO
            {
                EnvioId = prueba.EnvioId,
                FechaHora = prueba.FechaHora,
                Receptor = prueba.Receptor,
                DniReceptor = prueba.DniReceptor,
                CobroReembolso = prueba.CobroReembolso,
                ImporteCobrado = prueba.ImporteCobrado,
                CuentaCaja = prueba.CuentaCaja,
                UrlPago = prueba.UrlPago,
                Usuario = prueba.Usuario,
                TieneFirma = prueba.TieneFirma,
                Latitud = prueba.Latitud,
                Longitud = prueba.Longitud
            });
        }

        public async Task<ResultadoReparto<byte[]>> Firma(int envioId)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<byte[]>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            byte[] firma = await repositorio.LeerFirma(envioId).ConfigureAwait(false);
            return firma == null || firma.Length == 0 ? ResultadoReparto<byte[]>.Vacio() : ResultadoReparto<byte[]>.Bien(firma);
        }

        // ---------------------------------------------------------------- entregar

        public async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> Entregar(int envioId, EntregaRepartoDTO peticion, string usuario,
            OrigenPeticionReparto origen = null)
        {
            // NestoAPI#772: los bultos leídos tienen que ser de la parada (y, con el interruptor, hay que leerlos).
            ResultadoReparto<EstadoEnvioRepartoDTO> bultosMal = await ValidarBultosEntrega(envioId, peticion).ConfigureAwait(false);
            if (bultosMal != null)
            {
                return bultosMal;
            }
            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = NucleoParadasReparto.EsReposicion(envioId)
                ? await EntregarReposicion(NucleoParadasReparto.IdReposicion(envioId), peticion, usuario).ConfigureAwait(false)
                : await EntregarEnvio(envioId, peticion, usuario).ConfigureAwait(false);
            if (resultado.Tipo == TipoResultadoReparto.Ok)
            {
                // NestoAPI#772: salen de la furgoneta (los leídos o los de la parada que llevaba). Nunca rompe la entrega.
                await AnotarEntrega(envioId, peticion, usuario, resultado.Contenido).ConfigureAwait(false);
            }
            Avisar(resultado.Tipo, usuario, RepartoCambiadoDTO.MOTIVO_ENTREGA, origen);
            return resultado;
        }

        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> EntregarEnvio(int envioId, EntregaRepartoDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la ClaveIdempotencia de la entrega.");
            }
            if (peticion.BultosRecogidos.HasValue && (peticion.BultosRecogidos.Value < 0 || peticion.BultosRecogidos.Value > NestoAPI.Models.PedidosVenta.TiposNotaEntrega.BULTOS_MAXIMO))
            {
                return Invalido($"Los bultos recogidos tienen que estar entre 0 y {NestoAPI.Models.PedidosVenta.TiposNotaEntrega.BULTOS_MAXIMO}.");
            }
            string receptor = Limpio(peticion.Receptor);
            string dni = Limpio(peticion.DniReceptor);
            string observaciones = Limpio(peticion.Observaciones);
            if (receptor != null && receptor.Length > MAXIMO_RECEPTOR)
            {
                return Invalido($"El nombre de quien recoge no puede pasar de {MAXIMO_RECEPTOR} caracteres.");
            }
            if (dni != null && dni.Length > MAXIMO_DNI)
            {
                return Invalido($"El documento de quien recoge no puede pasar de {MAXIMO_DNI} caracteres.");
            }
            if (observaciones != null && observaciones.Length > MAXIMO_TEXTO)
            {
                return Invalido($"Las observaciones no pueden pasar de {MAXIMO_TEXTO} caracteres.");
            }
            string cobro = null;
            if (!string.IsNullOrWhiteSpace(peticion.CobroReembolso))
            {
                if (string.Equals(peticion.CobroReembolso.Trim(), PruebaEntregaReparto.COBRO_TARJETA, StringComparison.OrdinalIgnoreCase))
                {
                    return Invalido(MENSAJE_SIN_DATAFONO);
                }
                cobro = Cobros.FirstOrDefault(c => string.Equals(c, peticion.CobroReembolso.Trim(), StringComparison.OrdinalIgnoreCase));
                if (cobro == null)
                {
                    return Invalido("El cobro del reembolso tiene que ser Efectivo, Enlace o NoCobrado.");
                }
            }
            if (peticion.ImporteCobrado.HasValue && peticion.ImporteCobrado.Value < 0)
            {
                return Invalido("El importe cobrado no puede ser negativo.");
            }
            string correoEnlace = null;
            if (cobro == PruebaEntregaReparto.COBRO_ENLACE)
            {
                correoEnlace = Limpio(peticion.CorreoEnlace);
                if (correoEnlace == null)
                {
                    return Invalido("Para cobrar por enlace indique el correo al que se manda el enlace de pago.");
                }
                if (!CorreoFacturacionTienda.EsCorreoValido(correoEnlace))
                {
                    return Invalido($"El correo «{correoEnlace}» no es válido: revíselo para mandar el enlace de pago.");
                }
            }
            byte[] firma = null;
            if (!string.IsNullOrWhiteSpace(peticion.FirmaPng))
            {
                firma = LeerPng(peticion.FirmaPng, out string error);
                if (firma == null)
                {
                    return Invalido(error);
                }
            }

            EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return NoEncontrado(envioId);
            }

            ResultadoReparto<EstadoEnvioRepartoDTO> repetida = await YaRegistrada(peticion.ClaveIdempotencia, envioId, PruebaEntregaReparto.TIPO_ENTREGA).ConfigureAwait(false);
            if (repetida != null)
            {
                return repetida;
            }
            if (envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return Conflicto($"El envío {envio.Numero} ya está entregado.");
            }
            if (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO)
            {
                return Conflicto(NoEstaEnReparto(envio));
            }

            // El cobro: con reembolso y sin decir nada, «no cobrado» (Carlos 29/10/26: de momento es opcional).
            decimal reembolso = envio.Reembolso > 0 ? envio.Reembolso : 0m;
            if (cobro == null && reembolso > 0)
            {
                cobro = PruebaEntregaReparto.COBRO_NO_COBRADO;
            }
            bool efectivo = cobro == PruebaEntregaReparto.COBRO_EFECTIVO;
            bool enlace = cobro == PruebaEntregaReparto.COBRO_ENLACE;
            // Con enlace, ImporteCobrado es el importe del enlace (aún no cobrado: entra por la notificación de Redsys).
            decimal? importeCobrado = cobro == null ? peticion.ImporteCobrado
                : efectivo || enlace ? peticion.ImporteCobrado ?? reembolso
                : 0m;
            decimal? aContabilizar = null;
            string cuentaCaja = null;
            if (efectivo && importeCobrado > 0 && MomentosContabilizarReembolso.De(envio.Agencia) == MomentoContabilizarReembolso.AlEntregar)
            {
                // Carlos 29/10/26: la caja es del REPARTIDOR, no de la agencia (con dos repartidores, cada uno cobra en su caja).
                cuentaCaja = CajaDelUsuario(usuario);
                if (cuentaCaja == null)
                {
                    return Invalido(MENSAJE_SIN_CAJA);
                }
                aContabilizar = importeCobrado;
            }
            RespuestaIniciarPago pago = null;
            if (enlace)
            {
                if (!(importeCobrado > 0))
                {
                    return Invalido("No hay importe para el enlace de pago: indique el importe a cobrar.");
                }
                // El mismo enlace que manda Nesto desde el pedido («Pago pedido N de Nueva Visión»): el concepto lleva el pedido
                // (#679, EnlacesPagoPedido). Antes de tocar nada: si falla, el envío sigue como estaba. Un reintento con la misma
                // clave ya registrada no llega aquí (YaRegistrada devuelve el enlace guardado).
                try
                {
                    pago = await pagos().IniciarPago(new SolicitudPagoTPV
                    {
                        Empresa = envio.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Cliente = envio.Cliente?.Trim(),
                        Contacto = envio.Contacto?.Trim(),
                        Importe = importeCobrado.Value,
                        Descripcion = ConceptoEnlace(envio),
                        Correo = correoEnlace,
                        Vendedor = envio.Vendedor?.Trim()
                    }, usuario).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    return Invalido($"No se ha podido crear el enlace de pago del envío {envio.Numero}: {ex.Message}");
                }
            }

            // NestoAPI#759: en las notas de recogida, los bultos que se trae (en las demás paradas se ignoran).
            bool recogida = (await NotaDe(envio.Pedido).ConfigureAwait(false))?.EsRecogida == true;
            short? bultosRecogidos = recogida && peticion.BultosRecogidos.HasValue ? (short?)peticion.BultosRecogidos.Value : null;

            DateTime fechaHora = peticion.FechaHora ?? ahora();
            var prueba = new PruebaEntregaReparto
            {
                BultosRecogidos = bultosRecogidos,
                EnvioId = envio.Numero,
                ClaveIdempotencia = peticion.ClaveIdempotencia,
                Tipo = PruebaEntregaReparto.TIPO_ENTREGA,
                FechaHora = fechaHora,
                Receptor = receptor,
                DniReceptor = dni,
                CobroReembolso = cobro,
                ImporteCobrado = importeCobrado,
                CuentaCaja = cuentaCaja,
                PagoTPVId = pago?.IdPago,
                UrlPago = pago?.UrlPaginaPago,
                Texto = observaciones,
                Latitud = peticion.Latitud,
                Longitud = peticion.Longitud,
                Usuario = usuario,
                FechaRegistro = ahora(),
                TieneFirma = firma != null
            };
            string observacionesHistoria = recogida
                ? "Reparto propio: recogido" + (bultosRecogidos.HasValue ? $" ({bultosRecogidos.Value} {(bultosRecogidos.Value == 1 ? "bulto" : "bultos")})" : string.Empty)
                : "Reparto propio: entregado" + (cobro == null ? string.Empty : $" (reembolso: {TextoCobro(cobro, importeCobrado)})");
            List<EnvioHistoria> historia = Historia(envio, observacionesHistoria, usuario);
            SeguimientoEnviosJobsService.AplicarSeguimiento(envio, new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Entregado,
                FechaEntrega = fechaHora,
                Detalle = DETALLE_ENTREGADO
            });
            envio.Usuario = usuario;

            return await Registrar(prueba, firma, envio, historia, aContabilizar, usuario, EnvioRepartoDTO.ESTADO_ENTREGADO).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------- incidencia

        public async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> Incidencia(int envioId, IncidenciaRepartoDTO peticion, string usuario,
            OrigenPeticionReparto origen = null)
        {
            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = await IncidenciaEnvio(envioId, peticion, usuario).ConfigureAwait(false);
            Avisar(resultado.Tipo, usuario, RepartoCambiadoDTO.MOTIVO_INCIDENCIA, origen);
            return resultado;
        }

        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> IncidenciaEnvio(int envioId, IncidenciaRepartoDTO peticion, string usuario)
        {
            if (NucleoParadasReparto.EsReposicion(envioId))
            {
                return Invalido("Una parada de reposición no lleva incidencias: si no puede dejarla en la tienda, vuelva con ella al almacén y avise a la tienda.");
            }
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la ClaveIdempotencia de la incidencia.");
            }
            string motivo = Motivos.Keys.FirstOrDefault(m => string.Equals(m, peticion.Motivo?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (motivo == null)
            {
                return Invalido("El motivo de la incidencia tiene que ser Ausente, DireccionIncorrecta, Rechazado, FaltanBultos, NadaQueRecoger u Otro.");
            }
            string texto = Limpio(peticion.Texto);
            if (motivo == IncidenciaRepartoDTO.OTRO && texto == null)
            {
                return Invalido("Con el motivo «Otro» hay que escribir qué ha pasado.");
            }
            if (texto != null && texto.Length > MAXIMO_TEXTO)
            {
                return Invalido($"El texto de la incidencia no puede pasar de {MAXIMO_TEXTO} caracteres.");
            }

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
                return Conflicto($"El envío {envio.Numero} ya está entregado: no se puede poner una incidencia.");
            }
            if (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO)
            {
                return Conflicto(NoEstaEnReparto(envio));
            }
            if (motivo == IncidenciaRepartoDTO.NADA_QUE_RECOGER && (await NotaDe(envio.Pedido).ConfigureAwait(false))?.EsRecogida != true)
            {
                return Invalido("«No había nada que recoger» solo vale para las notas de recogida.");
            }

            DateTime fechaHora = peticion.FechaHora ?? ahora();
            var prueba = new PruebaEntregaReparto
            {
                EnvioId = envio.Numero,
                ClaveIdempotencia = peticion.ClaveIdempotencia,
                Tipo = PruebaEntregaReparto.TIPO_INCIDENCIA,
                FechaHora = fechaHora,
                Motivo = motivo,
                Texto = texto,
                Latitud = peticion.Latitud,
                Longitud = peticion.Longitud,
                Usuario = usuario,
                FechaRegistro = ahora()
            };
            string detalle = DetalleIncidencia(motivo, texto);
            short estadoAnterior = envio.Estado;
            List<EnvioHistoria> historia = Historia(envio, "Reparto propio: incidencia (" + Motivos[motivo] + ")", usuario);
            SeguimientoEnviosJobsService.AplicarSeguimiento(envio, new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Incidentado,
                Detalle = detalle
            });
            envio.Usuario = usuario;

            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = await Registrar(prueba, null, envio, historia, null, usuario, EnvioRepartoDTO.ESTADO_INCIDENTADO).ConfigureAwait(false);
            if (resultado.Tipo == TipoResultadoReparto.Ok && AvisadorIncidenciasEnvio.PasaAIncidencia(estadoAnterior, envio) && avisarIncidencia != null)
            {
                // #666: como cuando la incidencia la da una agencia. Nunca rompe la respuesta.
                try
                {
                    await avisarIncidencia(envio).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reparto propio #739] No se pudo avisar de la incidencia del envío {envio.Numero}: {ex.Message}", ex));
                }
            }
            return resultado;
        }

        // ---------------------------------------------------------------- mis cobros

        /// <summary>
        /// GET api/RepartoPropio/MisCobros: el extracto contable del día de la caja del usuario (su CajaDefecto), el mismo que el
        /// de la ventana de Cajas de Nesto (api/Informes/ExtractoContable). Importe = Debe − Haber de la caja (positivo, entra).
        /// </summary>
        public async Task<ResultadoReparto<MisCobrosRepartoDTO>> MisCobros(DateTime? fecha, string usuario)
        {
            string cuenta = CajaDelUsuario(usuario);
            if (cuenta == null)
            {
                return ResultadoReparto<MisCobrosRepartoDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_SIN_CAJA);
            }
            DateTime dia = (fecha ?? ahora()).Date;
            IInformesService servicioInformes = informes();
            decimal saldoAnterior = await servicioInformes.LeerSaldoContableAnteriorAsync(Constantes.Empresas.EMPRESA_POR_DEFECTO, cuenta, dia)
                .ConfigureAwait(false);
            List<ExtractoContableDTO> lineas = await servicioInformes.LeerExtractoContableAsync(Constantes.Empresas.EMPRESA_POR_DEFECTO, cuenta, dia, dia)
                .ConfigureAwait(false) ?? new List<ExtractoContableDTO>();
            List<ApunteCajaRepartoDTO> apuntes = lineas
                .OrderBy(l => l.Fecha).ThenBy(l => l.Id)
                .Select(l => new ApunteCajaRepartoDTO
                {
                    Fecha = l.Fecha,
                    Concepto = l.Concepto?.Trim(),
                    Importe = l.Debe - l.Haber,
                    Asiento = l.Asiento,
                    Documento = l.Documento?.Trim()
                })
                .ToList();
            decimal totalDia = apuntes.Sum(a => a.Importe);
            return ResultadoReparto<MisCobrosRepartoDTO>.Bien(new MisCobrosRepartoDTO
            {
                Cuenta = cuenta,
                Fecha = dia,
                SaldoAnterior = saldoAnterior,
                Apuntes = apuntes,
                TotalDia = totalDia,
                SaldoFinal = saldoAnterior + totalDia
            });
        }

        // ---------------------------------------------------------------- piezas

        /// <summary>
        /// La CajaDefecto del usuario (empresa 1, sin dominio), SOLO de su propia fila: la de «(defecto)» sería la caja de otro.
        /// Null si no la tiene.
        /// </summary>
        private string CajaDelUsuario(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }
            string sinDominio = usuario.Substring(usuario.IndexOf('\\') + 1).Trim();
            string caja = parametros.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, sinDominio, PARAMETRO_CAJA)?.Trim();
            return string.IsNullOrEmpty(caja) ? null : caja;
        }

        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> Registrar(PruebaEntregaReparto prueba, byte[] firma, EnviosAgencia envio,
            List<EnvioHistoria> historia, decimal? aContabilizar, string usuario, string estado)
        {
            try
            {
                int asiento = await repositorio.Registrar(prueba, firma, envio, historia, aContabilizar, usuario).ConfigureAwait(false);
                prueba.Asiento = asiento > 0 ? asiento : (int?)null;
            }
            catch (ClaveIdempotenciaRepetidaException)
            {
                // Dos peticiones a la vez con la misma clave: la otra ganó. Misma clave = misma respuesta.
                ResultadoReparto<EstadoEnvioRepartoDTO> repetida = await YaRegistrada(prueba.ClaveIdempotencia, prueba.EnvioId, prueba.Tipo).ConfigureAwait(false);
                return repetida ?? Conflicto("Esa entrega ya se está registrando. Vuelva a intentarlo.");
            }
            catch (NestoBusinessException ex)
            {
                // La contabilización del reembolso (sin cuenta, sin diario...): nada se ha guardado.
                return Invalido($"No se ha registrado {(prueba.Tipo == PruebaEntregaReparto.TIPO_ENTREGA ? "la entrega" : "la incidencia")} del envío {envio.Numero}: {ex.Message}");
            }
            return ResultadoReparto<EstadoEnvioRepartoDTO>.Bien(new EstadoEnvioRepartoDTO { EnvioId = envio.Numero, Estado = estado, UrlPago = prueba.UrlPago });
        }

        /// <summary>Misma clave = misma respuesta; la clave de otro envío o de otra operación, 409. Null si la clave es nueva.</summary>
        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> YaRegistrada(Guid clave, int envioId, string tipo)
        {
            PruebaEntregaReparto previa = await repositorio.LeerPorClave(clave).ConfigureAwait(false);
            if (previa == null)
            {
                return null;
            }
            if (previa.EnvioId != envioId || !string.Equals(previa.Tipo, tipo, StringComparison.OrdinalIgnoreCase))
            {
                return Conflicto($"Esa ClaveIdempotencia ya se usó para {(previa.Tipo == PruebaEntregaReparto.TIPO_ENTREGA ? "la entrega" : "una incidencia")} " +
                    $"del envío {previa.EnvioId}. Cada entrega o incidencia lleva su propia clave.");
            }
            return ResultadoReparto<EstadoEnvioRepartoDTO>.Bien(new EstadoEnvioRepartoDTO
            {
                EnvioId = envioId,
                Estado = tipo == PruebaEntregaReparto.TIPO_ENTREGA ? EnvioRepartoDTO.ESTADO_ENTREGADO : EnvioRepartoDTO.ESTADO_INCIDENTADO,
                // El enlace que ya se mandó: un reintento no crea otro.
                UrlPago = previa.UrlPago
            });
        }

        /// <summary>«Pago pedido 928000 de Nueva Visión», como el enlace que manda Nesto desde el pedido. Sin pedido, el envío.</summary>
        internal static string ConceptoEnlace(EnviosAgencia envio)
            => envio.Pedido.HasValue
                ? $"Pago pedido {envio.Pedido.Value} de Nueva Visión"
                : $"Pago envío {envio.Numero} de Nueva Visión";

        // Estado y detalle ANTERIORES (como CierreManualEnvio). Nunca la firma ni el DNI.
        private List<EnvioHistoria> Historia(EnviosAgencia envio, string observaciones, string usuario)
        {
            DateTime momento = ahora();
            return new List<EnvioHistoria>
            {
                new EnvioHistoria { NumeroEnvio = envio.Numero, Campo = "Estado", ValorAnterior = envio.Estado.ToString(), Observaciones = observaciones, Usuario = usuario, FechaModificacion = momento },
                new EnvioHistoria { NumeroEnvio = envio.Numero, Campo = "DetalleEstado", ValorAnterior = Recortar(envio.DetalleEstado?.Trim() ?? string.Empty, 100), Observaciones = observaciones, Usuario = usuario, FechaModificacion = momento }
            };
        }

        internal static string DetalleIncidencia(string motivo, string texto)
        {
            string detalle = "Reparto propio: " + Motivos[motivo] + (texto == null ? string.Empty : ". " + texto);
            return Recortar(detalle, 100);
        }

        private static string TextoCobro(string cobro, decimal? importe)
            => cobro == PruebaEntregaReparto.COBRO_NO_COBRADO ? "no cobrado"
            : $"{(cobro == PruebaEntregaReparto.COBRO_ENLACE ? "enlace de pago enviado de" : cobro.ToLowerInvariant())} {(importe ?? 0m).ToString("N2", new System.Globalization.CultureInfo("es-ES"))} €";

        internal static string EstadoTexto(short estado)
        {
            switch (estado)
            {
                case Constantes.Agencias.ESTADO_TRAMITADO: return EnvioRepartoDTO.ESTADO_TRAMITADO;
                case Constantes.Agencias.ESTADO_INCIDENTADO: return EnvioRepartoDTO.ESTADO_INCIDENTADO;
                case Constantes.Agencias.ESTADO_ENTREGADO: return EnvioRepartoDTO.ESTADO_ENTREGADO;
                default: return null;
            }
        }

        private static string NoEstaEnReparto(EnviosAgencia envio)
            => envio.Estado < Constantes.Agencias.ESTADO_TRAMITADO
                ? $"El envío {envio.Numero} todavía no está tramitado: tramítelo antes de repartirlo."
                : $"El envío {envio.Numero} ya no está en reparto (devuelto o cerrado a mano).";

        private static T ADto<T>(EnviosAgencia e, T dto) where T : EnvioRepartoDTO
        {
            dto.EnvioId = e.Numero;
            dto.Empresa = e.Empresa?.Trim();
            dto.Pedido = e.Pedido;
            dto.Cliente = e.Cliente?.Trim();
            dto.Contacto = e.Contacto?.Trim();
            dto.Nombre = e.Nombre?.Trim();
            dto.Direccion = e.Direccion?.Trim();
            dto.CodigoPostal = e.CodPostal?.Trim();
            dto.Poblacion = e.Poblacion?.Trim();
            dto.Telefono = e.Telefono?.Trim();
            dto.Movil = e.Movil?.Trim();
            string correo = e.Email?.Trim();
            dto.Correo = CorreoFacturacionTienda.EsCorreoValido(correo) ? correo : null;
            dto.Bultos = e.Bultos;
            dto.Reembolso = e.Reembolso > 0 ? e.Reembolso : 0m;
            dto.Estado = EstadoTexto(e.Estado);
            dto.Observaciones = e.Observaciones?.Trim();
            return dto;
        }

        /// <summary>El PNG de la firma, o null con el motivo (sin repetir el contenido: es un dato personal).</summary>
        internal static byte[] LeerPng(string base64, out string error)
        {
            error = null;
            string limpio = base64.Trim();
            int coma = limpio.IndexOf(',');
            if (limpio.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma > 0)
            {
                limpio = limpio.Substring(coma + 1);
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(limpio);
            }
            catch (FormatException)
            {
                error = "La firma no está en base64.";
                return null;
            }
            if (bytes.Length > MAXIMO_FIRMA_BYTES)
            {
                error = "La firma es demasiado grande (más de 1 MB).";
                return null;
            }
            if (bytes.Length < CabeceraPng.Length || !CabeceraPng.SequenceEqual(bytes.Take(CabeceraPng.Length)))
            {
                error = "La firma tiene que ser una imagen PNG.";
                return null;
            }
            return bytes;
        }

        private static string Limpio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        private static string Recortar(string valor, int maximo) => valor.Length <= maximo ? valor : valor.Substring(0, maximo);

        private static ResultadoReparto<EstadoEnvioRepartoDTO> Invalido(string mensaje)
            => ResultadoReparto<EstadoEnvioRepartoDTO>.Mal(TipoResultadoReparto.Invalido, mensaje);

        private static ResultadoReparto<EstadoEnvioRepartoDTO> Conflicto(string mensaje)
            => ResultadoReparto<EstadoEnvioRepartoDTO>.Mal(TipoResultadoReparto.Conflicto, mensaje);

        private static ResultadoReparto<EstadoEnvioRepartoDTO> NoEncontrado(int envioId)
            => ResultadoReparto<EstadoEnvioRepartoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe el envío {envioId} del reparto propio.");
    }
}
