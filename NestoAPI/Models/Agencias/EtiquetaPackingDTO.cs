using System.Collections.Generic;

namespace NestoAPI.Models.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 5, Ariadna#13): el cuerpo de POST api/Almacen/Pedidos/{pedido}/Etiqueta. Lo manda Ariadna al terminar
    /// la entrega en el packing. Los bultos NO van: son los de la entrega en el packing de Ariadna (cajas distintas con su foto).
    /// </summary>
    public class PeticionEtiquetaPackingDTO
    {
        /// <summary>Sin valor, la empresa por defecto.</summary>
        public string Empresa { get; set; }
        /// <summary>Obligatorio y mayor que 0 para grabar el envío (#252). En una reimpresión no se usa.</summary>
        public decimal Peso { get; set; }
        /// <summary>Sin valor, la de etiquetas del usuario (ParámetrosUsuario ImpresoraCodBarras).</summary>
        public string Impresora { get; set; }
        /// <summary>Los códigos de los avisos «Confirmar» del 409 ya confirmados (<see cref="CodigosAvisoPropuestaEnvio"/>).</summary>
        public List<string> Confirmaciones { get; set; } = new List<string>();
        /// <summary>Aviso de prepagos (#569): true descontarlos, false no, null sin decidir (si hay aviso, 409).</summary>
        public bool? DescontarPrepagos { get; set; }
        /// <summary>Las dimensiones de los bultos (AnchoxAltoxLargo) si la agencia es Canteras.</summary>
        public string Dimensiones { get; set; }
        /// <summary>
        /// NestoAPI#740 (sugerencia 869, 29/10/26): los bultos que dice el mozo al imprimir la etiqueta, como en la reposición
        /// (eligen la caja al final, al ver el tamaño). Si viene (mayor que 0) manda sobre las cajas cerradas del packing y no hace
        /// falta haber cerrado ninguna; sin él (o 0), como siempre: los bultos son las cajas cerradas. Negativo o más de
        /// <see cref="NestoAPI.Infraestructure.Agencias.ServicioEtiquetaPacking.MAXIMO_BULTOS"/>, 400.
        /// </summary>
        public int? Bultos { get; set; }
    }

    /// <summary>NestoAPI#595 (slice 5): la respuesta 200 (y la 502, con <see cref="Impresa"/> a false) de la etiqueta del packing.</summary>
    public class EtiquetaPackingDTO
    {
        public int Pedido { get; set; }
        /// <summary>El número del envío grabado (en la 502 también: el envío queda grabado y reintentar no lo duplica).</summary>
        public int Envio { get; set; }
        public int Agencia { get; set; }
        public string AgenciaNombre { get; set; }
        /// <summary>El albarán de la agencia (CTT, Innovatrans) o el código de barras del envío; null si aún no lo tiene.</summary>
        public string CodigoBarras { get; set; }
        public int Bultos { get; set; }
        /// <summary>Dónde se ha impreso (o se iba a imprimir).</summary>
        public string Impresora { get; set; }
        /// <summary>La etiqueta ha salido por la impresora.</summary>
        public bool Impresa { get; set; }
        /// <summary>El envío ya estaba grabado y registrado en la agencia: solo se ha vuelto a imprimir.</summary>
        public bool Reimpresion { get; set; }
        /// <summary>El envío se ha grabado en esta llamada (si no, ya existía de un intento anterior).</summary>
        public bool EnvioNuevo { get; set; }
        /// <summary>La etiqueta de esta agencia todavía se saca en Nesto (GLS hasta #552, Canteras).</summary>
        public bool EtiquetaEnNesto { get; set; }
        /// <summary>Se ha facturado el pedido al imprimir (ParámetrosUsuario FacturarAlImprimirEtiqueta, #592).</summary>
        public bool Facturado { get; set; }
        /// <summary>Lo que se enseña al usuario: el resultado o, en la 502, qué ha fallado.</summary>
        public string Message { get; set; }
        /// <summary>En la 502, uno de <see cref="CodigosEtiquetaPacking"/>.</summary>
        public string Codigo { get; set; }
        /// <summary>Avisos informativos (la grabación, la facturación, otros pedidos de la entrega...).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#595 (slice 5): los códigos propios de la etiqueta del packing (los de DesdePedido pasan tal cual).</summary>
    public static class CodigosEtiquetaPacking
    {
        /// <summary>400: la entrega no tiene ninguna caja cerrada en el packing de Ariadna.</summary>
        public const string SIN_BULTOS = "SinBultos";
        /// <summary>400 (NestoAPI#698/#734): el pedido está retenido (o esperando a Administración): no sale etiqueta.</summary>
        public const string PEDIDO_RETENIDO = "PedidoRetenido";
        /// <summary>
        /// 400: el pedido amplía un envío en curso de otro pedido del mismo cliente y dirección. Los bultos de una ampliación son
        /// los del envío entero y el packing solo sabe los de esta entrega: se hace desde Agencias de Nesto.
        /// </summary>
        public const string AMPLIACION = "Ampliacion";
        /// <summary>502: el envío está grabado, pero la agencia no lo ha registrado.</summary>
        public const string TRAMITAR_FALLIDO = "TramitarFallido";
        /// <summary>502: el envío está registrado en la agencia, pero la etiqueta no ha salido por la impresora.</summary>
        public const string IMPRIMIR_FALLIDO = "ImprimirFallido";
    }
}
