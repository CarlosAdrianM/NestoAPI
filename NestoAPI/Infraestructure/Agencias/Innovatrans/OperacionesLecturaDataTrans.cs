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

    /// <summary>Un evento de seguimiento de DataTrans (ConsultarEstados). El estado lo identifica
    /// <see cref="Nombre"/> (descriptivo: DOCUMENTADO, EN TRÁNSITO, ENTREGADO…); <see cref="Codigo"/>
    /// es opaco. <see cref="Numero"/> es el orden del evento.</summary>
    public class EstadoEnvioDataTrans
    {
        public int? Numero { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Fecha { get; set; } // dd/MM/yyyy
        public string Hora { get; set; }  // HH:mm:ss
    }

    /// <summary>Resultado de ConsultarEstados. <see cref="Respuesta"/>: 200 ok, 300 sin registros, 400 auth mal.</summary>
    public class ResultadoConsultaEstados
    {
        public int Respuesta { get; set; }
        public List<EstadoEnvioDataTrans> Estados { get; set; } = new List<EstadoEnvioDataTrans>();
    }

    /// <summary>Una incidencia de DataTrans (ConsultarIncidencias). <see cref="Resuelta"/> = ya cerrada.</summary>
    public class IncidenciaDataTrans
    {
        public int? Numero { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public bool Resuelta { get; set; }
        public string Fecha { get; set; }
        public string Hora { get; set; }
        public string FechaCierre { get; set; }
    }

    /// <summary>Resultado de ConsultarIncidencias. <see cref="Respuesta"/>: 200 ok, 300 sin registros, 400 auth mal.</summary>
    public class ResultadoConsultaIncidencias
    {
        public int Respuesta { get; set; }
        public List<IncidenciaDataTrans> Incidencias { get; set; } = new List<IncidenciaDataTrans>();
    }

    /// <summary>
    /// Operaciones de LECTURA del WebService DataTrans DTX (solo lectura: no crean ni modifican
    /// envíos, seguras en producción). Expone BuscarPoblacion (servicio Poblaciones), ConsultarEstados
    /// (servicio Estados) y ConsultarIncidencias (servicio Incidencias) para el poll de seguimiento.
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

        /// <summary>
        /// Consulta los estados de seguimiento de un albarán (servicio Estados, operación
        /// ConsultarEstados, buscar=1=por albarán). Solo lectura.
        /// </summary>
        public async Task<ResultadoConsultaEstados> ConsultarEstadosAsync(string albaran)
        {
            XDocument doc = await _cliente.EjecutarAsync(
                "Estados",
                "ConsultarEstados",
                new XElement(ClienteSoapDataTrans.Mes + "albaran", albaran),
                new XElement(ClienteSoapDataTrans.Mes + "buscar", "1")).ConfigureAwait(false);

            return new ResultadoConsultaEstados
            {
                Respuesta = LeerEntero(doc, "respuesta"),
                Estados = doc.Descendants()
                    .Where(e => e.Name.LocalName == "resultado")
                    .Select(r => new EstadoEnvioDataTrans
                    {
                        Numero = ParsearEntero(ValorHijo(r, "numero")),
                        Codigo = ValorHijo(r, "codigo"),
                        Nombre = ValorHijo(r, "nombre"),
                        Fecha = ValorHijo(r, "fecha"),
                        Hora = ValorHijo(r, "hora")
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// Consulta las incidencias de un albarán (servicio Incidencias, operación ConsultarIncidencias).
        /// Solo lectura. <c>resuelta</c> llega como "1"/"0".
        /// IMPORTANTE: a diferencia de ConsultarEstados, el WSDL de Incidencias NO define el subelemento
        /// <c>buscar</c> (ver doc oficial DTX, ConsultarIncidenciasTypeIn = autenticacion + albaran). Enviarlo
        /// provoca HTTP 500 "Unexpected subelement buscar" en Axis2 y tumba todo el seguimiento (issue #254).
        /// </summary>
        public async Task<ResultadoConsultaIncidencias> ConsultarIncidenciasAsync(string albaran)
        {
            XDocument doc = await _cliente.EjecutarAsync(
                "Incidencias",
                "ConsultarIncidencias",
                new XElement(ClienteSoapDataTrans.Mes + "albaran", albaran)).ConfigureAwait(false);

            return new ResultadoConsultaIncidencias
            {
                Respuesta = LeerEntero(doc, "respuesta"),
                Incidencias = doc.Descendants()
                    .Where(e => e.Name.LocalName == "resultado")
                    .Select(r => new IncidenciaDataTrans
                    {
                        Numero = ParsearEntero(ValorHijo(r, "numero")),
                        Codigo = ValorHijo(r, "codigo"),
                        Nombre = ValorHijo(r, "nombre"),
                        Resuelta = ValorHijo(r, "resuelta") == "1",
                        Fecha = ValorHijo(r, "fecha"),
                        Hora = ValorHijo(r, "hora"),
                        FechaCierre = ValorHijo(r, "fechaCierre")
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

        private static int? ParsearEntero(string texto)
            => int.TryParse(texto, out int valor) ? valor : (int?)null;
    }
}
