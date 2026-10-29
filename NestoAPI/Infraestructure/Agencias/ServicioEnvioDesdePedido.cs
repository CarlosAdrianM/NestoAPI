using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 2, cortes 4 a 6): crea el envío de agencia de un pedido en el servidor (POST
    /// api/EnviosAgencias/DesdePedido), con las mismas reglas que InsertarRegistro de Agencias de Nesto
    /// (AgenciasViewModel.vb). Parte de la propuesta (<see cref="IPropuestaEnvioService"/>), aplica los cambios de la
    /// pantalla, pide lo que falte confirmar, calcula la plaza fuera de la transacción, graba (nuevo, pendiente
    /// reutilizado o ampliación) con el código de barras y, tras el commit, avisa al cliente.
    /// </summary>
    public interface IServicioEnvioDesdePedido
    {
        /// <param name="usuario">El usuario de auditoría (del Identity).</param>
        Task<ResultadoEnvioDesdePedido> Crear(CrearEnvioDesdePedidoDTO peticion, string usuario);
    }

    public enum TipoResultadoEnvioDesdePedido
    {
        /// <summary>200: se ha grabado.</summary>
        Creado,
        /// <summary>409: falta confirmar, elegir o indicar algo.</summary>
        Conflicto,
        /// <summary>400.</summary>
        Invalido,
        /// <summary>404: el pedido no existe.</summary>
        NoEncontrado
    }

    public class ResultadoEnvioDesdePedido
    {
        public TipoResultadoEnvioDesdePedido Tipo { get; private set; }
        public EnvioDesdePedidoDTO Envio { get; private set; }
        public ConflictoEnvioDesdePedidoDTO Conflicto { get; private set; }
        public ErrorEnvioDesdePedidoDTO Error { get; private set; }

        public static ResultadoEnvioDesdePedido Creado(EnvioDesdePedidoDTO envio)
            => new ResultadoEnvioDesdePedido { Tipo = TipoResultadoEnvioDesdePedido.Creado, Envio = envio };

        public static ResultadoEnvioDesdePedido ConPendientes(ConflictoEnvioDesdePedidoDTO conflicto)
            => new ResultadoEnvioDesdePedido { Tipo = TipoResultadoEnvioDesdePedido.Conflicto, Conflicto = conflicto };

        public static ResultadoEnvioDesdePedido Invalido(string codigo, string mensaje)
            => new ResultadoEnvioDesdePedido
            {
                Tipo = TipoResultadoEnvioDesdePedido.Invalido,
                Error = new ErrorEnvioDesdePedidoDTO { Codigo = codigo, Message = mensaje }
            };

        public static ResultadoEnvioDesdePedido NoEncontrado()
            => new ResultadoEnvioDesdePedido { Tipo = TipoResultadoEnvioDesdePedido.NoEncontrado };
    }

    /// <summary>NestoAPI#595 (slice 2): lo que el alta desde el pedido lee y escribe en la BD, detrás de una interfaz para probarlo sin BD.</summary>
    public interface IDatosEnvioDesdePedido
    {
        /// <summary>La fila de AgenciasTransporte (empresa y prefijo del código de barras); null si no existe.</summary>
        Task<AgenciaTransporte> LeerAgencia(int numero);
        /// <summary>El envío pendiente o el que se amplía, con seguimiento de cambios (se modifica y se graba).</summary>
        Task<EnviosAgencia> LeerEnvioParaModificar(int numero);
        void Anadir(EnviosAgencia envio);
        Task GuardarCambios();
        /// <summary>Todo lo que se graba va junto o no va (rollback si el cuerpo lanza).</summary>
        Task<T> EnTransaccion<T>(Func<Task<T>> cuerpo);
        /// <summary>Todas las líneas del pedido son de la tienda online (no se avisa al cliente, como NotificarEntregaAgencia).</summary>
        Task<bool> EsTodoElPedidoOnline(string empresa, int pedido);
        /// <summary>El coste de esa agencia para el destino (null = sin tarifa para la zona), como GET Agencias/{numero}/Coste.</summary>
        OpcionEnvioAgencia CosteDeAgencia(string empresa, string codPostal, decimal peso, decimal reembolso, int agencia, byte? servicio, string paisIso);
        /// <summary>La agencia tiene tarifas en el comparador (las demás, como Canteras, no pasan por la red de cobertura).</summary>
        bool EsAgenciaDelComparador(int agencia);
    }

    /// <summary>NestoAPI#595 (slice 2): el correo «Pedido entregado a la agencia» al cliente.</summary>
    public interface INotificadorEntregaAgencia
    {
        Task Notificar(EnviosAgencia envio);
    }

    /// <summary>
    /// El de siempre (GestorEnviosAgencia.EnviarCorreoEntregaAgencia, lo que llama Nesto en NotificarEntregaAgencia), pero en un
    /// job de Hangfire: Carlos (20/10/26) no quiere que quien inserta el envío espere a que se genere el PDF y salga el correo.
    /// Se encola solo el número; el job relee el envío de la BD.
    /// </summary>
    public class NotificadorEntregaAgenciaCorreo : INotificadorEntregaAgencia
    {
        private readonly Action<int> encolar;

        /// <param name="encolar">Para los tests; por defecto, BackgroundJob.Enqueue del job.</param>
        public NotificadorEntregaAgenciaCorreo(Action<int> encolar = null)
        {
            this.encolar = encolar ?? (numero => Hangfire.BackgroundJob.Enqueue(() => CorreoEntregaAgenciaJobs.Enviar(numero)));
        }

        public Task Notificar(EnviosAgencia envio)
        {
            encolar(envio.Numero);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// NestoAPI#595 (slice 2): el job del correo «Pedido entregado a la agencia» de un envío creado desde el pedido. Si no sale
    /// (sin correo o fallo al mandarlo), se avisa en la campana de Nesto (buzón + SignalR) a quien insertó el envío, para que
    /// avise él al cliente: Carlos (20/10/26) prefiere que lo sepa el usuario en el momento a que se quede solo en ELMAH.
    /// Sin reintentos automáticos: un reintento podría mandar el correo dos veces.
    /// </summary>
    public static class CorreoEntregaAgenciaJobs
    {
        public const string TIPO_NOTIFICACION = "CorreoEntregaAgenciaFallido";
        private const string DOMINIO = "NUEVAVISION\\";

        [Hangfire.AutomaticRetry(Attempts = 0)]
        public static Task Enviar(int numeroEnvio)
        {
            var gestor = new GestorEnviosAgencia();
            return Enviar(numeroEnvio, gestor.LeerEnvioDeBd, gestor.EnviarCorreoEntregaAgencia,
                new Notificaciones.ServicioNotificacionesPush(), ex => ElmahHelper.Log(ex));
        }

        internal static async Task Enviar(int numeroEnvio, Func<int, EnviosAgencia> leer, Func<EnviosAgencia, Task> mandar,
            Notificaciones.IServicioNotificacionesPush notificaciones, Action<Exception> registrar)
        {
            EnviosAgencia envio = leer(numeroEnvio);
            if (envio == null)
            {
                return;
            }
            string motivo;
            if (string.IsNullOrWhiteSpace(envio.Email))
            {
                motivo = "el envío no tiene correo electrónico";
            }
            else
            {
                try
                {
                    // Solo llegan envíos nuevos de TramitarAlCerrar (GLS): no hace falta la agencia remota (es para la recogida de CTT)
                    await mandar(envio).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Envío desde pedido #595] No se pudo mandar el correo «Pedido entregado a la agencia» del envío {envio.Numero} (pedido {envio.Pedido}): {ex.Message}", ex));
                    motivo = ex.Message;
                }
            }
            await Avisar(envio, motivo, notificaciones, registrar).ConfigureAwait(false);
        }

        internal static NotificacionPushDTO Componer(EnviosAgencia envio, string motivo) => new NotificacionPushDTO
        {
            Titulo = $"No se ha avisado al cliente del pedido {envio.Pedido}",
            Cuerpo = $"El correo «Pedido entregado a la agencia» del cliente {envio.Cliente?.Trim()} (envío {envio.Numero}) no ha salido: {motivo}. Avise usted al cliente con el número de seguimiento {envio.CodigoBarras?.Trim()}.",
            Tipo = TIPO_NOTIFICACION,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = envio.Empresa?.Trim(),
                ["envio"] = envio.Numero.ToString(),
                ["pedido"] = envio.Pedido?.ToString(),
                ["cliente"] = envio.Cliente?.Trim()
            }
        };

        /// <summary>«NUEVAVISION\alfredo» o «alfredo» → «NUEVAVISION\alfredo»; null si el envío no tiene usuario o es de la propia API.</summary>
        internal static string UsuarioBuzon(string usuario)
        {
            string sinDominio = usuario?.Substring(usuario.IndexOf('\\') + 1).Trim();
            return string.IsNullOrWhiteSpace(sinDominio) || sinDominio == "NestoAPI" ? null : DOMINIO + sinDominio;
        }

        private static async Task Avisar(EnviosAgencia envio, string motivo, Notificaciones.IServicioNotificacionesPush notificaciones,
            Action<Exception> registrar)
        {
            string usuario = UsuarioBuzon(envio.Usuario);
            if (usuario == null)
            {
                registrar(new Exception($"[Envío desde pedido #595] El correo del envío {envio.Numero} no ha salido ({motivo}) y no hay a quién avisar."));
                return;
            }
            try
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, Componer(envio, motivo)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Envío desde pedido #595] No se pudo avisar a {usuario} de que el correo del envío {envio.Numero} no ha salido: {ex.Message}", ex));
            }
        }
    }

    public class ServicioEnvioDesdePedido : IServicioEnvioDesdePedido
    {
        internal const string MENSAJE_PESO_OBLIGATORIO = "Indique el peso del envío: es necesario para elegir la agencia más barata y para tramitar con la agencia.";
        internal const string MENSAJE_ETIQUETA_RECOGIDA = "Haga la etiqueta de recogida desde Nesto.";

        private readonly IPropuestaEnvioService propuestas;
        private readonly IDatosEnvioDesdePedido datos;
        private readonly RegistroPlazasAgencias plazas;
        private readonly INotificadorEntregaAgencia notificador;
        private readonly Action<Exception> registrar;

        public ServicioEnvioDesdePedido(IPropuestaEnvioService propuestas, IDatosEnvioDesdePedido datos, RegistroPlazasAgencias plazas,
            INotificadorEntregaAgencia notificador, Action<Exception> registrar = null)
        {
            this.propuestas = propuestas ?? throw new ArgumentNullException(nameof(propuestas));
            this.datos = datos ?? throw new ArgumentNullException(nameof(datos));
            this.plazas = plazas ?? throw new ArgumentNullException(nameof(plazas));
            this.notificador = notificador;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        // Sin ConfigureAwait(false): ELMAH toma el usuario de HttpContext.Current.
        public async Task<ResultadoEnvioDesdePedido> Crear(CrearEnvioDesdePedidoDTO peticion, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Empresa) || peticion.Pedido <= 0)
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.PETICION_INVALIDA, "Hay que indicar la empresa y el pedido.");
            }
            // #252 (VB InsertarEnvioAsync): sin peso no se elige agencia ni se tramita.
            if (peticion.Peso <= 0m)
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.PESO_OBLIGATORIO, MENSAJE_PESO_OBLIGATORIO);
            }
            // Decisión de Carlos (20/10/26): la etiqueta de recogida sigue en Nesto, como hoy.
            if (peticion.EtiquetaRecogida)
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.ETIQUETA_RECOGIDA, MENSAJE_ETIQUETA_RECOGIDA);
            }

            string empresa = peticion.Empresa.Trim();

            // 1. La propuesta.
            PropuestaEnvioDTO propuesta = await propuestas.Calcular(empresa, peticion.Pedido, peticion.Bultos, peticion.Peso);
            if (propuesta == null)
            {
                return ResultadoEnvioDesdePedido.NoEncontrado();
            }

            EnviosAgencia existente = null;
            if (propuesta.Origen != PropuestaEnvioDTO.ORIGEN_NUEVO)
            {
                existente = propuesta.EnvioOrigen.HasValue ? await datos.LeerEnvioParaModificar(propuesta.EnvioOrigen.Value) : null;
                if (existente == null)
                {
                    return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.ENVIO_CAMBIADO,
                        $"El envío {propuesta.EnvioOrigen} ya no está. Vuelva a intentarlo.");
                }
            }

            // 2. Los cambios de la pantalla.
            var avisos = new List<string>();
            var antes = Huella.De(propuesta, ReembolsoDelPedido(propuesta, existente, peticion.Pedido));
            decimal reembolsoPedido = AplicarCambios(propuesta, peticion.Cambios, existente, peticion.Pedido, avisos, antes.ReembolsoPedido);
            if (peticion.DescontarPrepagos == true && propuesta.PrepagosPendientes > 0m)
            {
                // #569 (VB ReembolsoDescontandoPrepagos): lo que ya pagó el cliente sale del reembolso de ESTE pedido.
                reembolsoPedido = Math.Max(0m, reembolsoPedido - propuesta.PrepagosPendientes);
            }
            propuesta.Reembolso = ReembolsoDelEnvio(propuesta, existente, peticion.Pedido, reembolsoPedido);

            ResultadoEnvioDesdePedido invalido = await Validar(propuesta, existente, antes, empresa);
            if (invalido != null)
            {
                return invalido;
            }

            if (antes.CambiaElCoste(propuesta, reembolsoPedido))
            {
                RecalcularImporteGasto(propuesta, existente, empresa, reembolsoPedido);
            }

            // 3. Las confirmaciones.
            PropuestaEnvioService.RecalcularAvisosDeDestino(propuesta);
            List<AvisoPropuestaEnvio> pendientes = Pendientes(propuesta, peticion);
            if (pendientes.Count > 0)
            {
                return ResultadoEnvioDesdePedido.ConPendientes(new ConflictoEnvioDesdePedidoDTO { Pendientes = pendientes, Propuesta = propuesta });
            }

            // Nesto#367: las dimensiones de Canteras van a las observaciones (decisión de Carlos: se piden aquí).
            if (propuesta.Agencia == Constantes.Agencias.AGENCIA_CANTERAS)
            {
                propuesta.Observaciones = PropuestaEnvioService.ObservacionesConDimensiones(propuesta.Observaciones, peticion.Dimensiones);
            }

            // 4. La plaza, fuera de la transacción (GLS puede tardar hasta 15 s). Si GLS no contesta, sin plaza, como VB.
            IPlazaYCodigoBarrasAgencia agencia = plazas.Para(propuesta.Agencia);
            PlazaAgencia plaza = await agencia.CalcularPlazaAsync(propuesta.CodPostal, propuesta.Pais) ?? PlazaAgencia.Vacia();
            if (plaza.EsVacia && agencia.PlazaConsultaLaAgencia)
            {
                avisos.Add($"La agencia no ha dado la plaza del CP {propuesta.CodPostal}: el envío se graba sin plaza.");
            }
            AgenciaTransporte filaAgencia = await datos.LeerAgencia(propuesta.Agencia);

            // 5. La transacción, como TramitacionEnviosService: grabar, código de barras con el Numero y commit.
            short bultosAntes = existente?.Bultos ?? 0;
            EnviosAgencia envio;
            try
            {
                envio = await datos.EnTransaccion(async () =>
                {
                    EnviosAgencia grabado = Grabar(propuesta, existente, filaAgencia, plaza, usuario);
                    await datos.GuardarCambios();
                    string codigo = agencia.CalcularCodigoBarras(grabado, filaAgencia?.PrefijoCodigoBarras);
                    if (codigo == null)
                    {
                        throw new SinCodigoBarrasException(propuesta.Agencia);
                    }
                    // CTT, Innovatrans y Canteras no lo calculan (""): no se pisa uno que ya tuviera.
                    if (codigo.Length > 0 || string.IsNullOrWhiteSpace(grabado.CodigoBarras))
                    {
                        grabado.CodigoBarras = codigo;
                    }
                    await datos.GuardarCambios();
                    return grabado;
                });
            }
            catch (SinCodigoBarrasException ex)
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.SIN_CODIGO_BARRAS, ex.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.ENVIO_CAMBIADO,
                    $"El envío {propuesta.EnvioOrigen} ha cambiado mientras se grababa. Vuelva a intentarlo.");
            }

            string flujo = FlujoDe(envio.Agencia);
            var resultado = new EnvioDesdePedidoDTO
            {
                Envio = envio,
                Origen = propuesta.Origen,
                EnvioOrigen = propuesta.EnvioOrigen,
                // VB InsertarRegistro: en una ampliación con los mismos bultos no hay etiqueta nueva.
                HayQueImprimir = propuesta.Origen != PropuestaEnvioDTO.ORIGEN_AMPLIACION || bultosAntes != envio.Bultos,
                Flujo = flujo,
                EtiquetaEnNesto = flujo != FlujosEnvioDesdePedido.REGISTRAR_AL_IMPRIMIR,
                Avisos = avisos
            };

            // 6. El aviso al cliente, después del commit y sin romper nunca la respuesta.
            resultado.CorreoEnviado = await AvisarAlCliente(envio, propuesta.Origen, flujo, empresa, peticion.Pedido);
            return ResultadoEnvioDesdePedido.Creado(resultado);
        }

        // ---------------------------------------------------------------- cambios

        /// <summary>
        /// Aplica a la propuesta los valores de la pantalla según el origen (ver <see cref="CambiosEnvioDesdePedidoDTO"/>).
        /// Devuelve el reembolso de ESTE pedido.
        /// </summary>
        private static decimal AplicarCambios(PropuestaEnvioDTO propuesta, CambiosEnvioDesdePedidoDTO cambios, EnviosAgencia existente,
            int pedido, List<string> avisos, decimal reembolsoPedido)
        {
            if (cambios == null)
            {
                return reembolsoPedido;
            }

            bool cambiaAgencia = cambios.Agencia.HasValue && cambios.Agencia.Value > 0 && cambios.Agencia.Value != propuesta.Agencia;

            if (propuesta.Origen == PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO)
            {
                // VB 3505-3517: del pendiente solo cambian el estado, los bultos, el peso, el ImporteGasto y, en las agencias
                // gestionadas por la API, el servicio y el retorno de la pantalla (Nesto#495). El destino es el del pendiente.
                if (cambiaAgencia)
                {
                    avisos.Add($"El envío pendiente {propuesta.EnvioOrigen} sigue por su agencia ({propuesta.AgenciaNombre}): el cambio de agencia no se aplica.");
                }
                else if (EsGestionadaPorApi(propuesta.Agencia))
                {
                    propuesta.Servicio = cambios.Servicio ?? propuesta.Servicio;
                    propuesta.Retorno = cambios.Retorno ?? propuesta.Retorno;
                }
                if (DestinoDistinto(propuesta, cambios))
                {
                    avisos.Add($"El envío pendiente {propuesta.EnvioOrigen} conserva su destino: los cambios de dirección de la pantalla no se aplican.");
                }
                return reembolsoPedido;
            }

            if (propuesta.Origen == PropuestaEnvioDTO.ORIGEN_AMPLIACION)
            {
                // VB 3439-3441 (Nesto#412): en una ampliación la agencia es la del envío que se amplía.
                if (cambiaAgencia)
                {
                    avisos.Add($"El envío {propuesta.EnvioOrigen} que se amplía sigue por su agencia ({propuesta.AgenciaNombre}): el cambio de agencia no se aplica.");
                }
            }
            else if (cambiaAgencia)
            {
                propuesta.Agencia = cambios.Agencia.Value;
                propuesta.AgenciaNombre = null;
            }

            // VB 3521-3549: nuevo y ampliación graban lo que hay en la pantalla.
            propuesta.Servicio = cambios.Servicio ?? propuesta.Servicio;
            propuesta.Horario = cambios.Horario ?? propuesta.Horario;
            propuesta.Retorno = cambios.Retorno ?? propuesta.Retorno;
            propuesta.Nombre = cambios.Nombre != null ? cambios.Nombre.Replace("\"", string.Empty) : propuesta.Nombre;
            propuesta.Direccion = cambios.Direccion != null ? cambios.Direccion.Replace("\"", string.Empty) : propuesta.Direccion;
            propuesta.CodPostal = cambios.CodPostal?.Trim() ?? propuesta.CodPostal;
            propuesta.Poblacion = cambios.Poblacion ?? propuesta.Poblacion;
            propuesta.Provincia = cambios.Provincia ?? propuesta.Provincia;
            if (cambios.Pais.HasValue && cambios.Pais.Value > 0)
            {
                propuesta.Pais = cambios.Pais.Value;
            }
            propuesta.PaisIso = PropuestaEnvioService.PaisIsoDe(propuesta.Pais, propuesta.CodPostal);
            propuesta.Telefono = cambios.Telefono ?? propuesta.Telefono;
            propuesta.Movil = cambios.Movil ?? propuesta.Movil;
            propuesta.Email = cambios.Email ?? propuesta.Email;
            propuesta.Atencion = cambios.Atencion ?? propuesta.Atencion;
            propuesta.Observaciones = cambios.Observaciones != null
                ? PropuestaEnvioService.ObservacionesDeLaPantalla(cambios.Observaciones)
                : propuesta.Observaciones;
            return cambios.Reembolso ?? reembolsoPedido;
        }

        private static bool DestinoDistinto(PropuestaEnvioDTO propuesta, CambiosEnvioDesdePedidoDTO cambios)
            => Distinto(cambios.Nombre, propuesta.Nombre) || Distinto(cambios.Direccion, propuesta.Direccion)
               || Distinto(cambios.CodPostal, propuesta.CodPostal) || Distinto(cambios.Poblacion, propuesta.Poblacion);

        private static bool Distinto(string pantalla, string propuesta)
            => pantalla != null && !string.Equals(pantalla.Trim(), propuesta?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        /// <summary>El reembolso de este pedido que trae la propuesta (en una ampliación de otro pedido, sin lo que ya llevaba el envío).</summary>
        private static decimal ReembolsoDelPedido(PropuestaEnvioDTO propuesta, EnviosAgencia existente, int pedido)
            => EsAmpliacionDeOtroPedido(propuesta, existente, pedido) ? propuesta.Reembolso - existente.Reembolso : propuesta.Reembolso;

        /// <summary>VB 3542: en una ampliación de OTRO pedido se suma el reembolso al que ya llevaba el envío.</summary>
        private static decimal ReembolsoDelEnvio(PropuestaEnvioDTO propuesta, EnviosAgencia existente, int pedido, decimal reembolsoPedido)
            => EsAmpliacionDeOtroPedido(propuesta, existente, pedido) ? existente.Reembolso + reembolsoPedido : reembolsoPedido;

        private static bool EsAmpliacionDeOtroPedido(PropuestaEnvioDTO propuesta, EnviosAgencia existente, int pedido)
            => propuesta.Origen == PropuestaEnvioDTO.ORIGEN_AMPLIACION && existente != null && existente.Pedido != pedido;

        // ---------------------------------------------------------------- validaciones (400)

        private async Task<ResultadoEnvioDesdePedido> Validar(PropuestaEnvioDTO propuesta, EnviosAgencia existente, Huella antes, string empresa)
        {
            // VB 2407-2409: sin agencia con tarifa para el destino no se puede tramitar.
            if (propuesta.Agencia <= 0)
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.SIN_AGENCIA,
                    $"Ninguna agencia tiene tarifa para la zona del destino (CP {propuesta.CodPostal}). No se puede tramitar el envío.");
            }
            if (!plazas.Soporta(propuesta.Agencia))
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.AGENCIA_NO_SOPORTADA,
                    $"La agencia {propuesta.Agencia} no se puede insertar desde el servidor.");
            }
            if (propuesta.Agencia != antes.Agencia)
            {
                // VB 3520: la empresa del envío es la de la agencia.
                AgenciaTransporte nueva = await datos.LeerAgencia(propuesta.Agencia);
                if (nueva == null)
                {
                    return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.AGENCIA_NO_SOPORTADA,
                        $"La agencia {propuesta.Agencia} no existe.");
                }
                propuesta.AgenciaNombre = nueva.Nombre?.Trim();
                if (!string.IsNullOrWhiteSpace(nueva.Empresa))
                {
                    propuesta.Empresa = nueva.Empresa.Trim();
                }
            }

            // NestoAPI#546: el servicio, el horario y el retorno tienen que ser de la agencia.
            string errorCatalogo = CatalogoServiciosAgencias.Validar(propuesta.Agencia, propuesta.Servicio, propuesta.Horario, propuesta.Retorno);
            if (errorCatalogo != null)
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.CATALOGO_INVALIDO, errorCatalogo);
            }

            // Red de cobertura (ValidarCoberturaTarifa / VB AsegurarCoberturaDestinoAsync): si la pantalla cambia la agencia o
            // el destino, la agencia del comparador tiene que tener tarifa para la zona. La propuesta ya la tiene.
            bool cambiaDestino = propuesta.Agencia != antes.Agencia || !string.Equals(propuesta.CodPostal?.Trim(), antes.CodPostal?.Trim(), StringComparison.OrdinalIgnoreCase)
                || propuesta.PaisIso != antes.PaisIso;
            if (cambiaDestino && datos.EsAgenciaDelComparador(propuesta.Agencia) &&
                datos.CosteDeAgencia(empresa, propuesta.CodPostal, 0m, 0m, propuesta.Agencia, null, propuesta.PaisIso) == null)
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.SIN_COBERTURA,
                    $"La agencia seleccionada no tiene tarifa para la zona del destino (CP {propuesta.CodPostal}). No se puede crear el envío.");
            }

            // Decisión de Carlos (20/10/26): ampliar un envío de CTT o Innovatrans que ya está registrado en la agencia
            // dejaría la agencia con los bultos de antes (y en Nesto, VB l.3695 borraba el albarán).
            if (propuesta.Origen == PropuestaEnvioDTO.ORIGEN_AMPLIACION && EsGestionadaPorApi(propuesta.Agencia) &&
                !string.IsNullOrWhiteSpace(existente?.CodigoBarras))
            {
                return ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.AMPLIACION_REGISTRADA,
                    $"El envío {existente.Numero} ya está registrado en la agencia (albarán {existente.CodigoBarras.Trim()}): " +
                    "anule el envío y vuelva a insertarlo.");
            }
            return null;
        }

        private void RecalcularImporteGasto(PropuestaEnvioDTO propuesta, EnviosAgencia existente, string empresa, decimal reembolsoPedido)
        {
            decimal coste = 0m;
            try
            {
                coste = datos.CosteDeAgencia(empresa, propuesta.CodPostal, propuesta.Peso, reembolsoPedido, propuesta.Agencia,
                    PropuestaEnvioService.ServicioForzadoParaCoste(propuesta.Agencia, propuesta.Servicio, propuesta.CodPostal),
                    propuesta.PaisIso)?.Coste ?? 0m;
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"Envío desde el pedido {propuesta.Pedido}: no se pudo calcular el coste de la agencia {propuesta.Agencia}. {ex.Message}", ex));
            }
            // VB 3513-3517 (pendiente): el coste real si se tiene; si no, el que tuviera.
            propuesta.ImporteGasto = propuesta.Origen == PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO && coste <= 0m
                ? existente?.ImporteGasto ?? 0m
                : coste;
        }

        // ---------------------------------------------------------------- confirmaciones (409)

        private static List<AvisoPropuestaEnvio> Pendientes(PropuestaEnvioDTO propuesta, CrearEnvioDesdePedidoDTO peticion)
        {
            var confirmados = new HashSet<string>((peticion.Confirmaciones ?? new List<string>()).Where(c => c != null).Select(c => c.Trim()),
                StringComparer.OrdinalIgnoreCase);
            var pendientes = new List<AvisoPropuestaEnvio>();
            foreach (AvisoPropuestaEnvio aviso in propuesta.AvisosDetalle)
            {
                bool resuelto;
                switch (aviso.Tipo)
                {
                    case TiposAvisoPropuestaEnvio.CONFIRMAR:
                        resuelto = confirmados.Contains(aviso.Codigo);
                        break;
                    case TiposAvisoPropuestaEnvio.ELEGIR:
                        resuelto = peticion.DescontarPrepagos.HasValue;
                        break;
                    case TiposAvisoPropuestaEnvio.DATO:
                        resuelto = PropuestaEnvioService.EsFormatoDimensionesValido(peticion.Dimensiones);
                        break;
                    default:
                        // Bloqueante (sin agencia): ya ha dado 400. NestoAPI#734: la consulta a Administración abierta NO se da
                        // por resuelta nunca (el pedido espera su respuesta).
                        resuelto = !string.Equals(aviso.Codigo, CodigosAvisoPropuestaEnvio.CONSULTA_REEMBOLSO_ABIERTA, StringComparison.OrdinalIgnoreCase);
                        break;
                }
                if (!resuelto)
                {
                    pendientes.Add(aviso);
                }
            }
            return pendientes;
        }

        // ---------------------------------------------------------------- grabar

        /// <summary>VB InsertarRegistro (l.3612-3672): el envío tal como lo graba Nesto según el origen.</summary>
        private EnviosAgencia Grabar(PropuestaEnvioDTO propuesta, EnviosAgencia existente, AgenciaTransporte filaAgencia, PlazaAgencia plaza, string usuario)
        {
            EnviosAgencia envio;
            if (propuesta.Origen == PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO)
            {
                envio = existente;
                envio.Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO;
                envio.Bultos = propuesta.Bultos;
                envio.Servicio = propuesta.Servicio;
                envio.Retorno = propuesta.Retorno;
                envio.Peso = propuesta.Peso;
                if (propuesta.ImporteGasto > 0m)
                {
                    envio.ImporteGasto = propuesta.ImporteGasto;
                }
                // #569: si se descuentan los prepagos, también del reembolso del pendiente.
                if (propuesta.Reembolso >= 0m && propuesta.Reembolso != envio.Reembolso && envio.Reembolso > 0m)
                {
                    envio.Reembolso = propuesta.Reembolso;
                }
            }
            else
            {
                envio = existente ?? new EnviosAgencia();
                envio.Empresa = string.IsNullOrWhiteSpace(filaAgencia?.Empresa) ? propuesta.Empresa : filaAgencia.Empresa;
                envio.Agencia = propuesta.Agencia;
                envio.Cliente = propuesta.Cliente;
                envio.Contacto = propuesta.Contacto;
                envio.Pedido = propuesta.Pedido;
                envio.Fecha = propuesta.Fecha;
                envio.FechaEntrega = propuesta.FechaEntrega;
                envio.Servicio = propuesta.Servicio;
                envio.Horario = propuesta.Horario;
                envio.Bultos = propuesta.Bultos;
                envio.Retorno = propuesta.Retorno;
                envio.Nombre = propuesta.Nombre ?? string.Empty;
                envio.Direccion = propuesta.Direccion ?? string.Empty;
                envio.CodPostal = propuesta.CodPostal ?? string.Empty;
                envio.Poblacion = propuesta.Poblacion ?? string.Empty;
                envio.Provincia = propuesta.Provincia ?? string.Empty;
                envio.Pais = propuesta.Pais;
                envio.Telefono = propuesta.Telefono ?? string.Empty;
                envio.Movil = propuesta.Movil ?? string.Empty;
                envio.Email = propuesta.Email ?? string.Empty;
                envio.Observaciones = propuesta.Observaciones ?? string.Empty;
                envio.Atencion = propuesta.Atencion ?? string.Empty;
                envio.Reembolso = propuesta.Reembolso;
                envio.Vendedor = propuesta.Vendedor;
                envio.Peso = propuesta.Peso;
                envio.ImporteGasto = propuesta.ImporteGasto;
                if (existente == null)
                {
                    envio.Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO;
                }
            }

            // VB 3667-3671: la plaza de la agencia (si no la da, se quedan los campos como estaban).
            if (!plaza.EsVacia)
            {
                envio.Nemonico = plaza.Nemonico;
                envio.NombrePlaza = plaza.Nombre;
                envio.TelefonoPlaza = PlazaAgencia.RecortarTelefono(plaza.Telefono);
                envio.EmailPlaza = plaza.Email;
            }

            // Como el POST y el PUT: NestoAPI#596 (CP canónico), textos libres a 80 y el usuario del Identity.
            envio.CodPostal = EnviosAgenciasController.CodigoPostalEnvio(envio.CodPostal, Direcciones.CodigoPostal.PaisIso(envio.Pais));
            EnviosAgenciasController.RecortarTextosLibres(envio);
            envio.Usuario = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario;
            envio.FechaModificacion = DateTime.Now;

            if (existente == null)
            {
                datos.Anadir(envio);
            }
            return envio;
        }

        // ---------------------------------------------------------------- aviso al cliente

        /// <summary>
        /// VB NotificarEntregaAgencia: solo un envío NUEVO de las agencias clásicas (en CTT e Innovatrans el código lo da la
        /// agencia al registrar), con código y si el pedido no es todo de la tienda online. El pendiente reutilizado no avisa,
        /// como hoy (decisión de Carlos, 20/10/26).
        /// </summary>
        private async Task<bool> AvisarAlCliente(EnviosAgencia envio, string origen, string flujo, string empresa, int pedido)
        {
            if (notificador == null || origen != PropuestaEnvioDTO.ORIGEN_NUEVO || flujo != FlujosEnvioDesdePedido.TRAMITAR_AL_CERRAR ||
                string.IsNullOrWhiteSpace(envio.CodigoBarras))
            {
                return false;
            }
            try
            {
                if (await datos.EsTodoElPedidoOnline(empresa, pedido))
                {
                    return false;
                }
                await notificador.Notificar(envio);
                return true;
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"Envío {envio.Numero} (pedido {pedido}): se ha grabado, pero no se pudo avisar al cliente. {ex.Message}", ex));
                return false;
            }
        }

        // ---------------------------------------------------------------- piezas

        /// <summary>CTT e Innovatrans: registrar al imprimir (IPerfilConGestionRemota en el servidor, AgenciaGestionadaPorApi en Nesto).</summary>
        internal static bool EsGestionadaPorApi(int agencia)
            => agencia == Constantes.Agencias.AGENCIA_CTT || agencia == Constantes.Agencias.AGENCIA_INNOVATRANS;

        /// <summary>IAgencia.FlujoTramitacion de Nesto (Canteras, que allí es TramitarAlCerrar, aquí es Manual: su nº llega por correo).</summary>
        internal static string FlujoDe(int agencia)
        {
            if (EsGestionadaPorApi(agencia))
            {
                return FlujosEnvioDesdePedido.REGISTRAR_AL_IMPRIMIR;
            }
            return agencia == Constantes.Agencias.AGENCIA_CANTERAS ? FlujosEnvioDesdePedido.MANUAL : FlujosEnvioDesdePedido.TRAMITAR_AL_CERRAR;
        }

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // El registro nunca rompe la operación.
            }
        }

        /// <summary>Lo que trae la propuesta antes de los cambios, para saber si hay que recalcular el coste y la cobertura.</summary>
        private sealed class Huella
        {
            public int Agencia { get; private set; }
            public string CodPostal { get; private set; }
            public string PaisIso { get; private set; }
            public short Servicio { get; private set; }
            public decimal ReembolsoPedido { get; private set; }

            public static Huella De(PropuestaEnvioDTO propuesta, decimal reembolsoPedido) => new Huella
            {
                Agencia = propuesta.Agencia,
                CodPostal = propuesta.CodPostal,
                PaisIso = propuesta.PaisIso,
                Servicio = propuesta.Servicio,
                ReembolsoPedido = reembolsoPedido
            };

            public bool CambiaElCoste(PropuestaEnvioDTO propuesta, decimal reembolsoPedido)
                => propuesta.Agencia != Agencia || propuesta.PaisIso != PaisIso || propuesta.Servicio != Servicio
                   || reembolsoPedido != ReembolsoPedido
                   || !string.Equals(propuesta.CodPostal?.Trim(), CodPostal?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private sealed class SinCodigoBarrasException : Exception
        {
            public SinCodigoBarrasException(int agencia)
                : base($"No se puede calcular el código de barras: la agencia {agencia} no tiene prefijo. No se ha grabado nada.")
            {
            }
        }
    }

    /// <summary>NestoAPI#595 (slice 2): los datos del alta desde el pedido en la BD.</summary>
    public class DatosEnvioDesdePedidoEF : IDatosEnvioDesdePedido
    {
        private readonly NVEntities db;
        private readonly IAmbitoTransaccion transaccion;
        private ComparadorAgencias comparador;

        public DatosEnvioDesdePedidoEF(NVEntities db, IAmbitoTransaccion transaccion = null)
        {
            this.db = db;
            this.transaccion = transaccion ?? new AmbitoTransaccionDb();
        }

        // Where + ToListAsync (no FirstOrDefaultAsync con predicado): ver el «Limit1» de TramitacionEnviosService.
        public async Task<AgenciaTransporte> LeerAgencia(int numero)
            => (await db.AgenciasTransportes.AsNoTracking().Where(a => a.Numero == numero).ToListAsync()).FirstOrDefault();

        public async Task<EnviosAgencia> LeerEnvioParaModificar(int numero)
            => (await db.EnviosAgencias.Where(e => e.Numero == numero).ToListAsync()).FirstOrDefault();

        public void Anadir(EnviosAgencia envio) => db.EnviosAgencias.Add(envio);

        public Task GuardarCambios() => db.SaveChangesAsync();

        public Task<T> EnTransaccion<T>(Func<Task<T>> cuerpo) => transaccion.EjecutarAsync(db, cuerpo);

        public async Task<bool> EsTodoElPedidoOnline(string empresa, int pedido)
            => (await new GestorPedidoParaAgencia(db).LeerSituacionLineas(empresa, pedido)).EsTodoOnline;

        public OpcionEnvioAgencia CosteDeAgencia(string empresa, string codPostal, decimal peso, decimal reembolso, int agencia, byte? servicio, string paisIso)
            => Comparador().CosteDeAgencia(empresa, codPostal, peso, reembolso, agencia, servicio, string.IsNullOrWhiteSpace(paisIso) ? "ES" : paisIso);

        public bool EsAgenciaDelComparador(int agencia) => new RegistroTarifas().Todas().Any(t => t.AgenciaId == agencia);

        private ComparadorAgencias Comparador() => comparador ?? (comparador = ComparadorAgenciasFactory.ParaSeleccion(db));
    }
}
