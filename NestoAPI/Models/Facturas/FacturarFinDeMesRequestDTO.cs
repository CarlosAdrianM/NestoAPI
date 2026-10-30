namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// NestoAPI#727: body de POST api/FacturacionRutas/FinDeMes.
    /// </summary>
    public class FacturarFinDeMesRequestDTO
    {
        /// <summary>Por defecto, la empresa por defecto.</summary>
        public string Empresa { get; set; }

        /// <summary>yyyy-MM-dd. Entran los albaranes de ese día incluido. Por defecto, hoy (todos los albaranes).</summary>
        public string FechaCorte { get; set; }

        /// <summary>Opcional: solo este cliente.</summary>
        public string Cliente { get; set; }

        /// <summary>Opcional (exige Cliente): solo los pedidos de este contacto.</summary>
        public string Contacto { get; set; }
    }
}
