using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>Una población devuelta por BuscarPoblacion (CP -> población/kilómetros).</summary>
    public class PoblacionDataTrans
    {
        public string Poblacion { get; set; }
        public decimal? Kilometros { get; set; }
    }

    /// <summary>
    /// Resultado de BuscarPoblacion. <see cref="Respuesta"/>: 200 = ok, 300 = sin registros para el CP,
    /// 400 = datos de autenticación incorrectos. Los códigos 200/300 confirman que la conexión y las
    /// credenciales son válidas; el 400 indica que la clave (MD5) o el usuario no cuadran.
    /// </summary>
    public class ResultadoBuscarPoblacion
    {
        public int Respuesta { get; set; }
        public string MensajeError { get; set; }
        public List<PoblacionDataTrans> Poblaciones { get; set; } = new List<PoblacionDataTrans>();
    }

    /// <summary>
    /// Operaciones de LECTURA del WebService DataTrans DTX. Son seguras (no crean ni modifican
    /// envíos), así que sirven para probar conectividad/credenciales contra producción. De momento
    /// expone BuscarPoblacion (servicio Poblaciones); el seguimiento de estados (ConsultarEstados)
    /// se construirá aquí mismo cuando abordemos el polling.
    /// </summary>
    public class OperacionesLecturaDataTrans
    {
        private readonly IClienteSoapDataTrans _cliente;

        public OperacionesLecturaDataTrans(IClienteSoapDataTrans cliente)
        {
            _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        }

        /// <summary>
        /// Consulta las poblaciones de un código postal. Operación de solo lectura, ideal como
        /// prueba de conectividad: si DataTrans responde 200/300 la autenticación es correcta.
        /// </summary>
        public async Task<ResultadoBuscarPoblacion> BuscarPoblacionAsync(string codigoPostal)
        {
            XDocument doc = await _cliente.EjecutarAsync(
                "Poblaciones",
                "BuscarPoblacion",
                new XElement(ClienteSoapDataTrans.Mes + "codigoPostal", codigoPostal)).ConfigureAwait(false);

            return new ResultadoBuscarPoblacion
            {
                Respuesta = LeerEntero(doc, "respuesta"),
                MensajeError = LeerTexto(doc, "msgError"),
                Poblaciones = doc.Descendants()
                    .Where(e => e.Name.LocalName == "resultado")
                    .Select(r => new PoblacionDataTrans
                    {
                        Poblacion = ValorHijo(r, "poblacion"),
                        Kilometros = ParsearDecimal(ValorHijo(r, "kilometros"))
                    })
                    .ToList()
            };
        }

        private static string LeerTexto(XDocument doc, string nombreLocal)
            => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == nombreLocal)?.Value;

        private static int LeerEntero(XDocument doc, string nombreLocal)
            => int.TryParse(LeerTexto(doc, nombreLocal), out int valor) ? valor : 0;

        private static string ValorHijo(XElement elemento, string nombreLocal)
            => elemento.Elements().FirstOrDefault(e => e.Name.LocalName == nombreLocal)?.Value;

        private static decimal? ParsearDecimal(string texto)
            => decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valor) ? valor : (decimal?)null;
    }
}
