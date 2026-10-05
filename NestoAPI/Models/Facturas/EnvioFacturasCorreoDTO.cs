using System.Collections.Generic;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Nesto#259: POST api/Facturas/EnviarPorCorreo. Las facturas marcadas en la ficha comercial del cliente, al
    /// correo (o correos, separados por «;» o «,») que escribe el usuario. Todas del mismo cliente.
    /// </summary>
    public class EnvioFacturasCorreoDTO
    {
        public string Empresa { get; set; }
        public List<string> Facturas { get; set; }
        public List<string> Correos { get; set; }
    }

    public class ResultadoEnvioFacturasCorreoDTO
    {
        /// <summary>El correo ha salido. False: el servidor de correo no lo ha aceptado (ni al reintentar).</summary>
        public bool Enviado { get; set; }
        /// <summary>Las facturas que van adjuntas.</summary>
        public List<string> Facturas { get; set; } = new List<string>();
        /// <summary>Las que se pidieron y no se han podido adjuntar (no se pudo generar su PDF).</summary>
        public List<string> Omitidas { get; set; } = new List<string>();
        public List<string> Correos { get; set; } = new List<string>();
        public string Mensaje { get; set; }
    }

    /// <summary>Nesto#259: GET api/Facturas/CorreoFacturas: el correo de facturas del cliente, para proponerlo.</summary>
    public class CorreoFacturasDTO
    {
        public string Correo { get; set; }
    }
}
