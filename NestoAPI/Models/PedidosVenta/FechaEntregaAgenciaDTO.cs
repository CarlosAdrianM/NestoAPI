using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#606: cuándo entregamos el pedido a la agencia. Lo devuelven
    /// <c>GET api/PedidosVenta/{empresa}/{numero}/FechaEntregaAgencia</c> (detalle del pedido) y
    /// <c>POST api/PedidosVenta/FechaEntregaAgencia</c> (plantilla, sin pedido creado). Las fechas son días, sin hora.
    /// </summary>
    public class FechaEntregaAgenciaDTO
    {
        /// <summary>Día de la primera entrega a la agencia. Null = sin fecha (o nada sale de Algete por agencia).</summary>
        public DateTime? PrimeraEntrega { get; set; }
        /// <summary>Día de la entrega con la que queda todo servido. Null = algo no tiene fecha.</summary>
        public DateTime? EntregaCompleta { get; set; }
        /// <summary>La que hay que enseñar: la completa en «Todo junto»; la primera en el resto de modos.</summary>
        public DateTime? FechaEntregaAgencia { get; set; }
        /// <summary>"Completa" o "Primera": cuál de las dos es <see cref="FechaEntregaAgencia"/>.</summary>
        public string Aplica { get; set; }
        /// <summary>Todos los días en que sale algo, en orden.</summary>
        public List<DateTime> Entregas { get; set; } = new List<DateTime>();
        /// <summary>Por qué, en castellano (para el tooltip).</summary>
        public string Motivo { get; set; }
        /// <summary>La fecha que se dio al crear el pedido (CabPedidoVta.FechaEntregaAgenciaPrometida). Null en la plantilla,
        /// en los pedidos anteriores a la columna y en los que nacieron sin fecha.</summary>
        public DateTime? FechaPrometida { get; set; }

        public const string APLICA_PRIMERA = "Primera";
        public const string APLICA_COMPLETA = "Completa";
    }
}
