using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

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
    /// la cuenta de caja de la agencia 14). La firma y el DNI solo van a EnviosAgenciaPruebasEntrega: nunca a ELMAH ni a los
    /// mensajes ni a la historia.
    /// </summary>
    public class ServicioRepartoPropio
    {
        public const string MENSAJE_FALTA_SCRIPT = "Falta lanzar el script Scripts/Issue739_RepartoPropio.sql (reparto propio): " +
            "hasta entonces no se pueden registrar entregas ni incidencias. Avise a Informática.";
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
            { IncidenciaRepartoDTO.OTRO, "Otro" }
        };

        private static readonly string[] Cobros =
        {
            PruebaEntregaReparto.COBRO_EFECTIVO, PruebaEntregaReparto.COBRO_TARJETA, PruebaEntregaReparto.COBRO_NO_COBRADO
        };

        private readonly IRepositorioRepartoPropio repositorio;
        private readonly Func<DateTime> ahora;
        private readonly Func<EnviosAgencia, Task> avisarIncidencia;

        public ServicioRepartoPropio(IRepositorioRepartoPropio repositorio, Func<DateTime> ahora = null, Func<EnviosAgencia, Task> avisarIncidencia = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.avisarIncidencia = avisarIncidencia;
        }

        // ---------------------------------------------------------------- lecturas

        /// <summary>
        /// Los envíos en reparto (Tramitado o Incidentado) con fecha hasta <paramref name="fecha"/> (hoy por defecto) y desde
        /// 30 días antes: un incidentado de ayer sigue en la lista para reintentarlo.
        /// </summary>
        public async Task<ResultadoReparto<List<EnvioRepartoDTO>>> Envios(DateTime? fecha)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<List<EnvioRepartoDTO>>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            DateTime dia = (fecha ?? ahora()).Date;
            List<EnviosAgencia> envios = await repositorio.LeerEnviosEnReparto(dia.AddDays(-DIAS_ATRAS_LISTA), dia.AddDays(1)).ConfigureAwait(false)
                ?? new List<EnviosAgencia>();
            return ResultadoReparto<List<EnvioRepartoDTO>>.Bien(envios
                .Where(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && EstadoTexto(e.Estado) != null && e.Estado != Constantes.Agencias.ESTADO_ENTREGADO)
                .OrderBy(e => e.Fecha).ThenBy(e => e.Numero)
                .Select(e => ADto(e, new EnvioRepartoDTO()))
                .ToList());
        }

        /// <summary>El envío de un bulto escaneado (RP{envío}-{N}).</summary>
        public async Task<ResultadoReparto<EnvioRepartoBultoDTO>> Bulto(string codigo)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            if (!CodigoBultoRepartoPropio.TryLeerBulto(codigo, out int numero, out int bulto))
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.NoEncontrado,
                    $"El código «{codigo?.Trim()}» no es de un bulto del reparto propio (RP, el número del envío, guion y el bulto).");
            }
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

        public async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> Entregar(int envioId, EntregaRepartoDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la ClaveIdempotencia de la entrega.");
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
                cobro = Cobros.FirstOrDefault(c => string.Equals(c, peticion.CobroReembolso.Trim(), StringComparison.OrdinalIgnoreCase));
                if (cobro == null)
                {
                    return Invalido("El cobro del reembolso tiene que ser Efectivo, Tarjeta o NoCobrado.");
                }
            }
            if (peticion.ImporteCobrado.HasValue && peticion.ImporteCobrado.Value < 0)
            {
                return Invalido("El importe cobrado no puede ser negativo.");
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
            bool cobrado = cobro == PruebaEntregaReparto.COBRO_EFECTIVO || cobro == PruebaEntregaReparto.COBRO_TARJETA;
            decimal? importeCobrado = cobro == null ? peticion.ImporteCobrado
                : cobrado ? peticion.ImporteCobrado ?? reembolso
                : 0m;
            decimal? aContabilizar = null;
            if (cobrado && importeCobrado > 0 && MomentosContabilizarReembolso.De(envio.Agencia) == MomentoContabilizarReembolso.AlEntregar)
            {
                if (string.IsNullOrWhiteSpace(envio.AgenciasTransporte?.CuentaReembolsos))
                {
                    return Invalido("La agencia Reparto propio no tiene cuenta de caja (AgenciasTransporte.CuentaReembolsos): no se puede " +
                        "contabilizar el reembolso cobrado. Avise a Administración para que la ponga y vuelva a enviar la entrega.");
                }
                aContabilizar = importeCobrado;
            }

            DateTime fechaHora = peticion.FechaHora ?? ahora();
            var prueba = new PruebaEntregaReparto
            {
                EnvioId = envio.Numero,
                ClaveIdempotencia = peticion.ClaveIdempotencia,
                Tipo = PruebaEntregaReparto.TIPO_ENTREGA,
                FechaHora = fechaHora,
                Receptor = receptor,
                DniReceptor = dni,
                CobroReembolso = cobro,
                ImporteCobrado = importeCobrado,
                Texto = observaciones,
                Latitud = peticion.Latitud,
                Longitud = peticion.Longitud,
                Usuario = usuario,
                FechaRegistro = ahora(),
                TieneFirma = firma != null
            };
            string observacionesHistoria = "Reparto propio: entregado" + (cobro == null ? string.Empty : $" (reembolso: {TextoCobro(cobro, importeCobrado)})");
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

        public async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> Incidencia(int envioId, IncidenciaRepartoDTO peticion, string usuario)
        {
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
                return Invalido("El motivo de la incidencia tiene que ser Ausente, DireccionIncorrecta, Rechazado, FaltanBultos u Otro.");
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

        // ---------------------------------------------------------------- piezas

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
                return Invalido($"No se ha registrado la entrega del envío {envio.Numero}: {ex.Message}");
            }
            return ResultadoReparto<EstadoEnvioRepartoDTO>.Bien(new EstadoEnvioRepartoDTO { EnvioId = envio.Numero, Estado = estado });
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
                Estado = tipo == PruebaEntregaReparto.TIPO_ENTREGA ? EnvioRepartoDTO.ESTADO_ENTREGADO : EnvioRepartoDTO.ESTADO_INCIDENTADO
            });
        }

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
            : $"{cobro.ToLowerInvariant()} {(importe ?? 0m).ToString("N2", new System.Globalization.CultureInfo("es-ES"))} €";

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
