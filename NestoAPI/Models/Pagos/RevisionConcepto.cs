using System.Collections.Generic;

namespace NestoAPI.Models.Pagos
{
    /// <summary>
    /// NestoAPI#609: petición de <c>POST api/Pagos/RevisarConcepto</c>. Contrato común con Nesto y NestoApp#216.
    /// </summary>
    public class SolicitudRevisarConcepto
    {
        public string Concepto { get; set; }
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;

        /// <summary>Opcional. Hoy no cambia la propuesta; queda en el contrato para afinar el glosario por cliente.</summary>
        public string Cliente { get; set; }
    }

    /// <summary>
    /// NestoAPI#609: propuesta de corrección del concepto. El cliente enseña «¿Quisiste decir…?» solo si
    /// <see cref="HayCambios"/>; si el usuario la acepta, manda <see cref="Propuesto"/> al crear el enlace.
    /// </summary>
    public class RespuestaRevisarConcepto
    {
        public string Original { get; set; }
        public string Propuesto { get; set; }

        /// <summary>
        /// True si la propuesta cambia algo más de lo que ya arregla por sí solo el alta del enlace
        /// (<c>FormateadorConcepto.Normalizar</c>, mayúsculas/minúsculas).
        /// </summary>
        public bool HayCambios { get; set; }

        /// <summary>Diferencias palabra a palabra (Original → Propuesto), para resaltarlas.</summary>
        public List<CambioConcepto> Cambios { get; set; } = new List<CambioConcepto>();
    }

    public class CambioConcepto
    {
        public string De { get; set; }
        public string A { get; set; }
    }
}
