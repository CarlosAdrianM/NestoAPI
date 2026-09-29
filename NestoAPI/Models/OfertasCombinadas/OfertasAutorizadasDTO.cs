using NestoAPI.Models.OfertasEscalonadas;
using System.Collections.Generic;

namespace NestoAPI.Models.OfertasCombinadas
{
    /// <summary>
    /// NestoAPI#233: las ofertas autorizadas vigentes de las tres pestañas del mantenimiento de
    /// Nesto, para la pantalla de solo lectura de NestoApp (NestoApp#137). Cada lista lleva el
    /// mismo DTO que su endpoint de siempre, así que NestoApp no tiene que cambiar sus modelos.
    /// </summary>
    public class OfertasAutorizadasDTO
    {
        public List<OfertaCombinadaDTO> Combinadas { get; set; } = new List<OfertaCombinadaDTO>();
        /// <summary>Solo las que permiten el N+M: las denegaciones (NestoAPI#564) no son ofertas.</summary>
        public List<OfertaPermitidaFamiliaDTO> Familias { get; set; } = new List<OfertaPermitidaFamiliaDTO>();
        public List<OfertaEscalonadaDTO> Escalonadas { get; set; } = new List<OfertaEscalonadaDTO>();
    }

    /// <summary>NestoAPI#233: lo que se ha mandado a los vendedores y a cuántos dispositivos ha llegado.</summary>
    public class ResultadoInformarVendedoresDTO
    {
        public string Titulo { get; set; }
        public string Cuerpo { get; set; }
        public string Ruta { get; set; }
        public int DispositivosNotificados { get; set; }
    }
}
