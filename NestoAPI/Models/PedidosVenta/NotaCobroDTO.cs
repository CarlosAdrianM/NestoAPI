using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#786: POST api/NotasEntrega/Cobro, «Nueva nota de cobro» de Nesto (desde el detalle del pedido o la ficha del cliente).
    /// Una nota de solo COBRO: sin líneas ni bultos (no pasa por picking, packing ni furgoneta). Hay que indicar los efectos a cobrar,
    /// el importe o las dos cosas.
    /// </summary>
    public class CrearNotaCobroDTO
    {
        /// <summary>La empresa de la nota (y de su parada): solo la 1 (el reparto propio). Vacío: la 1.</summary>
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        /// <summary>El contacto al que se va a cobrar (la dirección de la parada). Vacío: el del primer efecto.</summary>
        public string Contacto { get; set; }
        /// <summary>
        /// Los Nº Orden de ExtractoCliente que se cobran, en el orden en que se liquidan (como «Cobrar una deuda»). Pueden ser de la
        /// empresa 1 o de la 3 (una de la 3 se cobra siempre en efectivo). Vacío: la nota es solo por importe (al cobrarla, a cuenta).
        /// </summary>
        public List<int> Efectos { get; set; }
        /// <summary>Lo que hay que cobrar. Sin él, la suma de lo pendiente de los efectos. Con efectos, lo que pase de ellos va a cuenta.</summary>
        public decimal? Importe { get; set; }
        /// <summary>Desde qué día sale en el reparto (el día que se va a cobrar). Sin él, hoy; nunca antes de hoy.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>Para el repartidor (va en las observaciones de la parada, recortado a 80 con el importe delante) y en los comentarios del pedido.</summary>
        public string Comentario { get; set; }
    }

    /// <summary>NestoAPI#786: la respuesta (201) de POST api/NotasEntrega/Cobro.</summary>
    public class NotaCobroDTO
    {
        public string Empresa { get; set; }
        /// <summary>El pedido de la nota (CabPedidoVta con NotaEntrega = 1, sin líneas).</summary>
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Ruta { get; set; }
        /// <summary>Lo que hay que cobrar (el reembolso de la parada).</summary>
        public decimal Importe { get; set; }
        public List<EfectoNotaCobroDTO> Efectos { get; set; } = new List<EfectoNotaCobroDTO>();
        /// <summary>«NC{Pedido}».</summary>
        public string CodigoDocumento { get; set; }
        /// <summary>El envío de la agencia 14 que es su parada (ya tramitado, Bultos 0, Reembolso = Importe).</summary>
        public int EnvioId { get; set; }
        /// <summary>El día desde el que sale en el reparto.</summary>
        public DateTime Fecha { get; set; }
        /// <summary>Lo que ve el repartidor («Cobro de 134,16 €» y una línea por efecto).</summary>
        public string Texto { get; set; }
    }

    /// <summary>NestoAPI#786: un efecto (ExtractoCliente) de una nota de cobro, con lo que tiene pendiente AHORA.</summary>
    public class EfectoNotaCobroDTO
    {
        /// <summary>Nº Orden de ExtractoCliente.</summary>
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public DateTime Vencimiento { get; set; }
        public decimal Importe { get; set; }
        public decimal ImportePendiente { get; set; }
    }
}
