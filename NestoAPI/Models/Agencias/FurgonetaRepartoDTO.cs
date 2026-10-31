using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Agencias
{
    /// <summary>NestoAPI#772: GET api/RepartoPropio/Furgoneta. Lo que lleva encima un repartidor (o todos).</summary>
    public class FurgonetaRepartoDTO
    {
        /// <summary>El repartidor, sin dominio. Null con todos=true.</summary>
        public string Usuario { get; set; }
        public int Total { get; set; }
        /// <summary>Cuándo terminó hoy su jornada (la última vez). Null si no la ha terminado.</summary>
        public DateTime? JornadaTerminada { get; set; }
        public List<BultoFurgonetaDTO> Bultos { get; set; } = new List<BultoFurgonetaDTO>();
    }

    /// <summary>NestoAPI#772: un bulto en una furgoneta (su último movimiento es Cargado o Recogido).</summary>
    public class BultoFurgonetaDTO
    {
        /// <summary>«RP251300-1», «NR929554-2», «R81010-3».</summary>
        public string Codigo { get; set; }
        /// <summary>«RP», «NR» o «R».</summary>
        public string TipoCodigo { get; set; }
        /// <summary>«Cargado» o «Recogido».</summary>
        public string Tipo { get; set; }
        /// <summary>Quién lo lleva, sin dominio.</summary>
        public string Usuario { get; set; }
        /// <summary>RP/NR: el envío; R cargado para una tienda: el EnvioId (negativo) de la parada de reposición.</summary>
        public int? EnvioId { get; set; }
        public int? Pedido { get; set; }
        public int? Traspaso { get; set; }
        public int Bulto { get; set; }
        public int? Total { get; set; }
        public string Descripcion { get; set; }
        public DateTime FechaHora { get; set; }
    }

    /// <summary>NestoAPI#772: POST api/RepartoPropio/Recoger y POST api/RepartoPropio/Descargar.</summary>
    public class MovimientoBultoRepartoDTO
    {
        /// <summary>Lo leído: NR{pedido}-{N} o R{traspaso}-{N} al recoger; cualquier bulto al descargar.</summary>
        public string Codigo { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        /// <summary>Hora local de la PDA (aunque llegue más tarde por falta de cobertura). Sin ella, ahora.</summary>
        public DateTime? FechaHora { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
    }

    /// <summary>NestoAPI#772: la respuesta de Recoger y Descargar.</summary>
    public class ResultadoMovimientoBultoDTO
    {
        public string Codigo { get; set; }
        public string TipoCodigo { get; set; }
        /// <summary>«Recogido» o «Descargado».</summary>
        public string Tipo { get; set; }
        public int Bulto { get; set; }
        public int? Total { get; set; }
        public int? EnvioId { get; set; }
        public int? Pedido { get; set; }
        public int? Traspaso { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Al recoger: los bultos distintos ya recogidos de ese traspaso o de esa nota.</summary>
        public int? BultosRecogidos { get; set; }
        /// <summary>True cuando con este bulto se completa la reposición y queda «Recogida».</summary>
        public bool ReposicionRecogida { get; set; }
        /// <summary>Ya estaba así (lo llevaba él al recoger, o ya estaba descargado): no se ha apuntado nada nuevo.</summary>
        public bool YaEstaba { get; set; }
        /// <summary>Cuántos bultos lleva ahora el que lo ha leído.</summary>
        public int EnFurgoneta { get; set; }
        /// <summary>Para enseñar tal cual, de usted.</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#772: POST api/RepartoPropio/Jornada/Terminar.</summary>
    public class TerminarJornadaRepartoDTO
    {
        public Guid ClaveIdempotencia { get; set; }
        public DateTime? FechaHora { get; set; }
        /// <summary>Terminar aunque queden bultos en la furgoneta (avisa a Almacén, Dirección y al repartidor).</summary>
        public bool Confirmar { get; set; }
    }

    public class ResultadoJornadaRepartoDTO
    {
        public bool Terminada { get; set; }
        public List<BultoFurgonetaDTO> Pendientes { get; set; } = new List<BultoFurgonetaDTO>();
        /// <summary>A quién se ha avisado («Almacén», «Dirección», «Andre»). Vacía si no hacía falta.</summary>
        public List<string> Avisados { get; set; } = new List<string>();
        public string Mensaje { get; set; }
    }
}
