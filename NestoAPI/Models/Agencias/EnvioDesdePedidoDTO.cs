using System.Collections.Generic;

namespace NestoAPI.Models.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 2, corte 4): la petición de POST api/EnviosAgencias/DesdePedido. El servidor parte de la
    /// propuesta (GET api/EnviosAgencias/Propuesta), le aplica los <see cref="Cambios"/> de la pantalla, pide lo que falte
    /// confirmar (409) y graba el envío como InsertarRegistro de Agencias de Nesto.
    /// </summary>
    public class CrearEnvioDesdePedidoDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Los bultos TOTALES del envío (en una ampliación, los del envío entero). Sin valor: los del packing de Ariadna o 1.</summary>
        public short? Bultos { get; set; }
        /// <summary>Obligatorio y mayor que 0 (#252).</summary>
        public decimal Peso { get; set; }
        /// <summary>Los códigos de los avisos «Confirmar» ya confirmados (<see cref="CodigosAvisoPropuestaEnvio"/>).</summary>
        public List<string> Confirmaciones { get; set; } = new List<string>();
        /// <summary>
        /// Aviso de prepagos (#569): true = descontarlos del reembolso, false = no descontarlos (o ya decidido en la
        /// pantalla), null = sin decidir (si hay aviso, 409).
        /// </summary>
        public bool? DescontarPrepagos { get; set; }
        /// <summary>Las dimensiones de los bultos (AnchoxAltoxLargo) para Canteras (Nesto#367). Van a las observaciones.</summary>
        public string Dimensiones { get; set; }
        /// <summary>
        /// El servicio elegido crea una etiqueta de recogida (IAgencia.ServicioCreaEtiquetaRetorno): eso sigue en Nesto (400
        /// <see cref="CodigosErrorEnvioDesdePedido.ETIQUETA_RECOGIDA"/>).
        /// </summary>
        public bool EtiquetaRecogida { get; set; }
        /// <summary>Lo que hay en la pantalla. Null en un campo = el de la propuesta.</summary>
        public CambiosEnvioDesdePedidoDTO Cambios { get; set; }
    }

    /// <summary>
    /// NestoAPI#595 (slice 2, corte 4): los valores de la pantalla de Agencias. Nesto los manda SIEMPRE todos (decisión de
    /// Carlos, 20/10/26); null = el de la propuesta. Qué se aplica depende del origen, como en InsertarRegistro:
    /// <list type="bullet">
    /// <item>Nuevo: todo.</item>
    /// <item>Ampliación: todo menos la agencia (la del envío que se amplía). El reembolso es el de ESTE pedido; si el envío
    /// era de otro pedido, se suma al que ya llevaba.</item>
    /// <item>Pendiente reutilizado: solo el servicio y el retorno en CTT e Innovatrans (Nesto#495). El destino y el reembolso
    /// son los del pendiente (Nesto#395).</item>
    /// </list>
    /// </summary>
    public class CambiosEnvioDesdePedidoDTO
    {
        public int? Agencia { get; set; }
        public short? Servicio { get; set; }
        public short? Horario { get; set; }
        public short? Retorno { get; set; }
        /// <summary>El reembolso de ESTE pedido (el de la pantalla), sin descontar prepagos si no se ha decidido.</summary>
        public decimal? Reembolso { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        /// <summary>El id de país de la agencia (34 España, 351 Portugal), como EnviosAgencia.Pais.</summary>
        public int? Pais { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        public string Email { get; set; }
        public string Atencion { get; set; }
        /// <summary>Se recortan a 80 caracteres.</summary>
        public string Observaciones { get; set; }
    }

    /// <summary>NestoAPI#595 (slice 2, corte 4): la respuesta 200 de DesdePedido. Siempre que se graba, 200.</summary>
    public class EnvioDesdePedidoDTO
    {
        /// <summary>El envío grabado, con su Numero, la plaza y el código de barras.</summary>
        public EnviosAgencia Envio { get; set; }
        /// <summary>«Nuevo», «PendienteReutilizado» o «Ampliacion» (<see cref="PropuestaEnvioDTO"/>).</summary>
        public string Origen { get; set; }
        /// <summary>El envío pendiente reutilizado o el ampliado; null si es nuevo.</summary>
        public int? EnvioOrigen { get; set; }
        /// <summary>False en una ampliación con los mismos bultos (no hay etiqueta nueva que imprimir), como InsertarRegistro.</summary>
        public bool HayQueImprimir { get; set; }
        /// <summary>Uno de <see cref="FlujosEnvioDesdePedido"/>.</summary>
        public string Flujo { get; set; }
        /// <summary>
        /// True si la etiqueta la monta Nesto en local con la plantilla de la agencia (GLS, Canteras). False en CTT e
        /// Innovatrans, cuya etiqueta da la agencia al registrar el envío (Nesto sigue con InsertarYEtiquetar).
        /// </summary>
        public bool EtiquetaEnNesto { get; set; }
        /// <summary>Se ha encolado el correo «Pedido entregado a la agencia» al cliente (solo envío nuevo de TramitarAlCerrar con código); sale en segundo plano.</summary>
        public bool CorreoEnviado { get; set; }
        /// <summary>Avisos informativos de la grabación (plaza de GLS sin contestar, cambios que no se aplican...).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#595 (slice 2, corte 4): la respuesta 409 de DesdePedido: lo que falta confirmar, elegir o indicar.</summary>
    public class ConflictoEnvioDesdePedidoDTO
    {
        public List<AvisoPropuestaEnvio> Pendientes { get; set; } = new List<AvisoPropuestaEnvio>();
        /// <summary>La propuesta con los cambios ya aplicados (lo que se grabaría).</summary>
        public PropuestaEnvioDTO Propuesta { get; set; }
    }

    /// <summary>NestoAPI#595 (slice 2, corte 4): la respuesta 400 de DesdePedido.</summary>
    public class ErrorEnvioDesdePedidoDTO
    {
        public string Message { get; set; }
        /// <summary>Uno de <see cref="CodigosErrorEnvioDesdePedido"/>.</summary>
        public string Codigo { get; set; }
    }

    /// <summary>NestoAPI#595 (slice 2, corte 4): cómo sigue el envío después de grabarlo (IAgencia.FlujoTramitacion de Nesto).</summary>
    public static class FlujosEnvioDesdePedido
    {
        /// <summary>GLS: la etiqueta se imprime en Nesto y el envío se manda a la agencia al tramitar.</summary>
        public const string TRAMITAR_AL_CERRAR = "TramitarAlCerrar";
        /// <summary>CTT e Innovatrans: imprimir la etiqueta ES registrar el envío en la agencia (/Tramitar).</summary>
        public const string REGISTRAR_AL_IMPRIMIR = "RegistrarAlImprimir";
        /// <summary>Canteras: aviso de recogida por correo y el nº de envío se pega a mano.</summary>
        public const string MANUAL = "Manual";
    }

    /// <summary>NestoAPI#595 (slice 2, corte 4): los códigos de los 400 de DesdePedido, para que el cliente sepa qué hacer.</summary>
    public static class CodigosErrorEnvioDesdePedido
    {
        public const string PESO_OBLIGATORIO = "PesoObligatorio";
        public const string SIN_AGENCIA = "SinAgencia";
        public const string CATALOGO_INVALIDO = "CatalogoInvalido";
        /// <summary>La agencia no tiene plaza ni código de barras en el servidor: el cliente sigue por su camino de siempre.</summary>
        public const string AGENCIA_NO_SOPORTADA = "AgenciaNoSoportada";
        public const string SIN_COBERTURA = "SinCobertura";
        /// <summary>Ampliación de un envío de CTT o Innovatrans que ya está registrado en la agencia.</summary>
        public const string AMPLIACION_REGISTRADA = "AmpliacionRegistrada";
        /// <summary>La etiqueta de recogida sigue en Nesto: el cliente sigue por su camino de siempre.</summary>
        public const string ETIQUETA_RECOGIDA = "EtiquetaRecogida";
        /// <summary>No se pudo calcular el código de barras (la agencia no tiene prefijo): no se graba nada.</summary>
        public const string SIN_CODIGO_BARRAS = "SinCodigoBarras";
        /// <summary>El envío que se iba a reutilizar o ampliar ha cambiado o ya no está.</summary>
        public const string ENVIO_CAMBIADO = "EnvioCambiado";
        public const string PETICION_INVALIDA = "PeticionInvalida";
    }
}
