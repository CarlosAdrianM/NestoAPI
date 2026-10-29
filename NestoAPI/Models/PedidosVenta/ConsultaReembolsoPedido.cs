using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#734: una fila de ConsultasReembolsoPedido. El almacén retiene un pedido contra reembolso que tiene un enlace de
    /// pago pendiente y pregunta a Administración si sale con o sin reembolso. Abierta mientras <see cref="Respondida"/> es null.
    /// Contrato con Nesto (Nesto#568) y Ariadna (Ariadna#60): los nombres se serializan tal cual (sin camelCase).
    /// </summary>
    public class ConsultaReembolsoDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Lo pendiente en enlaces de pago vivos al preguntar (foto del momento).</summary>
        public decimal ImporteEnlaces { get; set; }
        /// <summary>Cuántos enlaces de pago vivos había al preguntar.</summary>
        public int NumeroEnlaces { get; set; }
        /// <summary>La retención (PedidosRetenidos.Id) que puso la consulta; null si el pedido ya estaba retenido por otra cosa.</summary>
        public int? RetencionId { get; set; }
        /// <summary>Lo que añade quien pregunta (opcional).</summary>
        public string Comentario { get; set; }
        /// <summary>Quien preguntó (NUEVAVISION\Alfredo desde Nesto; el usuario de Ariadna desde la PDA).</summary>
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>Cuándo contestó Administración; null = abierta.</summary>
        public DateTime? Respondida { get; set; }
        public string UsuarioRespuesta { get; set; }
        /// <summary>La decisión: true = sale con reembolso; false = sale sin reembolso; null mientras está abierta.</summary>
        public bool? ConReembolso { get; set; }
        /// <summary>Si Administración pidió anular los enlaces pendientes.</summary>
        public bool? AnularEnlaces { get; set; }
        /// <summary>Los Id de PagosTPV anulados, separados por comas (null si ninguno).</summary>
        public string EnlacesAnulados { get; set; }
        /// <summary>Con «sin reembolso»: la forma de pago y los plazos que tenía el pedido y los que se le han puesto.</summary>
        public string FormaPagoAnterior { get; set; }
        public string PlazosPagoAnterior { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        /// <summary>Lo que añade Administración al contestar (opcional).</summary>
        public string ComentarioRespuesta { get; set; }

        /// <summary>Sin contestar todavía.</summary>
        public bool Abierta => !Respondida.HasValue;
    }

    /// <summary>NestoAPI#734: una consulta con el cliente del pedido y, si está abierta, sus enlaces pendientes de ahora (GET ConsultasReembolso).</summary>
    public class ConsultaReembolsoListadoDTO : ConsultaReembolsoDTO
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string Vendedor { get; set; }
        /// <summary>Solo en las abiertas: los enlaces de pago pendientes vivos del pedido ahora mismo (importe, fecha, concepto).</summary>
        public List<EnlacePagoPedido> EnlacesPendientes { get; set; } = new List<EnlacePagoPedido>();
    }

    /// <summary>NestoAPI#734: cuerpo (opcional) de POST api/PedidosVenta/{empresa}/{numero}/ConsultaReembolso.</summary>
    public class PedirConsultaReembolsoRequest
    {
        /// <summary>Opcional: lo que quiera añadir quien pregunta.</summary>
        public string Comentario { get; set; }
        /// <summary>Respaldo cuando no hay usuario autenticado (el servidor prefiere el del token).</summary>
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#734: cuerpo de POST api/PedidosVenta/{empresa}/{numero}/ConsultaReembolso/Respuesta.</summary>
    public class ResponderConsultaReembolsoRequest
    {
        /// <summary>Obligatorio: true = sale con reembolso; false = sale sin reembolso.</summary>
        public bool? ConReembolso { get; set; }
        /// <summary>true = anular los enlaces de pago pendientes del pedido (nunca se anulan solos: pueden ser de otro pedido).</summary>
        public bool? AnularEnlaces { get; set; }
        /// <summary>Opcional, con AnularEnlaces: solo estos (Id de PagosTPV). Null = todos los pendientes vivos del pedido.</summary>
        public List<int> EnlacesAAnular { get; set; }
        /// <summary>Obligatoria con ConReembolso = false: la forma de pago pactada (RCB, TRN…). Nunca TAR sin estar pagado.</summary>
        public string FormaPago { get; set; }
        /// <summary>Opcional con ConReembolso = false: los plazos pactados. Null = los que tiene el pedido. Nunca PRE sin estar pagado.</summary>
        public string PlazosPago { get; set; }
        /// <summary>Opcional: lo que se le dice a quien preguntó.</summary>
        public string Comentario { get; set; }
        /// <summary>Respaldo cuando no hay usuario autenticado.</summary>
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#734: lo que devuelven el POST de la consulta y el de la respuesta.</summary>
    public class RespuestaConsultaReembolsoDTO
    {
        public ConsultaReembolsoDTO Consulta { get; set; }
        /// <summary>POST ConsultaReembolso: la consulta ya estaba abierta y no se ha duplicado.</summary>
        public bool YaExistia { get; set; }
        /// <summary>POST Respuesta: los Id de PagosTPV anulados.</summary>
        public List<int> EnlacesAnulados { get; set; } = new List<int>();
        /// <summary>POST Respuesta: si el pedido ha quedado liberado.</summary>
        public bool Liberado { get; set; }
        /// <summary>Cosas que decirle al usuario (no se ha podido avisar, el envío ya existe y hay que cambiar el reembolso…).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#734: lo que hace falta del pedido para cambiar la forma de pago y avisar.</summary>
    public class DatosPagoPedidoConsulta
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public string CCC { get; set; }
        public string PeriodoFacturacion { get; set; }
        public bool NotaEntrega { get; set; }
        /// <summary>La cuenta bancaria (Clientes.CCC) de la ficha del cliente/contacto del pedido.</summary>
        public string CCCCliente { get; set; }
    }

    /// <summary>NestoAPI#734: una fila de FormasPago.</summary>
    public class FormaPagoConsulta
    {
        public string Numero { get; set; }
        public string Descripcion { get; set; }
        public bool CCCObligatorio { get; set; }
    }
}
